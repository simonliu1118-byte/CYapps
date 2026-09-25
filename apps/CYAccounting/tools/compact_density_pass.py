from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
main_path = ROOT / "app" / "main.py"
theme_path = ROOT / "app" / "theme.py"

main = main_path.read_text(encoding="utf-8")
theme = theme_path.read_text(encoding="utf-8")

replacements = [
    ('        super().__init__(parent)\n        self.db = db\n', '        super().__init__(parent)\n        self.setObjectName("inputPage")\n        self.db = db\n', 1),
    ('        outer.setContentsMargins(14, 12, 14, 12)\n        outer.setSpacing(9)\n', '        outer.setContentsMargins(12, 6, 12, 8)\n        outer.setSpacing(4)\n', 1),
    ('        basic = QGroupBox("基本資訊")\n', '        basic = QGroupBox("基本資訊")\n        basic.setObjectName("inputSection")\n', 1),
    ('        basic_row.setContentsMargins(12, 12, 12, 10)\n        basic_row.setSpacing(8)\n', '        basic_row.setContentsMargins(8, 6, 8, 4)\n        basic_row.setSpacing(7)\n', 1),
    ('        income = QGroupBox("收入")\n', '        income = QGroupBox("收入")\n        income.setObjectName("inputSection")\n', 1),
    ('        income_v.setContentsMargins(12, 10, 12, 10)\n        income_v.setSpacing(6)\n', '        income_v.setContentsMargins(8, 4, 8, 4)\n        income_v.setSpacing(4)\n', 1),
    ('        income_v.addSpacing(4)\n', '        income_v.addSpacing(0)\n', 1),
    ('        expense = QGroupBox("支出")\n', '        expense = QGroupBox("支出")\n        expense.setObjectName("inputSection")\n', 1),
    ('        expense_v.setContentsMargins(12, 10, 12, 10)\n        expense_v.setSpacing(6)\n', '        expense_v.setContentsMargins(8, 4, 8, 4)\n        expense_v.setSpacing(4)\n', 1),
    ('        expense_v.addSpacing(4)\n', '        expense_v.addSpacing(0)\n', 1),
    ('            button.setMinimumHeight(30)\n', '            button.setMinimumHeight(26)\n', 1),
    ('        outer.setContentsMargins(14, 12, 14, 12)\n        top = QHBoxLayout()\n', '        outer.setContentsMargins(12, 6, 12, 8)\n        outer.setSpacing(5)\n        top = QHBoxLayout()\n        top.setSpacing(6)\n', 1),
    ('        top.addSpacing(12)\n', '        top.addSpacing(8)\n', 1),
    ('        header_row = QHBoxLayout()\n', '        header_row = QHBoxLayout()\n        header_row.setContentsMargins(0, 0, 0, 0)\n        header_row.setSpacing(6)\n', 1),
    ('        self.table.verticalHeader().setMinimumSectionSize(26)\n        self.table.verticalHeader().setDefaultSectionSize(28)\n', '        self.table.verticalHeader().setMinimumSectionSize(24)\n        self.table.verticalHeader().setDefaultSectionSize(24)\n', 1),
    ('        self.table.horizontalHeader().setFixedHeight(30)\n', '        self.table.horizontalHeader().setFixedHeight(24)\n', 1),
    ('        bottom.setSpacing(10)\n', '        bottom.setSpacing(8)\n', 1),
    ('        sg.setContentsMargins(12, 10, 12, 10)\n        sg.setHorizontalSpacing(30)\n        sg.setVerticalSpacing(4)\n', '        sg.setContentsMargins(10, 6, 10, 6)\n        sg.setHorizontalSpacing(24)\n        sg.setVerticalSpacing(2)\n', 1),
]

for old, new, expected in replacements:
    count = main.count(old)
    if count != expected:
        raise SystemExit(f"main.py replacement mismatch: expected {expected}, got {count}: {old[:80]!r}")
    main = main.replace(old, new, expected)

theme_replacements = [
    ('QGroupBox#resultCard, QGroupBox#statsCard {{\n    border: 1px solid {NEUTRAL_BORDER};\n    border-radius: 6px;\n    margin-top: 16px;\n    padding: 14px 12px 10px 12px;\n', 'QGroupBox#resultCard, QGroupBox#statsCard {{\n    border: 1px solid {NEUTRAL_BORDER};\n    border-radius: 6px;\n    margin-top: 12px;\n    padding: 10px 10px 8px 10px;\n', 1),
    ('QGroupBox#resultCard::title, QGroupBox#statsCard::title {{\n', 'QGroupBox#inputSection {{\n    margin-top: 12px;\n    padding: 8px 0 2px 0;\n}}\nQWidget#inputPage QLineEdit, QWidget#inputPage QComboBox {{\n    min-height: 30px;\n    font-size: 11pt;\n}}\nQWidget#inputPage QPushButton#primaryButton, QWidget#inputPage QPushButton#accountChoiceButton {{\n    min-height: 30px;\n    font-size: 10.5pt;\n}}\n\nQGroupBox#resultCard::title, QGroupBox#statsCard::title {{\n', 1),
    ('QHeaderView::section {{\n    background: {NEUTRAL_WINDOW};\n    color: {TEXT_PRIMARY};\n    border: 0;\n    border-right: 1px solid {NEUTRAL_GRID};\n    border-bottom: 1px solid {NEUTRAL_DIVIDER};\n    padding: 4px;\n', 'QHeaderView::section {{\n    background: {NEUTRAL_WINDOW};\n    color: {TEXT_PRIMARY};\n    border: 0;\n    border-right: 1px solid {NEUTRAL_GRID};\n    border-bottom: 1px solid {NEUTRAL_DIVIDER};\n    padding: 2px 4px;\n', 1),
]

for old, new, expected in theme_replacements:
    count = theme.count(old)
    if count != expected:
        raise SystemExit(f"theme.py replacement mismatch: expected {expected}, got {count}: {old[:80]!r}")
    theme = theme.replace(old, new, expected)

main_path.write_text(main, encoding="utf-8")
theme_path.write_text(theme, encoding="utf-8")
print("density pass applied")
