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

# Save actions are ordinary entry actions, not page-primary actions.
main = replace_once(
    main,
    '        self.income_save = no_tab_button("存入收入")\n        self.income_save.setObjectName("primaryButton")\n        self.income_save.setFixedWidth(92)\n',
    '        self.income_save = no_tab_button("存入收入")\n        self.income_save.setFixedWidth(92)\n',
    "income save button",
)
main = replace_once(
    main,
    '        self.expense_save = no_tab_button("存入支出")\n        self.expense_save.setObjectName("primaryButton")\n        self.expense_save.setFixedWidth(92)\n',
    '        self.expense_save = no_tab_button("存入支出")\n        self.expense_save.setFixedWidth(92)\n',
    "expense save button",
)

# The separator belongs to the summary band itself.  Do not insert it as an
# independent QFrame row, because an independent row participates in the
# section layout and visually drifts as the form reflows.
income_old = '''        income_sep = QFrame()\n        income_sep.setObjectName("summarySeparator")\n        income_v.addWidget(income_sep)\n        income_summary_quick = QHBoxLayout()\n        income_summary_quick.setSpacing(5)\n        income_summary_quick.addWidget(QLabel("常用摘要："))\n        self.income_quick_summary_host = QWidget()\n        # Keep the common-summary row at a constant height whether it contains\n        # buttons or the empty-state hint.  This prevents the whole input form\n        # from shifting vertically when the selected category changes.\n        self.income_quick_summary_host.setFixedHeight(28)\n        self.income_quick_summary_layout = QHBoxLayout(self.income_quick_summary_host)\n        self.income_quick_summary_layout.setContentsMargins(0, 3, 0, 3)\n        self.income_quick_summary_layout.setSpacing(5)\n        income_summary_quick.addWidget(self.income_quick_summary_host)\n        income_summary_quick.addStretch(1)\n        income_v.addLayout(income_summary_quick)\n'''
income_new = '''        income_summary_band = QWidget()\n        income_summary_band.setObjectName("summaryBand")\n        income_summary_quick = QHBoxLayout(income_summary_band)\n        income_summary_quick.setContentsMargins(0, 6, 0, 0)\n        income_summary_quick.setSpacing(5)\n        income_summary_quick.addWidget(QLabel("常用摘要："))\n        self.income_quick_summary_host = QWidget()\n        # Keep the common-summary row at a constant height whether it contains\n        # buttons or the empty-state hint.  This prevents the whole input form\n        # from shifting vertically when the selected category changes.\n        self.income_quick_summary_host.setFixedHeight(28)\n        self.income_quick_summary_layout = QHBoxLayout(self.income_quick_summary_host)\n        self.income_quick_summary_layout.setContentsMargins(0, 3, 0, 3)\n        self.income_quick_summary_layout.setSpacing(5)\n        income_summary_quick.addWidget(self.income_quick_summary_host)\n        income_summary_quick.addStretch(1)\n        income_v.addWidget(income_summary_band)\n'''
main = replace_once(main, income_old, income_new, "income summary band")

expense_old = '''        expense_sep = QFrame()\n        expense_sep.setObjectName("summarySeparator")\n        expense_v.addWidget(expense_sep)\n        expense_summary_quick = QHBoxLayout()\n        expense_summary_quick.setSpacing(5)\n        expense_summary_quick.addWidget(QLabel("常用摘要："))\n        self.expense_quick_summary_host = QWidget()\n        self.expense_quick_summary_host.setFixedHeight(28)\n        self.expense_quick_summary_layout = QHBoxLayout(self.expense_quick_summary_host)\n        self.expense_quick_summary_layout.setContentsMargins(0, 3, 0, 3)\n        self.expense_quick_summary_layout.setSpacing(5)\n        expense_summary_quick.addWidget(self.expense_quick_summary_host)\n        expense_summary_quick.addStretch(1)\n        expense_v.addLayout(expense_summary_quick)\n'''
expense_new = '''        expense_summary_band = QWidget()\n        expense_summary_band.setObjectName("summaryBand")\n        expense_summary_quick = QHBoxLayout(expense_summary_band)\n        expense_summary_quick.setContentsMargins(0, 6, 0, 0)\n        expense_summary_quick.setSpacing(5)\n        expense_summary_quick.addWidget(QLabel("常用摘要："))\n        self.expense_quick_summary_host = QWidget()\n        self.expense_quick_summary_host.setFixedHeight(28)\n        self.expense_quick_summary_layout = QHBoxLayout(self.expense_quick_summary_host)\n        self.expense_quick_summary_layout.setContentsMargins(0, 3, 0, 3)\n        self.expense_quick_summary_layout.setSpacing(5)\n        expense_summary_quick.addWidget(self.expense_quick_summary_host)\n        expense_summary_quick.addStretch(1)\n        expense_v.addWidget(expense_summary_band)\n'''
main = replace_once(main, expense_old, expense_new, "expense summary band")

# The ten confirmation rows are a required part of the input workspace.  The
# main window may not be resized below the height at which this card is fully
# visible at the accepted 100% / 96-DPI baseline.
main = replace_once(
    main,
    '        self.setMinimumSize(980, 560)\n        self.resize(1120, 620)\n',
    '        self.setMinimumSize(1100, 760)\n        self.resize(1120, 760)\n',
    "main window minimum size",
)

# Remove filled title patches.  The title itself stays on the GroupBox margin,
# but its background is transparent so it no longer looks like a pasted label.
for label, old, new in [
    (
        "input title",
        '''QGroupBox#inputSection::title {{\n    left: 10px;\n    padding: 0 5px;\n    background: {NEUTRAL_WHITE};\n}}\n''',
        '''QGroupBox#inputSection::title {{\n    left: 10px;\n    padding: 0 3px;\n    background: transparent;\n}}\n''',
    ),
    (
        "income title",
        '''QGroupBox#incomeSection::title {{\n    left: 10px;\n    padding: 0 5px;\n    background: #E7F3E9;\n}}\n''',
        '''QGroupBox#incomeSection::title {{\n    left: 10px;\n    padding: 0 3px;\n    background: transparent;\n}}\n''',
    ),
    (
        "expense title",
        '''QGroupBox#expenseSection::title {{\n    left: 10px;\n    padding: 0 5px;\n    background: #F8E9E7;\n}}\n''',
        '''QGroupBox#expenseSection::title {{\n    left: 10px;\n    padding: 0 3px;\n    background: transparent;\n}}\n''',
    ),
    (
        "confirmation title",
        '''QGroupBox#confirmationCard::title {{\n    left: 10px;\n    padding: 0 5px;\n    background: {NEUTRAL_WINDOW};\n}}\n''',
        '''QGroupBox#confirmationCard::title {{\n    left: 10px;\n    padding: 0 3px;\n    background: transparent;\n}}\n''',
    ),
]:
    theme = replace_once(theme, old, new, label)

# The divider is a border of the summary band itself, so its Y position is
# stable and cannot become an independent layout row.
theme = replace_once(
    theme,
    '''QFrame#summarySeparator {{\n    border: 0;\n    border-top: 1px solid {NEUTRAL_DIVIDER};\n    min-height: 1px;\n    max-height: 1px;\n}}\n''',
    '''QWidget#summaryBand {{\n    border: 0;\n    border-top: 1px solid {NEUTRAL_DIVIDER};\n    background: transparent;\n}}\n''',
    "summary band style",
)

note = "- Input-section refinement: save buttons return to ordinary button styling, GroupBox title patches are transparent, summary dividers are attached to the summary band instead of being independent layout rows, and the MainWindow minimum size is raised to keep all 10 confirmation rows visible.\n"
if note not in version:
    version += note

main_path.write_text(main, encoding="utf-8")
theme_path.write_text(theme, encoding="utf-8")
version_path.write_text(version, encoding="utf-8")
print("input layout pass 2 applied")
