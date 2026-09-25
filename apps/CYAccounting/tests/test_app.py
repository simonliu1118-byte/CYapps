from __future__ import annotations

import tempfile
import json
import sqlite3
from pathlib import Path
from datetime import date
from unittest.mock import patch

from PySide6.QtWidgets import QApplication, QLabel, QGroupBox, QPushButton
from PySide6.QtGui import QColor, QPalette
from PySide6.QtCore import Qt

import sys
sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'app'))

from db import Database
import main as main_module
import dialogs as dialogs_module
import util as util_module
from main import MainWindow
from dialogs import AccountManagerDialog, CategoryManagerDialog, OpeningBalanceDialog, SettingsDialog
from import_dialog import ImportTransactionsDialog
from background import start_background_task
from widgets import CategoryComboBox
from util import normalize_date_input, shift_month, APP_VERSION, APP_RELEASE_DATE


def reset_test_transactions(db: Database) -> None:
    # Test fixture only. The application no longer offers a clear-all operation.
    with db.tx() as conn:
        conn.execute('DELETE FROM transactions')
        conn.execute('DELETE FROM opening_balances')


def test_locale_constructor_compatibility():
    from PySide6.QtCore import QLocale
    locale = QLocale("zh_TW")
    QLocale.setDefault(locale)
    assert locale.name().lower().replace('-', '_').startswith('zh_tw')


def test_database_core():
    root = Path(tempfile.mkdtemp())
    db = Database(root / 'test.db')
    db.add_account('銀行')
    db.set_opening_balances('2026/07', {'現金': 1000, '銀行': -200})
    db.save_transaction('2026/07/02', '現金', 'expense', '一般支出', 'B', 50)
    db.save_transaction('2026/07/01', '銀行', 'income', '一般收入', 'A', 100)
    db.save_transaction('2026/07/01', '現金', 'income', '一般收入', 'A', 300)
    data = db.month_data('2026/07')
    assert [r['tx_date'] for r in data['rows']] == ['2026/07/01', '2026/07/01', '2026/07/02']
    assert [r['amount'] for r in data['rows'][:2]] == [100, 300]
    assert data['opening_total'] == 800
    assert data['income_total'] == 400
    assert data['expense_total'] == 50
    assert data['ending_total'] == 1150

    # Deleted historical accounts are not carried to a new month.
    bank_id = next(a['id'] for a in db.accounts() if a['name'] == '銀行')
    db.delete_account(bank_id)
    carry = db.previous_month_carry_values('2026/08')
    assert set(carry) == {'現金'}

    # Lock through month blocks all prior writes.
    db.set_locked_through('2026/07')
    assert not db.save_transaction('2026/07/31', '現金', 'income', '一般收入', '', 1).ok
    assert db.save_transaction('2026/08/01', '現金', 'income', '一般收入', '', 1).ok

    # Reset this test's records; the user's master data and month lock remain.
    reset_test_transactions(db)
    assert db.counts() == {'transactions': 0, 'openings': 0}
    assert db.locked_through() == '2026/07'
    assert len(db.accounts()) == 1
    assert db.category_names('income')
    income_id = db.category_tree('income')[0]['categories'][0]['id']
    db.set_category_favorite(income_id, True)
    assert db.favorite_category_names('income') == ['一般收入']
    db.set_category_favorite(income_id, False)
    assert db.favorite_category_names('income') == []

    # Favorite categories allow up to 10 per income/expense side.
    group_id = db.category_tree('income')[0]['id']
    extra_ids = []
    for idx in range(10):
        name = f'常用{idx + 1}'
        db.add_category('income', group_id, name)
        cid = next(c['id'] for g in db.category_tree('income') for c in g['categories'] if c['name'] == name)
        db.set_category_favorite(cid, True)
        extra_ids.append(cid)
    assert len(db.favorite_category_names('income')) == 10
    db.add_category('income', group_id, '第十一個')
    eleventh_id = next(c['id'] for g in db.category_tree('income') for c in g['categories'] if c['name'] == '第十一個')
    try:
        db.set_category_favorite(eleventh_id, True)
        assert False, 'the 11th favorite should be rejected'
    except Exception as exc:
        assert '10' in str(exc)

    # Frequent summaries are account + category specific and ranked by frequency, then recency.
    reset_test_transactions(db)
    for summary in ['甲', '乙', '甲', '乙', '乙', '甲', '乙', '丙']:
        db.save_transaction('2026/08/01', '現金', 'income', '一般收入', summary, 1)
    db.add_account('中信')
    for _ in range(5):
        db.save_transaction('2026/08/02', '中信', 'income', '一般收入', '中信專用', 1)
    assert db.frequent_summaries('income', '現金', '一般收入', 100, 3, 10, 'tx_date')[:2] == ['乙', '甲']
    assert db.frequent_summaries('income', '中信', '一般收入', 100, 3, 10, 'tx_date') == ['中信專用']

    # Accounting-date and recent-entry sampling can intentionally differ.
    reset_test_transactions(db)
    for _ in range(3):
        db.save_transaction('2026/09/01', '現金', 'income', '一般收入', '新日期', 1)
    for _ in range(3):
        db.save_transaction('2026/08/01', '現金', 'income', '一般收入', '後補輸入', 1)
    assert db.frequent_summaries('income', '現金', '一般收入', 3, 3, 10, 'tx_date') == ['新日期']
    assert db.frequent_summaries('income', '現金', '一般收入', 3, 3, 10, 'created_at') == ['後補輸入']

    # Backup is a valid application database.
    backup = root / 'backup.db'
    db.backup_to(backup)
    assert db.validate_database_file(backup)[0]
    db.close()



def test_account_manager_controls_and_titles():
    root = Path(tempfile.mkdtemp())
    db = Database(root / 'accounts.db')
    app = QApplication.instance() or QApplication([])
    dlg = AccountManagerDialog(db)
    button_texts = [b.text() for b in dlg.findChildren(QPushButton)]
    visible_order = [text for text in button_texts if text in {'↑', '↓', '新增', '修改', '刪除'}]
    assert visible_order == ['↑', '↓', '新增', '修改', '刪除']
    assert '關閉' not in button_texts
    assert dlg.windowTitle() == '帳戶管理'
    old_palette = app.palette()
    old_style = app.styleSheet()
    try:
        dark_palette = QPalette(old_palette)
        dark_palette.setColor(QPalette.ColorRole.Base, QColor('#202020'))
        dark_palette.setColor(QPalette.ColorRole.Text, QColor('#202020'))
        app.setPalette(dark_palette)
        app.setStyleSheet(main_module.APP_STYLE)
        dlg.list.ensurePolished()
        assert dlg.list.palette().color(QPalette.ColorRole.Base).name() == '#ffffff'
        assert dlg.list.palette().color(QPalette.ColorRole.Text).name() == '#1f2937'
    finally:
        app.setStyleSheet(old_style)
        app.setPalette(old_palette)
    category_dlg = CategoryManagerDialog(db)
    category_buttons = [b.text() for b in category_dlg.findChildren(QPushButton)]
    assert '關閉' not in category_buttons
    assert {'↑', '↓', '新增科目', '修改', '刪除', '新增大分類', '移至其他大分類'} <= set(category_buttons)
    combo = CategoryComboBox()
    assert combo.lineEdit().hasFrame() is False
    opening_dlg = OpeningBalanceDialog(db, '2026/07')
    assert opening_dlg.width() <= 330
    assert dlg.width() <= 350
    assert category_dlg.width() <= 410
    dlg.close()
    category_dlg.close()
    opening_dlg.close()
    db.close()

def test_clear_data_requires_two_delete_confirmations():
    root = Path(tempfile.mkdtemp())
    db = Database(root / 'confirm.db')
    db.set_opening_balances('2026/07', {'現金': 500})
    assert db.save_transaction('2026/07/01', '現金', 'income', '一般收入', '測試', 30).ok
    db.add_account('銀行')
    db.add_category('income', db.category_tree('income')[0]['id'], '自訂科目')
    db.set_locked_through('2026/07')
    backup_path = root / 'before_clear.db'
    db.backup_to(backup_path)
    app = QApplication.instance() or QApplication([])
    dlg = SettingsDialog(db, {}, lambda _p: (True, ''), lambda _p: (True, ''),
                         db.reset_local_ledger, lambda: None)
    assert [b.text() for b in dlg.findChildren(QPushButton)].count('清除所有記帳資料與期初餘額') == 1
    for replies, prompts in [
        ([('delete', True)], 1),
        ([('DELETE', True), ('delete', True)], 2),
        ([('DELETE', True), ('DELETE', False)], 2),
        ([('DELETE', False)], 1),
    ]:
        with patch.object(dialogs_module.QInputDialog, 'getText', side_effect=replies) as get_text, \
             patch.object(dialogs_module.QMessageBox, 'warning'):
            dlg.clear_data()
            assert get_text.call_count == prompts
        assert db.counts() == {'transactions': 1, 'openings': 1}
    with patch.object(dialogs_module.QInputDialog, 'getText', side_effect=[('DELETE', True), ('DELETE', True)]) as get_text, \
         patch.object(dialogs_module.QMessageBox, 'information'):
        dlg.clear_data()
        assert get_text.call_count == 2
    assert db.counts() == {'transactions': 0, 'openings': 0}
    assert db.locked_through() is None
    assert [a['name'] for a in db.accounts()] == ['現金']
    assert db.category_names('income') == ['一般收入']
    assert db.category_names('expense') == ['一般支出']
    assert db.validate_database_file(backup_path)[0]
    dlg.close()
    db.close()

def test_full_clear_resets_settings_and_preserves_recovery_backup():
    root = Path(tempfile.mkdtemp())
    db = Database(root / 'ledger.db')
    db.add_account('銀行')
    db.set_opening_balances('2026/09', {'現金': 20, '銀行': 50})
    assert db.save_transaction('2026/09/01', '銀行', 'income', '一般收入', '舊帳', 100).ok
    db.set_locked_through('2026/09')
    original_config = {'database_path': str(db.path), 'google_drive_sync_enabled': True,
                       'google_refresh_token': 'test-only', 'ledger_entry_position': 'oldest'}
    config = dict(original_config)
    config_file = root / 'config.json'
    app = QApplication.instance() or QApplication([])
    win = MainWindow(db, config, None)
    with patch.object(util_module, 'config_path', return_value=config_file), \
         patch.object(main_module, 'config_path', return_value=config_file):
        util_module.save_config(config)
        win.clear_data()
        assert config == {'database_path': str(db.path)}
        assert json.loads(config_file.read_text(encoding='utf-8')) == config
        assert json.loads((root / 'config.json.bak').read_text(encoding='utf-8')) == config
        assert db.counts() == {'transactions': 0, 'openings': 0}
        assert [row['name'] for row in db.accounts()] == ['現金']
        assert db.locked_through() is None
        backups = list((root / 'RestoreBackup').glob('CYacc_beforeclear_*.db'))
        assert len(backups) == 1
        with sqlite3.connect(backups[0]) as archived:
            assert archived.execute('SELECT COUNT(*) FROM transactions').fetchone()[0] == 1
            assert archived.execute('SELECT COUNT(*) FROM opening_balances').fetchone()[0] == 2
        # If settings cannot be written, do not strand the user with an empty
        # ledger: restore both the database and in-memory preferences.
        db.set_locked_through('2026/09')
        assert db.save_transaction('2026/10/01', '現金', 'income', '一般收入', '保留', 1).ok
        before_failure = dict(config)
        with patch.object(main_module, 'save_config', side_effect=OSError('test write failure')):
            try:
                win.clear_data()
                assert False, 'a failed settings write must abort the reset'
            except OSError:
                pass
        assert config == before_failure
        assert db.counts()['transactions'] == 1
    win.close()

def test_ui_constructs():
    root = Path(tempfile.mkdtemp())
    db = Database(root / 'ui.db')
    db.save_transaction('2026/07/01', '現金', 'income', '一般收入', '測試收入', 13500)
    app = QApplication.instance() or QApplication([])
    config = {}
    win = MainWindow(db, config, None)
    assert win.windowTitle() == '志遠記帳系統'
    assert win.tab_bar.count() == 2
    assert win.pages.count() == 2
    assert win.input_tab.date_edit.hasFocus() is False  # focus is queued
    assert '2026/07' in win.ledger_tab.table_title.text()
    assert len(win.input_tab.confirm_labels) == 10
    assert win.minimumWidth() == 1120
    assert win.minimumHeight() == 710
    selected_button = win.input_tab.account_buttons[win.input_tab.selected_account]
    assert selected_button.isChecked()
    assert win.input_tab.income_amount.maxLength() == 7
    assert win.input_tab.expense_amount.maxLength() == 7
    assert win.input_tab.income_amount.placeholderText() == '最多7位'
    assert win.input_tab.income_quick_summary_host.height() == 28
    assert win.input_tab.expense_quick_summary_host.height() == 28

    # Blank summaries are shown explicitly in the transient confirmation list.
    win.input_tab.date_edit.setText('2026/07/02')
    win.input_tab.income_summary.clear()
    win.input_tab.income_amount.setText('1')
    win.input_tab.save_entry('income')
    assert '(空白)' in win.input_tab.confirm_lines[0]
    assert '#98A2B3' in win.input_tab.confirm_lines[0]
    assert '[現金]' in win.input_tab.confirm_lines[0]
    assert 'background-color:#EDF5F2' in win.input_tab.confirm_lines[0]
    assert '｜' not in win.input_tab.confirm_lines[0]
    assert '$1' in win.input_tab.confirm_lines[0]
    assert '&lt;存檔成功&gt;' in win.input_tab.confirm_lines[0]

    assert normalize_date_input('20260901') == '2026/09/01'
    assert normalize_date_input('2026/0209') == '2026/02/09'
    assert normalize_date_input('202602/09') == '2026/02/09'

    # Settings expose the ledger entry-position preference and update the shared config.
    dlg = SettingsDialog(
        db, config, lambda _p: (True, ''), lambda _p: (True, ''),
        lambda: None, lambda: None, win,
    )
    assert dlg.ledger_position.count() == 2
    oldest_index = dlg.ledger_position.findData('oldest')
    dlg.ledger_position.setCurrentIndex(oldest_index)
    assert config['ledger_entry_position'] == 'oldest'
    assert dlg.common_summary_basis.currentData() == 'tx_date'
    assert dlg.common_summary_recent.text() == '100'
    assert dlg.common_summary_min.text() == '3'
    assert dlg.common_summary_recent.findChild(QPushButton) is None
    assert dlg.common_summary_min.findChild(QPushButton) is None
    # The settings page intentionally remains an administrator override that
    # may move the lock backward after an explicit warning.
    db.set_locked_through('2026/07')
    dlg.lock_month.set_month('2026/06')
    with patch.object(main_module.QMessageBox, 'warning', return_value=main_module.QMessageBox.StandardButton.Yes):
        dlg.apply_lock()
    assert db.locked_through() == '2026/06'
    # Account header toggles a temporary account-grouped view with account balances.
    win.ledger_tab._set_account_sort_mode(True)
    assert win.ledger_tab.account_sort_mode is True
    assert win.ledger_tab.model.account_view is True
    assert win.ledger_tab.model.headerData(1, __import__('PySide6').QtCore.Qt.Orientation.Horizontal) == '帳戶 ▲'
    assert win.ledger_tab.model.headerData(9, __import__('PySide6').QtCore.Qt.Orientation.Horizontal) == '帳戶餘額'
    win.ledger_tab._set_account_sort_mode(False)
    assert win.ledger_tab.model.account_view is False

    # A real workbook exercises header detection, mapping and row normalization.
    from openpyxl import Workbook
    book_path = root / 'import.xlsx'
    book = Workbook()
    sheet = book.active
    sheet.append(['日期', '帳戶', '收入科目', '收入摘要', '收入金額', '支出科目', '支出摘要', '支出金額'])
    sheet.append(['2026/08/02', '現金', '一般收入', 'Excel收入', 9999999, '', '', None])
    sheet.append(['2026/08/03', '現金', '', '', None, '一般支出', '超額', 10000000])
    book.save(book_path)
    import_dlg = ImportTransactionsDialog(db, config, win)
    import_dlg.load_file(book_path, book_path.name)
    parsed = list(import_dlg._iter_rows())
    assert parsed[0][1]['amount'] == 9999999 and parsed[0][2] is None
    assert parsed[1][1] is None and parsed[1][2] == '金額最多7位數'
    import_dlg.close()

    # Blocking work returns to the UI thread through the background relay.
    result_box = []
    thread = start_background_task(lambda: '完成', lambda result, error: result_box.append((result, error)))
    from time import monotonic
    until = monotonic() + 3
    while not result_box and monotonic() < until:
        app.processEvents()
    app.processEvents()
    assert result_box == [('完成', None)]
    thread.quit()
    assert thread.wait(1000)
    dlg.close()
    win.close()


def test_backup_schedule_and_recovery_paths():
    root = Path(tempfile.mkdtemp())
    db = Database(root / 'live.db')
    db.save_transaction('2026/09/01', '現金', 'income', '一般收入', '原資料', 100)
    app = QApplication.instance() or QApplication([])
    config = {}
    win = MainWindow(db, config, None)

    # Failed location switching returns to the original live database.
    original_path = db.path
    original_open = db.open
    calls = {'count': 0}
    def fail_first_open():
        calls['count'] += 1
        if calls['count'] == 1:
            raise RuntimeError('simulated target open failure')
        return original_open()
    db.open = fail_first_open
    ok, _message = win.change_database_location(root / 'new-location')
    assert not ok
    assert db.path == original_path
    assert db.month_data('2026/09')['rows'][0]['summary'] == '原資料'
    db.open = original_open

    # Failed restore after replacement rolls the original database back.
    source_db = Database(root / 'source.db')
    source_db.save_transaction('2026/09/02', '現金', 'expense', '一般支出', '備份資料', 20)
    source = root / 'source-backup.db'
    source_db.backup_to(source)
    source_db.close()
    calls['count'] = 0
    def fail_first_restore_open():
        calls['count'] += 1
        if calls['count'] == 1:
            raise RuntimeError('simulated restored DB open failure')
        return original_open()
    db.open = fail_first_restore_open
    ok, _message = win.restore_database(source)
    assert not ok
    db.open = original_open
    assert db.month_data('2026/09')['rows'][0]['summary'] == '原資料'

    # Automatic backup is due every three days and retains exactly 30 names.
    class FixedDate(date):
        @classmethod
        def today(cls):
            return cls(2026, 9, 11)
    backup_dir = db.path.parent / 'AutoBackup'
    backup_dir.mkdir(exist_ok=True)
    for day in range(1, 33):
        (backup_dir / f'CYaccbkup_202608{day:02d}.db').write_bytes(b'old')
    old_date = main_module.date
    old_save_config = main_module.save_config
    main_module.date = FixedDate
    main_module.save_config = lambda _config: None
    try:
        assert main_module.run_auto_backup(db, config) is None
        assert config['last_auto_backup_date'] == '2026/09/11'
        assert len(list(backup_dir.glob('CYaccbkup_*.db'))) == 30
        assert main_module.run_auto_backup(db, config) is None
    finally:
        main_module.date = old_date
        main_module.save_config = old_save_config

    win.close()


def test_maximized_window_state_is_restored():
    root = Path(tempfile.mkdtemp())
    db = Database(root / 'max.db')
    app = QApplication.instance() or QApplication([])
    win = MainWindow(db, {'window_maximized': True}, None)
    app.processEvents()
    assert win.isMaximized()
    win.showNormal()
    win.close()


if __name__ == '__main__':
    test_locale_constructor_compatibility()
    test_database_core()
    test_account_manager_controls_and_titles()
    test_clear_data_requires_two_delete_confirmations()
    test_full_clear_resets_settings_and_preserves_recovery_backup()
    test_ui_constructs()
    test_backup_schedule_and_recovery_paths()
    test_maximized_window_state_is_restored()
    print('ALL TESTS PASSED')


def test_phase1_section_group_titles_are_effectively_bold():
    root = Path(tempfile.mkdtemp())
    db = Database(root / 'visual-groups.db')
    app = QApplication.instance() or QApplication([])
    old_style = app.styleSheet()
    try:
        app.setStyleSheet(main_module.APP_STYLE)

        input_tab = main_module.InputTab(db, {}, lambda _month: None)
        input_groups = {g.title(): g for g in input_tab.findChildren(QGroupBox)}
        for title in ('基本資訊', '收入', '支出', '輸入確認'):
            group = input_groups[title]
            group.ensurePolished()
            assert group.font().bold(), title
            labels = group.findChildren(QLabel)
            if labels:
                labels[0].ensurePolished()
                assert not labels[0].font().bold(), f'{title} child text must remain regular'

        settings = SettingsDialog(db, {}, lambda _p: (True, ''), lambda _p: (True, ''),
                                  db.reset_local_ledger, lambda: None)
        for group in settings.findChildren(QGroupBox):
            group.ensurePolished()
            assert group.font().bold(), group.title()

        importer = ImportTransactionsDialog(db, {})
        import_groups = {g.title(): g for g in importer.findChildren(QGroupBox)}
        for title in ('匯入來源', '欄位對應', '匯入預覽（前 20 筆）'):
            group = import_groups[title]
            group.ensurePolished()
            assert group.font().bold(), title

        chrome = main_module.ChineseStandardButtonFilter(app)
        account = AccountManagerDialog(db)
        chrome._prepare_secondary_dialog_chrome(account)
        assert not bool(account.windowFlags() & Qt.WindowType.WindowSystemMenuHint)
        assert bool(account.windowFlags() & Qt.WindowType.WindowTitleHint)
        assert bool(account.windowFlags() & Qt.WindowType.WindowCloseButtonHint)

        account.close()
        importer.close()
        settings.close()
        input_tab.close()
    finally:
        app.setStyleSheet(old_style)
        db.close()
