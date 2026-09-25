from __future__ import annotations

from util import app_root

# CY Desktop Visual Guide — Phase 1 / Blue Theme
# Canonical source: AITeam/shared/cy-visual/desktop/CY_DESKTOP_VISUAL_GUIDE.md

NEUTRAL_WHITE = "#FFFFFF"
NEUTRAL_WINDOW = "#F8FAFC"
NEUTRAL_READ_ONLY = "#F1F3F5"
NEUTRAL_BORDER = "#D1D5DB"
NEUTRAL_DIVIDER = "#E5E8EC"
NEUTRAL_GRID = "#DDE1E6"
TEXT_PRIMARY = "#1F2937"
TEXT_SECONDARY = "#667085"
TEXT_DISABLED = "#98A2B3"

ACCENT = "#2563EB"
ACCENT_HOVER = "#1D4ED8"
ACCENT_PRESSED = "#1E40AF"
ACCENT_SOFT = "#DBEAFE"
ACCENT_FOCUS = "#60A5FA"

SUCCESS = "#21825C"
SUCCESS_SOFT = "#EAF5F0"
WARNING = "#A66B10"
WARNING_SOFT = "#FAF1E3"
DANGER = "#B43737"
DANGER_SOFT = "#F8EAEA"
INFO = "#356A9A"
INFO_SOFT = "#EAF1F7"

# Accounting-domain colors are deliberately separate from UI state colors.
INCOME_TEXT = "#2E6F5E"
INCOME_SOFT = "#EDF5F2"
EXPENSE_TEXT = "#8A5B5B"
EXPENSE_SOFT = "#F6EEEE"

COMBO_ARROW_PATH = (app_root() / "app" / "resources" / "combo_arrow.png").as_posix()

APP_STYLE = f"""
QMainWindow, QDialog {{
    background: {NEUTRAL_WINDOW};
}}
QWidget {{
    font-family: "Microsoft JhengHei UI", "Microsoft JhengHei", "Segoe UI", sans-serif;
    font-size: 10.5pt;
    color: {TEXT_PRIMARY};
}}
QLabel {{
    background: transparent;
}}

/* Continuous accounting workspace: section titles carry hierarchy, not cards. */
QGroupBox {{
    border: 0;
    border-radius: 0;
    margin-top: 18px;
    padding: 14px 0 4px 0;
    background: transparent;
    font-weight: normal;
}}
QGroupBox::title {{
    subcontrol-origin: margin;
    left: 0;
    padding: 0 2px 0 0;
    color: {TEXT_PRIMARY};
    font-size: 12pt;
    font-weight: 600;
}}
QGroupBox#statsCard {{
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
QGroupBox#confirmationCard, QGroupBox#inputSection, QGroupBox#incomeSection, QGroupBox#expenseSection {{
    border: 1px solid {NEUTRAL_BORDER};
    border-radius: 6px;
    margin-top: 10px;
    padding: 0;
    background: {NEUTRAL_WHITE};
    /* QGroupBox title painting on Windows follows the group font itself. */
    font-weight: 700;
}}
QGroupBox#confirmationCard QWidget, QGroupBox#inputSection QWidget, QGroupBox#incomeSection QWidget, QGroupBox#expenseSection QWidget {{
    font-weight: normal;
}}
QGroupBox#confirmationCard::title, QGroupBox#inputSection::title, QGroupBox#incomeSection::title, QGroupBox#expenseSection::title {{
    subcontrol-origin: margin;
    subcontrol-position: top left;
    left: 10px;
    padding: 0 2px;
    color: {TEXT_PRIMARY};
    font-size: 11.5pt;
    font-weight: 700;
}}
QDialog#settingsDialog QGroupBox {{
    border: 1px solid {NEUTRAL_BORDER};
    border-radius: 6px;
    margin-top: 10px;
    padding: 0;
    background: {NEUTRAL_WHITE};
    font-weight: 700;
}}
QDialog#settingsDialog QGroupBox QWidget {{
    font-weight: normal;
}}
QDialog#settingsDialog QGroupBox::title {{
    subcontrol-origin: margin;
    subcontrol-position: top left;
    left: 10px;
    padding: 0 2px;
    color: {TEXT_PRIMARY};
    font-size: 11.5pt;
    font-weight: 700;
}}
QWidget#inputPage QLineEdit, QWidget#inputPage QComboBox {{
    min-height: 30px;
    font-size: 11pt;
}}
QWidget#inputPage QPushButton#primaryButton, QWidget#inputPage QPushButton#accountChoiceButton {{
    min-height: 30px;
    font-size: 10.5pt;
}}

QGroupBox#statsCard::title {{
    subcontrol-origin: margin;
    subcontrol-position: top left;
    left: 10px;
    padding: 0 2px;
    color: {TEXT_PRIMARY};
    font-size: 11.5pt;
    font-weight: 700;
}}

/* Category manager uses the canonical Header-only Custom tab direction:
   large/equal hit targets, low visual presence, stronger selected weight and
   a 2px Accent underline. The QTabWidget still owns page lifecycle/keyboard. */
QTabWidget#categoryManagerTabs::pane {{
    border: 1px solid {NEUTRAL_BORDER};
    border-radius: 4px;
    background: {NEUTRAL_WHITE};
    top: -1px;
}}
QTabWidget#categoryManagerTabs QTabBar::tab {{
    min-height: 36px;
    min-width: 140px;
    padding: 8px 18px;
    margin: 0;
    border: 0;
    border-bottom: 2px solid transparent;
    background: transparent;
    color: {TEXT_SECONDARY};
    font-size: 11.5pt;
    font-weight: 600;
}}
QTabWidget#categoryManagerTabs QTabBar::tab:selected {{
    color: {TEXT_PRIMARY};
    border-bottom: 2px solid {ACCENT};
    font-weight: 700;
}}
QTabWidget#categoryManagerTabs QTabBar::tab:hover:!selected {{
    color: {ACCENT_HOVER};
    background: {NEUTRAL_WINDOW};
}}
QLineEdit, QComboBox, QSpinBox, QDateEdit {{
    border: 1px solid {NEUTRAL_BORDER};
    border-radius: 4px;
    padding: 4px 7px;
    background: {NEUTRAL_WHITE};
    color: {TEXT_PRIMARY};
    selection-background-color: {ACCENT_SOFT};
    selection-color: {TEXT_PRIMARY};
}}
QComboBox {{
    padding-right: 34px;
}}
QComboBox::drop-down {{
    subcontrol-origin: padding;
    subcontrol-position: top right;
    width: 30px;
    border-left: 1px solid #AEB9C5;
    background: #F1F4F7;
    border-top-right-radius: 4px;
    border-bottom-right-radius: 4px;
}}
QComboBox::drop-down:hover {{
    background: #E0E7EE;
}}
QComboBox::drop-down:disabled {{
    background: {NEUTRAL_READ_ONLY};
    border-left-color: {NEUTRAL_BORDER};
}}
QComboBox::down-arrow {{
    image: url("{COMBO_ARROW_PATH}");
    width: 14px;
    height: 9px;
}}
QLineEdit:focus, QComboBox:focus, QSpinBox:focus, QDateEdit:focus {{
    border: 1px solid {ACCENT_FOCUS};
}}
QLineEdit:read-only {{
    background: {NEUTRAL_READ_ONLY};
    color: {TEXT_SECONDARY};
}}
QLineEdit:disabled, QComboBox:disabled, QSpinBox:disabled, QDateEdit:disabled {{
    background: {NEUTRAL_READ_ONLY};
    color: {TEXT_DISABLED};
}}
QLineEdit[monthError="true"] {{
    border: 1px solid {DANGER};
}}

QPushButton {{
    padding: 5px 12px;
    border: 1px solid {NEUTRAL_BORDER};
    border-radius: 4px;
    background: {NEUTRAL_WHITE};
    color: {TEXT_PRIMARY};
}}
QPushButton:hover {{ background: {NEUTRAL_WINDOW}; }}
QPushButton:pressed {{ background: {NEUTRAL_READ_ONLY}; }}
QPushButton:disabled {{ color: {TEXT_DISABLED}; background: {NEUTRAL_READ_ONLY}; }}
QPushButton#primaryButton {{
    background: {ACCENT};
    color: {NEUTRAL_WHITE};
    border-color: {ACCENT};
    font-weight: 600;
}}
QPushButton#primaryButton:hover {{ background: {ACCENT_HOVER}; border-color: {ACCENT_HOVER}; }}
QPushButton#primaryButton:pressed {{ background: {ACCENT_PRESSED}; border-color: {ACCENT_PRESSED}; }}
QPushButton#dangerButton {{
    color: {DANGER};
    border-color: {DANGER};
    background: {NEUTRAL_WHITE};
    font-weight: 600;
}}
QPushButton#dangerButton:hover {{ background: {DANGER_SOFT}; }}
QPushButton#topActionButton {{
    background: {NEUTRAL_WHITE};
    color: {TEXT_PRIMARY};
    border-color: {NEUTRAL_BORDER};
}}
QPushButton#topActionButton:hover {{ background: {NEUTRAL_WINDOW}; }}

QPushButton#accountChoiceButton {{
    background: {NEUTRAL_WHITE};
    color: {TEXT_PRIMARY};
    border: 1px solid {NEUTRAL_BORDER};
    font-weight: normal;
}}
QPushButton#accountChoiceButton:hover {{ background: {NEUTRAL_WINDOW}; }}
QPushButton#accountChoiceButton:checked {{
    background: {ACCENT_SOFT};
    color: {ACCENT_PRESSED};
    border: 1px solid {ACCENT};
    font-weight: 600;
}}
QPushButton#quickCategoryButton {{
    padding: 4px 9px;
}}
QPushButton#quickSummaryButton {{
    padding: 3px 8px;
    color: {TEXT_SECONDARY};
    background: {NEUTRAL_WHITE};
    border: 1px solid {NEUTRAL_BORDER};
    border-radius: 3px;
}}
QPushButton#quickSummaryButton:hover {{ background: {NEUTRAL_WINDOW}; border-color: {ACCENT_FOCUS}; }}
QWidget#summaryBand {{
    border: 0;
    border-top: 1px dashed {TEXT_PRIMARY};
    background: transparent;
}}

QToolButton#dateStepUp, QToolButton#dateStepDown {{
    padding: 0;
    border: 1px solid {NEUTRAL_BORDER};
    background: {NEUTRAL_WHITE};
}}
QToolButton#dateStepUp {{
    border-top-left-radius: 3px;
    border-top-right-radius: 3px;
    border-bottom: 0;
}}
QToolButton#dateStepDown {{
    border-bottom-left-radius: 3px;
    border-bottom-right-radius: 3px;
}}
QToolButton#dateStepUp:hover, QToolButton#dateStepDown:hover {{ background: {NEUTRAL_WINDOW}; }}
QToolButton#dateStepUp:pressed, QToolButton#dateStepDown:pressed {{ background: {NEUTRAL_READ_ONLY}; }}
QPushButton#calendarButton {{
    padding: 0;
    border: 1px solid {NEUTRAL_BORDER};
    background: {NEUTRAL_WHITE};
}}
QPushButton#calendarButton:hover {{ background: {NEUTRAL_WINDOW}; }}
QPushButton#monthNavButton {{
    min-width: 36px;
    padding-left: 0;
    padding-right: 0;
    font-weight: 600;
}}

/* Header-only tab treatment. */
QTabBar#mainTabBar::tab {{
    background: transparent;
    color: {TEXT_SECONDARY};
    border: 0;
    border-bottom: 2px solid transparent;
    padding: 8px 14px;
    min-width: 100px;
    margin-right: 4px;
}}
QTabBar#mainTabBar::tab:selected {{
    color: {TEXT_PRIMARY};
    border-bottom: 2px solid {ACCENT};
    font-weight: 600;
}}
QTabBar#mainTabBar::tab:hover {{ color: {ACCENT_HOVER}; }}
QFrame#pageContainer {{
    border: 1px solid {NEUTRAL_BORDER};
    border-radius: 6px;
    background: {NEUTRAL_WINDOW};
}}

QTableView {{
    background: {NEUTRAL_WHITE};
    alternate-background-color: {NEUTRAL_WINDOW};
    border: 1px solid {NEUTRAL_BORDER};
    gridline-color: {NEUTRAL_GRID};
    selection-background-color: {ACCENT_SOFT};
    selection-color: {TEXT_PRIMARY};
}}
QHeaderView::section {{
    background: {NEUTRAL_WINDOW};
    color: {TEXT_PRIMARY};
    border: 0;
    border-right: 1px solid {NEUTRAL_GRID};
    border-bottom: 1px solid {NEUTRAL_DIVIDER};
    padding: 2px 4px;
    font-weight: 600;
}}
QTreeWidget, QListWidget, QComboBox QAbstractItemView {{
    background: {NEUTRAL_WHITE};
    color: {TEXT_PRIMARY};
    selection-background-color: {ACCENT_SOFT};
    selection-color: {TEXT_PRIMARY};
}}
QToolTip {{
    background: {NEUTRAL_WHITE};
    color: {TEXT_PRIMARY};
    border: 1px solid {NEUTRAL_BORDER};
    padding: 5px;
}}
"""

# Import is a genuinely multi-unit workflow, but its section chrome must use
# the same legend treatment as the main input page and Settings.
IMPORT_DIALOG_STYLE = f"""
QDialog#importDialog QGroupBox {{
    border: 1px solid {NEUTRAL_BORDER};
    border-radius: 6px;
    margin-top: 10px;
    padding: 0;
    background: {NEUTRAL_WHITE};
    font-weight: 700;
}}
QDialog#importDialog QGroupBox QWidget {{
    font-weight: normal;
}}
QDialog#importDialog QGroupBox::title {{
    subcontrol-origin: margin;
    subcontrol-position: top left;
    left: 10px;
    padding: 0 2px;
    color: {TEXT_PRIMARY};
    font-size: 11.5pt;
    font-weight: 700;
}}
"""

CATEGORY_POPUP_STYLE = f"""
QAbstractItemView {{
    border: 1px solid {NEUTRAL_BORDER};
    background: {NEUTRAL_WHITE};
    color: {TEXT_PRIMARY};
    outline: 0;
    selection-background-color: {ACCENT_SOFT};
    selection-color: {TEXT_PRIMARY};
}}
"""

CALENDAR_STYLE = f"""
QCalendarWidget QAbstractItemView::item:selected {{
    background: {ACCENT_SOFT};
    color: {TEXT_PRIMARY};
    font-weight: 600;
    border: 1px solid {ACCENT_FOCUS};
}}
"""
