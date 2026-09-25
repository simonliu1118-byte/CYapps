from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
main_path = ROOT / "app" / "main.py"
theme_path = ROOT / "app" / "theme.py"
version_path = ROOT / "V1.2.0.txt"

main = main_path.read_text(encoding="utf-8")
theme = theme_path.read_text(encoding="utf-8")
version = version_path.read_text(encoding="utf-8")


def replace_once(text: str, old: str, new: str, label: str) -> str:
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"{label}: expected 1 match, got {count}")
    return text.replace(old, new, 1)

main = replace_once(
    main,
    '        outer.setContentsMargins(12, 6, 12, 8)\n        outer.setSpacing(4)\n\n        basic = QGroupBox("基本資訊")\n',
    '        outer.setContentsMargins(12, 6, 12, 8)\n        outer.setSpacing(8)\n\n        basic = QGroupBox("基本資訊")\n',
    "input outer spacing",
)
main = replace_once(
    main,
    '        basic_row.setContentsMargins(8, 6, 8, 4)\n        basic_row.setSpacing(7)\n',
    '        basic_row.setContentsMargins(10, 8, 10, 8)\n        basic_row.setSpacing(7)\n',
    "basic spacing",
)

old_date = '''        # Compact date stepper: upper arrow = +1 day, lower arrow = -1 day.\n        self.date_step_host = QWidget()\n        self.date_step_layout = QVBoxLayout(self.date_step_host)\n        self.date_step_layout.setContentsMargins(0, 0, 0, 0)\n        self.date_step_layout.setSpacing(2)\n        self.date_up_btn = QToolButton(self.date_step_host)\n        self.date_up_btn.setObjectName("dateStepButton")\n        self.date_up_btn.setText("▲")\n        self.date_up_btn.setToolTip("日期 +1 天")\n        self.date_up_btn.setFocusPolicy(Qt.FocusPolicy.NoFocus)\n        self.date_up_btn.clicked.connect(lambda: self.shift_date(1))\n        self.date_down_btn = QToolButton(self.date_step_host)\n        self.date_down_btn.setObjectName("dateStepButton")\n        self.date_down_btn.setText("▼")\n        self.date_down_btn.setToolTip("日期 -1 天")\n        self.date_down_btn.setFocusPolicy(Qt.FocusPolicy.NoFocus)\n        self.date_down_btn.clicked.connect(lambda: self.shift_date(-1))\n        self.date_step_layout.addWidget(self.date_up_btn)\n        self.date_step_layout.addWidget(self.date_down_btn)\n\n        self.calendar_btn = no_tab_button("")\n        self.calendar_btn.setIcon(QIcon(str(app_root() / "app" / "resources" / "calendar.png")))\n        self.calendar_btn.setToolTip("選擇日期")\n        self.calendar_btn.setFixedWidth(42)\n        self.calendar_btn.clicked.connect(self.pick_date)\n'''
new_date = '''        # Date controls share one visual height. Use Qt arrow primitives instead\n        # of text glyphs so the day stepper stays crisp at Windows DPI scaling.\n        date_control_height = max(34, self.date_edit.sizeHint().height())\n        self.date_step_host = QWidget()\n        self.date_step_host.setFixedSize(28, date_control_height)\n        self.date_step_layout = QVBoxLayout(self.date_step_host)\n        self.date_step_layout.setContentsMargins(0, 0, 0, 0)\n        self.date_step_layout.setSpacing(1)\n        upper_height = (date_control_height - 1) // 2\n        lower_height = date_control_height - 1 - upper_height\n        self.date_up_btn = QToolButton(self.date_step_host)\n        self.date_up_btn.setObjectName("dateStepUp")\n        self.date_up_btn.setArrowType(Qt.ArrowType.UpArrow)\n        self.date_up_btn.setFixedSize(28, upper_height)\n        self.date_up_btn.setToolTip("日期 +1 天")\n        self.date_up_btn.setFocusPolicy(Qt.FocusPolicy.NoFocus)\n        self.date_up_btn.clicked.connect(lambda: self.shift_date(1))\n        self.date_down_btn = QToolButton(self.date_step_host)\n        self.date_down_btn.setObjectName("dateStepDown")\n        self.date_down_btn.setArrowType(Qt.ArrowType.DownArrow)\n        self.date_down_btn.setFixedSize(28, lower_height)\n        self.date_down_btn.setToolTip("日期 -1 天")\n        self.date_down_btn.setFocusPolicy(Qt.FocusPolicy.NoFocus)\n        self.date_down_btn.clicked.connect(lambda: self.shift_date(-1))\n        self.date_step_layout.addWidget(self.date_up_btn)\n        self.date_step_layout.addWidget(self.date_down_btn)\n\n        self.calendar_btn = no_tab_button("")\n        self.calendar_btn.setObjectName("calendarButton")\n        self.calendar_btn.setIcon(QIcon(str(app_root() / "app" / "resources" / "calendar.png")))\n        self.calendar_btn.setToolTip("選擇日期")\n        self.calendar_btn.setFixedSize(42, date_control_height)\n        self.calendar_btn.clicked.connect(self.pick_date)\n'''
main = replace_once(main, old_date, new_date, "date control block")

main = replace_once(main, '        income.setObjectName("inputSection")\n', '        income.setObjectName("incomeSection")\n', "income section id")
main = replace_once(main, '        expense.setObjectName("inputSection")\n', '        expense.setObjectName("expenseSection")\n', "expense section id")
main = replace_once(
    main,
    '        income_v.setContentsMargins(8, 4, 8, 4)\n        income_v.setSpacing(4)\n',
    '        income_v.setContentsMargins(10, 8, 10, 8)\n        income_v.setSpacing(5)\n',
    "income spacing",
)
main = replace_once(
    main,
    '        expense_v.setContentsMargins(8, 4, 8, 4)\n        expense_v.setSpacing(4)\n',
    '        expense_v.setContentsMargins(10, 8, 10, 8)\n        expense_v.setSpacing(5)\n',
    "expense spacing",
)

old_confirm = '''        confirm = QGroupBox("輸入確認")\n        confirm.setObjectName("resultCard")\n        confirm.setSizePolicy(QSizePolicy.Policy.Expanding, QSizePolicy.Policy.Expanding)\n        confirm.setMinimumHeight(244)\n        cv = QVBoxLayout(confirm)\n        cv.setContentsMargins(12, 10, 12, 10)\n        cv.setSpacing(3)\n        self.confirm_labels = []\n        for _ in range(10):\n            lab = QLabel("")\n            lab.setMinimumHeight(22)\n            lab.setTextFormat(Qt.TextFormat.RichText)\n            self.confirm_labels.append(lab)\n            cv.addWidget(lab)\n        cv.addStretch(1)\n        outer.addWidget(confirm, 1)\n'''
new_confirm = '''        confirm = QGroupBox("輸入確認")\n        confirm.setObjectName("confirmationCard")\n        confirm.setSizePolicy(QSizePolicy.Policy.Expanding, QSizePolicy.Policy.Fixed)\n        confirm.setFixedHeight(240)\n        cv = QVBoxLayout(confirm)\n        cv.setContentsMargins(10, 7, 10, 7)\n        cv.setSpacing(2)\n        self.confirm_labels = []\n        for _ in range(10):\n            lab = QLabel("")\n            lab.setMinimumHeight(18)\n            lab.setTextFormat(Qt.TextFormat.RichText)\n            self.confirm_labels.append(lab)\n            cv.addWidget(lab)\n        outer.addWidget(confirm)\n        outer.addStretch(1)\n'''
main = replace_once(main, old_confirm, new_confirm, "confirmation panel")

main = replace_once(
    main,
    '        self.table.verticalHeader().setMinimumSectionSize(24)\n        self.table.verticalHeader().setDefaultSectionSize(24)\n',
    '        self.table.verticalHeader().setMinimumSectionSize(22)\n        self.table.verticalHeader().setDefaultSectionSize(23)\n',
    "ledger data row density",
)

# Theme source needs the accepted legacy combo arrow asset for the input page only.
if 'from util import app_root\n' not in theme:
    theme = theme.replace('from __future__ import annotations\n', 'from __future__ import annotations\n\nfrom util import app_root\n', 1)
if 'COMBO_ARROW_PATH' not in theme:
    theme = theme.replace('EXPENSE_SOFT = "#F6EEEE"\n', 'EXPENSE_SOFT = "#F6EEEE"\n\nCOMBO_ARROW_PATH = (app_root() / "app" / "resources" / "combo_arrow.png").as_posix()\n', 1)

old_sections = '''QGroupBox#inputSection {{\n    margin-top: 12px;\n    padding: 8px 0 2px 0;\n}}\nQWidget#inputPage QLineEdit, QWidget#inputPage QComboBox {{\n    min-height: 30px;\n    font-size: 11pt;\n}}\n'''
new_sections = '''QGroupBox#inputSection, QGroupBox#incomeSection, QGroupBox#expenseSection {{\n    border: 1px solid {NEUTRAL_BORDER};\n    border-radius: 6px;\n    margin-top: 14px;\n    padding: 12px 10px 8px 10px;\n}}\nQGroupBox#inputSection {{\n    background: {NEUTRAL_WHITE};\n}}\nQGroupBox#incomeSection {{\n    background: #E7F3E9;\n    border-color: #C7DCCB;\n}}\nQGroupBox#expenseSection {{\n    background: #F8E9E7;\n    border-color: #E7CECA;\n}}\nQGroupBox#inputSection::title {{\n    left: 10px;\n    padding: 0 5px;\n    background: {NEUTRAL_WHITE};\n}}\nQGroupBox#incomeSection::title {{\n    left: 10px;\n    padding: 0 5px;\n    background: #E7F3E9;\n}}\nQGroupBox#expenseSection::title {{\n    left: 10px;\n    padding: 0 5px;\n    background: #F8E9E7;\n}}\nQWidget#inputPage QLineEdit, QWidget#inputPage QComboBox {{\n    min-height: 30px;\n    font-size: 11pt;\n}}\nQWidget#inputPage QComboBox {{\n    padding-right: 34px;\n}}\nQWidget#inputPage QComboBox::drop-down {{\n    subcontrol-origin: padding;\n    subcontrol-position: top right;\n    width: 30px;\n    border-left: 1px solid #AEB9C5;\n    background: #F1F4F7;\n    border-top-right-radius: 4px;\n    border-bottom-right-radius: 4px;\n}}\nQWidget#inputPage QComboBox::drop-down:hover {{\n    background: #E0E7EE;\n}}\nQWidget#inputPage QComboBox::down-arrow {{\n    image: url("{COMBO_ARROW_PATH}");\n    width: 14px;\n    height: 9px;\n}}\n'''
theme = replace_once(theme, old_sections, new_sections, "input section theme")

old_result = '''QGroupBox#resultCard, QGroupBox#statsCard {{\n    border: 1px solid {NEUTRAL_BORDER};\n    border-radius: 6px;\n    margin-top: 12px;\n    padding: 10px 10px 8px 10px;\n    background: {NEUTRAL_WHITE};\n}}\n'''
new_result = '''QGroupBox#statsCard {{\n    border: 1px solid {NEUTRAL_BORDER};\n    border-radius: 6px;\n    margin-top: 12px;\n    padding: 10px 10px 8px 10px;\n    background: {NEUTRAL_WHITE};\n}}\nQGroupBox#confirmationCard {{\n    border: 1px solid {NEUTRAL_BORDER};\n    border-radius: 6px;\n    margin-top: 12px;\n    padding: 10px 10px 8px 10px;\n    background: {NEUTRAL_WINDOW};\n}}\n'''
theme = replace_once(theme, old_result, new_result, "result cards")

theme = replace_once(
    theme,
    'QGroupBox#resultCard::title, QGroupBox#statsCard::title {{\n    left: 12px;\n    padding: 0 5px;\n    background: {NEUTRAL_WHITE};\n}}\n',
    'QGroupBox#statsCard::title {{\n    left: 12px;\n    padding: 0 5px;\n    background: {NEUTRAL_WHITE};\n}}\nQGroupBox#confirmationCard::title {{\n    left: 10px;\n    padding: 0 5px;\n    background: {NEUTRAL_WINDOW};\n}}\n',
    "card titles",
)

old_step = '''QToolButton#dateStepButton {{\n    min-width: 24px;\n    max-width: 24px;\n    min-height: 15px;\n    max-height: 15px;\n    padding: 0;\n    border: 1px solid {NEUTRAL_BORDER};\n    border-radius: 3px;\n    background: {NEUTRAL_WHITE};\n    font-size: 8pt;\n    font-weight: 600;\n}}\nQToolButton#dateStepButton:hover {{ background: {NEUTRAL_WINDOW}; }}\nQToolButton#dateStepButton:pressed {{ background: {NEUTRAL_READ_ONLY}; }}\n'''
new_step = '''QToolButton#dateStepUp, QToolButton#dateStepDown {{\n    padding: 0;\n    border: 1px solid {NEUTRAL_BORDER};\n    background: {NEUTRAL_WHITE};\n}}\nQToolButton#dateStepUp {{\n    border-top-left-radius: 3px;\n    border-top-right-radius: 3px;\n    border-bottom: 0;\n}}\nQToolButton#dateStepDown {{\n    border-bottom-left-radius: 3px;\n    border-bottom-right-radius: 3px;\n}}\nQToolButton#dateStepUp:hover, QToolButton#dateStepDown:hover {{ background: {NEUTRAL_WINDOW}; }}\nQToolButton#dateStepUp:pressed, QToolButton#dateStepDown:pressed {{ background: {NEUTRAL_READ_ONLY}; }}\nQPushButton#calendarButton {{\n    padding: 0;\n    border: 1px solid {NEUTRAL_BORDER};\n    background: {NEUTRAL_WHITE};\n}}\nQPushButton#calendarButton:hover {{ background: {NEUTRAL_WINDOW}; }}\n'''
theme = replace_once(theme, old_step, new_step, "date control theme")

note = '- Input page refinement: framed Basic/Income/Expense sections restored; Income uses CYAccountingWeb #E7F3E9 and Expense uses #F8E9E7; legacy polished category-combo arrow treatment restored only on the input page; date controls refined and matched in height; confirmation panel is fixed to the 10-entry use case without a white card fill; ledger data rows restored to the original 23px density while the header stays 24px.\n'
if note not in version:
    version += note

main_path.write_text(main, encoding="utf-8")
theme_path.write_text(theme, encoding="utf-8")
version_path.write_text(version, encoding="utf-8")
print("Phase 1 input refinement applied")
