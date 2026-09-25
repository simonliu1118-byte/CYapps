from __future__ import annotations

from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def replace_once(path: Path, old: str, new: str) -> None:
    text = path.read_text(encoding="utf-8")
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"{path}: expected exactly one guarded match, found {count}")
    path.write_text(text.replace(old, new, 1), encoding="utf-8")


# 1) Secondary-dialog chrome: keep the real Windows system menu + close button.
# Only the icon itself is suppressed. Build 4 removed WindowSystemMenuHint, which
# made the X button unavailable on Windows.
main_path = ROOT / "app" / "main.py"
old_prepare = '''    @staticmethod
    def _prepare_secondary_dialog_chrome(dialog: QDialog) -> None:
        # Use a true dialog title-bar configuration rather than a transparent
        # icon.  A transparent HICON still reserves the icon slot and leaves
        # the title visibly indented on Windows.
        if dialog.property("cySecondaryChromePrepared"):
            return
        dialog.setProperty("cySecondaryChromePrepared", True)
        dialog.setWindowIcon(QIcon())
        try:
            flags = dialog.windowFlags()
            flags |= (
                Qt.WindowType.CustomizeWindowHint
                | Qt.WindowType.WindowTitleHint
                | Qt.WindowType.WindowCloseButtonHint
            )
            flags &= ~Qt.WindowType.WindowSystemMenuHint
            dialog.setWindowFlags(flags)
        except Exception:
            pass
'''
new_prepare = '''    @staticmethod
    def _prepare_secondary_dialog_chrome(dialog: QDialog) -> None:
        # Keep the native Windows system menu: the Close button depends on it.
        # The icon is removed separately at the native non-client level after
        # the HWND exists.  Do not use CustomizeWindowHint here; doing so can
        # leave the X button disabled even when WindowCloseButtonHint is set.
        if dialog.property("cySecondaryChromePrepared"):
            return
        dialog.setProperty("cySecondaryChromePrepared", True)
        dialog.setWindowIcon(QIcon())
        try:
            flags = dialog.windowFlags()
            flags |= (
                Qt.WindowType.WindowTitleHint
                | Qt.WindowType.WindowSystemMenuHint
                | Qt.WindowType.WindowCloseButtonHint
            )
            flags &= ~Qt.WindowType.CustomizeWindowHint
            dialog.setWindowFlags(flags)
        except Exception:
            pass
'''
replace_once(main_path, old_prepare, new_prepare)

old_native = '''                ex_style = user32.GetWindowLongW(hwnd, GWL_EXSTYLE)
                user32.SetWindowLongW(hwnd, GWL_EXSTYLE, ex_style | WS_EX_DLGMODALFRAME)
                user32.SendMessageW(hwnd, WM_SETICON, ICON_SMALL, 0)
                user32.SendMessageW(hwnd, WM_SETICON, ICON_BIG, 0)
'''
new_native = '''                # WS_EX_DLGMODALFRAME is the native no-caption-icon style.
                # It does not remove WS_SYSMENU, so the Windows Close button
                # remains active. WM_SETICON(NULL) then clears any inherited
                # QApplication/window icon after the native handle exists.
                get_ex_style = getattr(user32, "GetWindowLongPtrW", user32.GetWindowLongW)
                set_ex_style = getattr(user32, "SetWindowLongPtrW", user32.SetWindowLongW)
                ex_style = get_ex_style(hwnd, GWL_EXSTYLE)
                set_ex_style(hwnd, GWL_EXSTYLE, ex_style | WS_EX_DLGMODALFRAME)
                user32.SendMessageW(hwnd, WM_SETICON, ICON_SMALL, 0)
                user32.SendMessageW(hwnd, WM_SETICON, ICON_BIG, 0)
'''
replace_once(main_path, old_native, new_native)

# 2) Category manager: large, two-column tabs that fill the dialog width.
dialogs_path = ROOT / "app" / "dialogs.py"
old_category = '''        self.db = db
        self.setWindowTitle("收入支出科目管理")
        self.setModal(True)
'''
new_category = '''        self.db = db
        self.setWindowTitle("收入支出科目管理")
        self.setObjectName("categoryManagerDialog")
        self.setModal(True)
'''
replace_once(dialogs_path, old_category, new_category)

old_tabs = '''        tabs = QTabWidget()
        tabs.addTab(CategoryPage(db, "income"), "收入科目")
        tabs.addTab(CategoryPage(db, "expense"), "支出科目")
        outer.addWidget(tabs, 1)
'''
new_tabs = '''        tabs = QTabWidget()
        tabs.setObjectName("categoryManagerTabs")
        tabs.tabBar().setExpanding(True)
        tabs.tabBar().setUsesScrollButtons(False)
        tabs.addTab(CategoryPage(db, "income"), "收入科目")
        tabs.addTab(CategoryPage(db, "expense"), "支出科目")
        outer.addWidget(tabs, 1)
'''
replace_once(dialogs_path, old_tabs, new_tabs)

# 3) Visual style: month statistics uses the same no-title-fill, bold legend;
# category manager gets intentionally large tabs.
theme_path = ROOT / "app" / "theme.py"
old_stats_box = '''QGroupBox#statsCard {{
    border: 1px solid {NEUTRAL_BORDER};
    border-radius: 6px;
    margin-top: 12px;
    padding: 10px 10px 8px 10px;
    background: {NEUTRAL_WHITE};
}}
'''
new_stats_box = '''QGroupBox#statsCard {{
    border: 1px solid {NEUTRAL_BORDER};
    border-radius: 6px;
    margin-top: 12px;
    padding: 10px 10px 8px 10px;
    background: {NEUTRAL_WHITE};
    font-weight: 700;
}}
QGroupBox#statsCard QWidget {{
    font-weight: normal;
}}
'''
replace_once(theme_path, old_stats_box, new_stats_box)

old_stats_title = '''QGroupBox#statsCard::title {{
    left: 12px;
    padding: 0 5px;
    background: {NEUTRAL_WHITE};
}}
'''
new_stats_title = '''QGroupBox#statsCard::title {{
    subcontrol-origin: margin;
    subcontrol-position: top left;
    left: 10px;
    padding: 0 2px;
    color: {TEXT_PRIMARY};
    font-size: 11.5pt;
    font-weight: 700;
}}

QTabWidget#categoryManagerTabs::pane {{
    border: 1px solid {NEUTRAL_BORDER};
    border-radius: 4px;
    background: {NEUTRAL_WHITE};
    top: -1px;
}}
QTabWidget#categoryManagerTabs QTabBar::tab {{
    min-height: 36px;
    min-width: 140px;
    padding: 7px 18px;
    margin: 0;
    border: 1px solid {NEUTRAL_BORDER};
    border-bottom: 2px solid {NEUTRAL_BORDER};
    background: {NEUTRAL_READ_ONLY};
    color: {TEXT_SECONDARY};
    font-size: 11.5pt;
    font-weight: 600;
}}
QTabWidget#categoryManagerTabs QTabBar::tab:first {{
    border-top-left-radius: 4px;
}}
QTabWidget#categoryManagerTabs QTabBar::tab:last {{
    border-top-right-radius: 4px;
}}
QTabWidget#categoryManagerTabs QTabBar::tab:selected {{
    background: {NEUTRAL_WHITE};
    color: {ACCENT_PRESSED};
    border-bottom: 2px solid {ACCENT};
    font-weight: 700;
}}
QTabWidget#categoryManagerTabs QTabBar::tab:hover:!selected {{
    background: {NEUTRAL_WINDOW};
    color: {TEXT_PRIMARY};
}}
'''
replace_once(theme_path, old_stats_title, new_stats_title)

# 4) Regression tests: secondary dialogs must retain the system menu/Close hint;
# category tabs must be the dedicated expanding large-tab widget.
test_path = ROOT / "tests" / "test_app.py"
replace_once(
    test_path,
    'from PySide6.QtWidgets import QApplication, QPushButton\n',
    'from PySide6.QtWidgets import QApplication, QPushButton, QTabWidget\n',
)
replace_once(
    test_path,
    'from PySide6.QtGui import QColor, QPalette\n',
    'from PySide6.QtGui import QColor, QPalette\nfrom PySide6.QtCore import Qt\n',
)
old_test_tail = '''    category_dlg = CategoryManagerDialog(db)
    category_buttons = [b.text() for b in category_dlg.findChildren(QPushButton)]
    assert '關閉' not in category_buttons
    assert {'↑', '↓', '新增科目', '修改', '刪除', '新增大分類', '移至其他大分類'} <= set(category_buttons)
    combo = CategoryComboBox()
'''
new_test_tail = '''    category_dlg = CategoryManagerDialog(db)
    category_buttons = [b.text() for b in category_dlg.findChildren(QPushButton)]
    assert '關閉' not in category_buttons
    assert {'↑', '↓', '新增科目', '修改', '刪除', '新增大分類', '移至其他大分類'} <= set(category_buttons)
    category_tabs = category_dlg.findChild(QTabWidget, 'categoryManagerTabs')
    assert category_tabs is not None
    assert category_tabs.count() == 2
    assert category_tabs.tabBar().expanding() is True

    # A secondary dialog must keep the native system menu because Windows uses
    # it to back the caption Close button. Build 4 accidentally removed it.
    main_module.ChineseStandardButtonFilter._prepare_secondary_dialog_chrome(dlg)
    chrome_flags = dlg.windowFlags()
    assert bool(chrome_flags & Qt.WindowType.WindowSystemMenuHint)
    assert bool(chrome_flags & Qt.WindowType.WindowCloseButtonHint)
    assert not bool(chrome_flags & Qt.WindowType.CustomizeWindowHint)
    combo = CategoryComboBox()
'''
replace_once(test_path, old_test_tail, new_test_tail)

# 5) Build metadata.
(ROOT / "BUILD").write_text("5\n", encoding="utf-8")
notes_path = ROOT / "V1.2.0.txt"
notes = notes_path.read_text(encoding="utf-8")
if "Build: 4" not in notes:
    raise RuntimeError("V1.2.0.txt Build marker is not 4")
notes = notes.replace("Build: 4", "Build: 5", 1)
notes = notes.replace("Date: 2026/09/25", "Date: 2026/09/26", 1)
anchor = "Current source on Phase 1 visual-alignment branch; test artifact, not a formal Release:\n"
if anchor not in notes:
    raise RuntimeError("V1.2.0.txt anchor missing")
insert = (
    "- Build 5 restores the native secondary-dialog system menu and Close button while keeping the Windows no-icon treatment; no secondary dialog may disable the title-bar X as a side effect of icon suppression.\n"
    "- Build 5 changes Month Statistics to the same no-explicit-title-fill, bold GroupBox legend treatment used by the other section titles.\n"
    "- Build 5 gives Income Category / Expense Category dedicated large expanding tabs that divide the manager width evenly, with larger type and a clear selected state.\n"
)
notes = notes.replace(anchor, anchor + insert, 1)
notes_path.write_text(notes, encoding="utf-8")

print("Build 5 UI refinement applied successfully")
