from __future__ import annotations

import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
APP = ROOT / "app"


def read(name: str) -> str:
    return (APP / name).read_text(encoding="utf-8")


def write(name: str, text: str) -> None:
    (APP / name).write_text(text, encoding="utf-8", newline="\n")


def replace_once(text: str, old: str, new: str, label: str) -> str:
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"{label}: expected exactly one match, found {count}")
    return text.replace(old, new, 1)


def replace_all(text: str, old: str, new: str, label: str, minimum: int = 1) -> str:
    count = text.count(old)
    if count < minimum:
        raise RuntimeError(f"{label}: expected at least {minimum} matches, found {count}")
    return text.replace(old, new)


def update_main() -> None:
    text = read("main.py")
    pattern = re.compile(r'APP_STYLE = """.*?\n\nclass ChineseStandardButtonFilter', re.S)
    text, count = pattern.subn(
        'from theme import APP_STYLE\n\n\nclass ChineseStandardButtonFilter',
        text,
        count=1,
    )
    if count != 1:
        raise RuntimeError(f"main APP_STYLE block: expected 1 match, found {count}")

    text = replace_once(
        text,
        '        self.date_error.setStyleSheet("color:#c62828;font-weight:bold;")',
        '        self.date_error.setStyleSheet("color:#B43737;font-weight:600;")',
        "date error state",
    )
    text = replace_once(
        text,
        '        self.income_amount.setPlaceholderText("最多7位")',
        '        self.income_amount.setPlaceholderText("最多7位")\n        self.income_amount.setAlignment(Qt.AlignmentFlag.AlignRight | Qt.AlignmentFlag.AlignVCenter)',
        "income amount alignment",
    )
    text = replace_once(
        text,
        '        self.expense_amount.setPlaceholderText("最多7位")',
        '        self.expense_amount.setPlaceholderText("最多7位")\n        self.expense_amount.setAlignment(Qt.AlignmentFlag.AlignRight | Qt.AlignmentFlag.AlignVCenter)',
        "expense amount alignment",
    )
    text = replace_once(
        text,
        '        confirm = QGroupBox("輸入確認")',
        '        confirm = QGroupBox("輸入確認")\n        confirm.setObjectName("resultCard")',
        "confirmation result card",
    )
    text = replace_all(
        text,
        '            button.setFixedHeight(26)',
        '            button.setMinimumHeight(30)',
        "quick category button density",
        minimum=2,
    )

    text = replace_all(text, '#c62828', '#B43737', "danger color")
    text = replace_all(text, '#198754', '#21825C', "success color")
    text = replace_all(text, '#98a2b3', '#98A2B3', "disabled text color")
    text = replace_all(text, '#e7efe9', '#EDF5F2', "income soft color")
    text = replace_all(text, '#4f745c', '#2E6F5E', "income text color")
    text = replace_all(text, '#f4e8e8', '#F6EEEE', "expense soft color")
    text = replace_all(text, '#9b5b5b', '#8A5B5B', "expense tag text color")
    text = replace_all(text, '#1f5f99', '#2E6F5E', "income stats color")
    text = replace_all(text, '#b42318', '#8A5B5B', "expense domain color")
    text = replace_all(text, '#334155', '#1F2937', "primary text color")

    text = replace_once(
        text,
        '        self.table.verticalHeader().setMinimumSectionSize(22)',
        '        self.table.verticalHeader().setMinimumSectionSize(26)',
        "ledger minimum row height",
    )
    text = replace_once(
        text,
        '        self.table.verticalHeader().setDefaultSectionSize(23)',
        '        self.table.verticalHeader().setDefaultSectionSize(28)',
        "ledger default row height",
    )
    text = replace_once(
        text,
        '        self.table.horizontalHeader().setFixedHeight(28)',
        '        self.table.horizontalHeader().setFixedHeight(30)',
        "ledger header height",
    )
    text = replace_once(
        text,
        '        self.table.setStyleSheet("QTableView{font-size:10pt;} QHeaderView::section{font-size:10pt;padding:3px 3px;}")\n',
        '',
        "remove ledger local stylesheet",
    )
    text = replace_once(
        text,
        '        stats_box = QGroupBox("月份統計")',
        '        stats_box = QGroupBox("月份統計")\n        stats_box.setObjectName("statsCard")',
        "ledger stats card",
    )
    text = replace_once(
        text,
        '        central_layout.setContentsMargins(12, 8, 12, 10)',
        '        central_layout.setContentsMargins(16, 10, 16, 12)',
        "main window margins",
    )
    text = replace_once(
        text,
        '        top_nav.setContentsMargins(2, 0, 2, 0)',
        '        top_nav.setContentsMargins(0, 0, 0, 0)',
        "top navigation margins",
    )
    text = replace_once(
        text,
        '        top_nav.setSpacing(7)',
        '        top_nav.setSpacing(8)',
        "top navigation spacing",
    )
    write("main.py", text)


def update_dialogs() -> None:
    text = read("dialogs.py")
    text = replace_once(
        text,
        'from widgets import CategoryComboBox, DatePickerDialog, MonthSpinBox, SmartDateLineEdit, WeightedLineEdit\n',
        'from widgets import CategoryComboBox, DatePickerDialog, MonthSpinBox, SmartDateLineEdit, WeightedLineEdit\n',
        "dialogs import anchor",
    )
    text = replace_all(text, '#c62828', '#B43737', "dialog danger color")
    text = replace_all(text, '#5d6875', '#667085', "dialog secondary text", minimum=2)

    settings_style = re.compile(
        r'        self\.setStyleSheet\(\n'
        r'            "QDialog#settingsDialog QPushButton, QDialog#settingsDialog QLineEdit, "\n'
        r'            "QDialog#settingsDialog QComboBox, QDialog#settingsDialog QSpinBox \{"\n'
        r'            "min-height:21px; max-height:24px; padding-top:0px; padding-bottom:0px;\}"\n'
        r'            "QDialog#settingsDialog QGroupBox \{padding-top:9px; padding-bottom:7px;\}"\n'
        r'        \)\n'
    )
    text, count = settings_style.subn('', text, count=1)
    if count != 1:
        raise RuntimeError(f"settings compact stylesheet: expected 1 match, found {count}")
    text = replace_once(
        text,
        '        self.resize(700, 650)',
        '        self.resize(760, 700)',
        "settings dialog initial size",
    )
    text = replace_once(
        text,
        '        outer = QVBoxLayout(self)\n\n        db_box = QGroupBox("資料庫儲存位置")',
        '        outer = QVBoxLayout(self)\n        outer.setContentsMargins(16, 12, 16, 12)\n        outer.setSpacing(10)\n\n        db_box = QGroupBox("資料庫儲存位置")',
        "settings layout rhythm",
    )
    text = replace_once(
        text,
        '        self.amount = QLineEdit(str(tx["amount"])); self.amount.setValidator(QRegularExpressionValidator(QRegularExpression(r"[0-9]{0,7}"), self.amount))',
        '        self.amount = QLineEdit(str(tx["amount"])); self.amount.setValidator(QRegularExpressionValidator(QRegularExpression(r"[0-9]{0,7}"), self.amount)); self.amount.setAlignment(Qt.AlignmentFlag.AlignRight | Qt.AlignmentFlag.AlignVCenter)',
        "edit transaction amount alignment",
    )
    write("dialogs.py", text)


def update_import_dialog() -> None:
    text = read("import_dialog.py")
    text = replace_once(
        text,
        'from util import APP_NAME, MAX_AMOUNT, format_date, is_month_locked, month_key_from_date, normalize_date_input, parse_date, weighted_units\n',
        'from util import APP_NAME, MAX_AMOUNT, format_date, is_month_locked, month_key_from_date, normalize_date_input, parse_date, weighted_units\nfrom theme import IMPORT_DIALOG_STYLE\n',
        "import dialog theme import",
    )
    old = (
        '        self.setStyleSheet(\n'
        '            "QDialog#importDialog QPushButton, QDialog#importDialog QLineEdit, QDialog#importDialog QComboBox {"\n'
        '            "min-height:24px; max-height:27px; padding-top:0px; padding-bottom:0px;}"\n'
        '        )'
    )
    text = replace_once(text, old, '        self.setStyleSheet(IMPORT_DIALOG_STYLE)', "import dialog natural control heights")
    text = replace_once(
        text,
        '        outer = QVBoxLayout(self)\n        outer.setSpacing(8)',
        '        outer = QVBoxLayout(self)\n        outer.setContentsMargins(16, 12, 16, 12)\n        outer.setSpacing(12)',
        "import dialog layout rhythm",
    )
    write("import_dialog.py", text)


def update_widgets() -> None:
    text = read("widgets.py")
    text = replace_once(
        text,
        'from util import format_amount, normalize_date_input, shift_month, trim_weighted, weighted_units\n',
        'from util import format_amount, normalize_date_input, shift_month, trim_weighted, weighted_units\nfrom theme import CALENDAR_STYLE, CATEGORY_POPUP_STYLE\n',
        "widget theme imports",
    )
    old_view = (
        '        self.view().setStyleSheet(\n'
        '            "QAbstractItemView { border: 1px solid #aeb9c5; background: white; "\n'
        '            "outline: 0; selection-background-color: #dce9f5; }"\n'
        '        )'
    )
    text = replace_once(text, old_view, '        self.view().setStyleSheet(CATEGORY_POPUP_STYLE)', "category popup style")
    text = replace_all(text, 'QColor("#4c5968")', 'QColor("#667085")', "category heading text")
    text = replace_all(text, 'QColor("#eef2f6")', 'QColor("#F1F3F5")', "category heading surface")
    calendar_old = (
        '        self.calendar.setStyleSheet(\n'
        '            "QCalendarWidget QAbstractItemView::item:selected {"\n'
        '            "background:#39739d; color:white; font-weight:bold; border:1px solid #245a82;"\n'
        '            "}"\n'
        '        )'
    )
    text = replace_once(text, calendar_old, '        self.calendar.setStyleSheet(CALENDAR_STYLE)', "calendar selection style")
    text = replace_all(text, 'QColor("#edf0f3")', 'QColor("#F1F3F5")', "calendar outside-month surface")
    text = replace_all(text, 'QColor("#8b949e")', 'QColor("#98A2B3")', "calendar outside-month text")
    write("widgets.py", text)


def update_models() -> None:
    text = read("models.py")
    text = replace_once(text, 'from PySide6.QtGui import QBrush, QColor\n', '', "model unused paint imports")
    old = (
        '        if role == Qt.ItemDataRole.BackgroundRole:\n'
        '            return QBrush(QColor("#ffffff" if index.row() % 2 == 0 else "#f2f5f8"))\n'
    )
    text = replace_once(text, old, '', "single zebra styling source")
    write("models.py", text)


def main() -> None:
    update_main()
    update_dialogs()
    update_import_dialog()
    update_widgets()
    update_models()
    print("CYAccounting Phase 1 visual transformation applied")


if __name__ == "__main__":
    main()
