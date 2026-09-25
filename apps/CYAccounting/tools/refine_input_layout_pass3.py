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

# Secondary dialogs do not repeat the ACC application icon. QMessageBox keeps
# native semantic/shell behavior; native QFileDialog is left untouched.
old_filter = '''    def eventFilter(self, watched, event):  # noqa: N802\n        if event.type() in (QEvent.Type.Polish, QEvent.Type.Show):\n            if isinstance(watched, QDialogButtonBox):\n                self._localize_dialog_box(watched)\n            elif isinstance(watched, QMessageBox):\n                QTimer.singleShot(0, lambda box=watched: self._localize_message_box(box))\n        return False\n'''
new_filter = '''    @staticmethod\n    def _clear_secondary_dialog_icon(dialog: QDialog) -> None:\n        # CY Desktop visual rule: only the main application window carries the\n        # canonical family icon. Keep native dialog chrome/behavior.\n        dialog.setWindowIcon(QIcon())\n        if sys.platform == "win32":\n            try:\n                import ctypes\n                hwnd = int(dialog.winId())\n                user32 = ctypes.windll.user32\n                wm_seticon = 0x0080\n                user32.SendMessageW(hwnd, wm_seticon, 0, 0)  # ICON_SMALL\n                user32.SendMessageW(hwnd, wm_seticon, 1, 0)  # ICON_BIG\n            except Exception:\n                pass\n\n    def eventFilter(self, watched, event):  # noqa: N802\n        if event.type() in (QEvent.Type.Polish, QEvent.Type.Show):\n            if isinstance(watched, QDialogButtonBox):\n                self._localize_dialog_box(watched)\n            elif isinstance(watched, QMessageBox):\n                QTimer.singleShot(0, lambda box=watched: self._localize_message_box(box))\n            elif isinstance(watched, QDialog) and not isinstance(watched, QFileDialog):\n                QTimer.singleShot(0, lambda dlg=watched: self._clear_secondary_dialog_icon(dlg))\n        return False\n'''
main = replace_once(main, old_filter, new_filter, "secondary dialog icon filter")

# Basic information: framed section with an internal title and neutral-window fill.
old_basic = '''        basic = QGroupBox("基本資訊")\n        basic.setObjectName("inputSection")\n        basic.setSizePolicy(QSizePolicy.Policy.Expanding, QSizePolicy.Policy.Fixed)\n        basic_row = QHBoxLayout(basic)\n        basic_row.setContentsMargins(10, 8, 10, 8)\n        basic_row.setSpacing(7)\n'''
new_basic = '''        basic = QGroupBox()\n        basic.setObjectName("inputSection")\n        basic.setSizePolicy(QSizePolicy.Policy.Expanding, QSizePolicy.Policy.Fixed)\n        basic_v = QVBoxLayout(basic)\n        basic_v.setContentsMargins(10, 8, 10, 8)\n        basic_v.setSpacing(8)\n        basic_title = QLabel("基本資訊")\n        basic_title.setObjectName("sectionTitle")\n        basic_v.addWidget(basic_title)\n        basic_row = QHBoxLayout()\n        basic_row.setContentsMargins(0, 0, 0, 0)\n        basic_row.setSpacing(7)\n'''
main = replace_once(main, old_basic, new_basic, "basic section shell")
main = replace_once(
    main,
    '''        basic_row.addStretch(1)\n        outer.addWidget(basic)\n\n        income = QGroupBox("收入")\n''',
    '''        basic_row.addStretch(1)\n        basic_v.addLayout(basic_row)\n        outer.addWidget(basic)\n\n        income = QGroupBox()\n''',
    "basic row attach and income title shell",
)

# Income section: internal title + fixed, consistent vertical rhythm.
main = replace_once(
    main,
    '''        income_v = QVBoxLayout(income)\n        income_v.setContentsMargins(10, 8, 10, 8)\n        income_v.setSpacing(5)\n        income_quick = QHBoxLayout()\n''',
    '''        income_v = QVBoxLayout(income)\n        income_v.setContentsMargins(10, 8, 10, 8)\n        income_v.setSpacing(8)\n        income_title = QLabel("收入")\n        income_title.setObjectName("sectionTitle")\n        income_v.addWidget(income_title)\n        income_quick = QHBoxLayout()\n''',
    "income title and spacing",
)
main = replace_once(
    main,
    '''        self.income_quick_host = QWidget()\n        self.income_quick_layout = QHBoxLayout(self.income_quick_host)\n''',
    '''        self.income_quick_host = QWidget()\n        self.income_quick_host.setFixedHeight(30)\n        self.income_quick_layout = QHBoxLayout(self.income_quick_host)\n''',
    "income quick host height",
)
main = replace_once(main, "        income_v.addLayout(income_quick)\n        income_v.addSpacing(0)\n", "        income_v.addLayout(income_quick)\n", "income redundant spacing")
main = replace_once(
    main,
    '''        income_summary_quick = QHBoxLayout(income_summary_band)\n        income_summary_quick.setContentsMargins(0, 6, 0, 0)\n''',
    '''        income_summary_quick = QHBoxLayout(income_summary_band)\n        income_summary_quick.setContentsMargins(0, 8, 0, 0)\n''',
    "income summary divider spacing",
)

# Expense section mirrors income.
main = replace_once(main, '        expense = QGroupBox("支出")\n', '        expense = QGroupBox()\n', "expense title shell")
main = replace_once(
    main,
    '''        expense_v = QVBoxLayout(expense)\n        expense_v.setContentsMargins(10, 8, 10, 8)\n        expense_v.setSpacing(5)\n        expense_quick = QHBoxLayout()\n''',
    '''        expense_v = QVBoxLayout(expense)\n        expense_v.setContentsMargins(10, 8, 10, 8)\n        expense_v.setSpacing(8)\n        expense_title = QLabel("支出")\n        expense_title.setObjectName("sectionTitle")\n        expense_v.addWidget(expense_title)\n        expense_quick = QHBoxLayout()\n''',
    "expense title and spacing",
)
main = replace_once(
    main,
    '''        self.expense_quick_host = QWidget()\n        self.expense_quick_layout = QHBoxLayout(self.expense_quick_host)\n''',
    '''        self.expense_quick_host = QWidget()\n        self.expense_quick_host.setFixedHeight(30)\n        self.expense_quick_layout = QHBoxLayout(self.expense_quick_host)\n''',
    "expense quick host height",
)
main = replace_once(main, "        expense_v.addLayout(expense_quick)\n        expense_v.addSpacing(0)\n", "        expense_v.addLayout(expense_quick)\n", "expense redundant spacing")
main = replace_once(
    main,
    '''        expense_summary_quick = QHBoxLayout(expense_summary_band)\n        expense_summary_quick.setContentsMargins(0, 6, 0, 0)\n''',
    '''        expense_summary_quick = QHBoxLayout(expense_summary_band)\n        expense_summary_quick.setContentsMargins(0, 8, 0, 0)\n''',
    "expense summary divider spacing",
)

# Confirmation panel: same internal-title pattern; fixed to the ten-entry use case.
old_confirm = '''        confirm = QGroupBox("輸入確認")\n        confirm.setObjectName("confirmationCard")\n        confirm.setSizePolicy(QSizePolicy.Policy.Expanding, QSizePolicy.Policy.Fixed)\n        confirm.setFixedHeight(240)\n        cv = QVBoxLayout(confirm)\n        cv.setContentsMargins(10, 7, 10, 7)\n        cv.setSpacing(2)\n        self.confirm_labels = []\n'''
new_confirm = '''        confirm = QGroupBox()\n        confirm.setObjectName("confirmationCard")\n        confirm.setSizePolicy(QSizePolicy.Policy.Expanding, QSizePolicy.Policy.Fixed)\n        confirm.setFixedHeight(244)\n        cv = QVBoxLayout(confirm)\n        cv.setContentsMargins(10, 7, 10, 7)\n        cv.setSpacing(2)\n        confirm_title = QLabel("輸入確認")\n        confirm_title.setObjectName("sectionTitle")\n        cv.addWidget(confirm_title)\n        cv.addSpacing(4)\n        self.confirm_labels = []\n'''
main = replace_once(main, old_confirm, new_confirm, "confirmation internal title")

# Match entry action button height to the actual monetary input height after layout/style resolution.
main = replace_once(
    main,
    '''        self.expense_category.currentIndexChanged.connect(lambda *_: self._build_quick_summary_buttons("expense"))\n\n    def _clear_layout(self, layout: QHBoxLayout):\n''',
    '''        self.expense_category.currentIndexChanged.connect(lambda *_: self._build_quick_summary_buttons("expense"))\n        QTimer.singleShot(0, self._sync_entry_action_heights)\n\n    def _sync_entry_action_heights(self):\n        for field, button in (\n            (self.income_amount, self.income_save),\n            (self.expense_amount, self.expense_save),\n        ):\n            target_height = max(field.height(), field.sizeHint().height())\n            if target_height > 0:\n                button.setFixedHeight(target_height)\n\n    def _clear_layout(self, layout: QHBoxLayout):\n''',
    "entry action height sync",
)

# Minimum and initial sizes are intentionally identical: the fixed ten-entry
# confirmation panel must fit at the smallest supported 100% / 96-DPI window.
main = replace_once(
    main,
    '        self.setMinimumSize(1100, 760)\n        self.resize(1120, 760)\n',
    '        self.setMinimumSize(1120, 760)\n        self.resize(1120, 760)\n',
    "main minimum and initial size",
)

# Targeted QSS: internal section titles, neutral basic section, and dark dashed summary divider.
old_sections = '''QGroupBox#confirmationCard {{\n    border: 1px solid {NEUTRAL_BORDER};\n    border-radius: 6px;\n    margin-top: 12px;\n    padding: 10px 10px 8px 10px;\n    background: {NEUTRAL_WINDOW};\n}}\nQGroupBox#inputSection, QGroupBox#incomeSection, QGroupBox#expenseSection {{\n    border: 1px solid {NEUTRAL_BORDER};\n    border-radius: 6px;\n    margin-top: 14px;\n    padding: 12px 10px 8px 10px;\n}}\nQGroupBox#inputSection {{\n    background: {NEUTRAL_WHITE};\n}}\nQGroupBox#incomeSection {{\n    background: #E7F3E9;\n    border-color: #C7DCCB;\n}}\nQGroupBox#expenseSection {{\n    background: #F8E9E7;\n    border-color: #E7CECA;\n}}\nQGroupBox#inputSection::title {{\n    left: 10px;\n    padding: 0 3px;\n    background: transparent;\n}}\nQGroupBox#incomeSection::title {{\n    left: 10px;\n    padding: 0 3px;\n    background: transparent;\n}}\nQGroupBox#expenseSection::title {{\n    left: 10px;\n    padding: 0 3px;\n    background: transparent;\n}}\n'''
new_sections = '''QGroupBox#confirmationCard, QGroupBox#inputSection, QGroupBox#incomeSection, QGroupBox#expenseSection {{\n    border: 1px solid {NEUTRAL_BORDER};\n    border-radius: 6px;\n    margin-top: 0;\n    padding: 0;\n}}\nQGroupBox#confirmationCard, QGroupBox#inputSection {{\n    background: {NEUTRAL_WINDOW};\n}}\nQGroupBox#incomeSection {{\n    background: #E7F3E9;\n    border-color: #C7DCCB;\n}}\nQGroupBox#expenseSection {{\n    background: #F8E9E7;\n    border-color: #E7CECA;\n}}\nQLabel#sectionTitle {{\n    background: transparent;\n    color: {TEXT_PRIMARY};\n    font-size: 11.5pt;\n    font-weight: 600;\n}}\n'''
theme = replace_once(theme, old_sections, new_sections, "section shell styles")

# Remove the obsolete confirmation GroupBox legend rule now that the title is internal.
theme = replace_once(
    theme,
    '''QGroupBox#confirmationCard::title {{\n    left: 10px;\n    padding: 0 3px;\n    background: transparent;\n}}\n\n''',
    '',
    "obsolete confirmation legend style",
)

theme = replace_once(
    theme,
    '''QWidget#summaryBand {{\n    border: 0;\n    border-top: 1px solid {NEUTRAL_DIVIDER};\n    background: transparent;\n}}\n''',
    '''QWidget#summaryBand {{\n    border: 0;\n    border-top: 1px dashed {TEXT_PRIMARY};\n    background: transparent;\n}}\n''',
    "dark dashed summary divider",
)

note = "- Phase 1 input refinement: internal section titles replace GroupBox legend labels; Basic Information shares the confirmation neutral fill; income/expense vertical rhythm is fixed at 8px around the dark dashed summary divider; save-button height follows the adjacent amount field; minimum/initial window size is 1120x760; secondary dialogs suppress the app icon while native MessageBox behavior remains unchanged.\n"
if note not in version:
    version += note

main_path.write_text(main, encoding="utf-8")
theme_path.write_text(theme, encoding="utf-8")
version_path.write_text(version, encoding="utf-8")
print("CYAccounting input layout pass 3 applied")
