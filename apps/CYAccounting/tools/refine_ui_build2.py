from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def replace_once(text: str, old: str, new: str, label: str) -> str:
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"{label}: expected 1 occurrence, found {count}")
    return text.replace(old, new, 1)


main_path = ROOT / "app" / "main.py"
main = main_path.read_text(encoding="utf-8")
old_icon = '''    @staticmethod
    def _clear_secondary_dialog_icon(dialog: QDialog) -> None:
        # CY Desktop visual rule: only the main application window carries the
        # canonical family icon. Keep native dialog chrome/behavior.
        dialog.setWindowIcon(QIcon())
        if sys.platform == "win32":
            try:
                import ctypes
                hwnd = int(dialog.winId())
                user32 = ctypes.windll.user32
                wm_seticon = 0x0080
                user32.SendMessageW(hwnd, wm_seticon, 0, 0)  # ICON_SMALL
                user32.SendMessageW(hwnd, wm_seticon, 1, 0)  # ICON_BIG
            except Exception:
                pass
'''
new_icon = '''    @staticmethod
    def _clear_secondary_dialog_icon(dialog: QDialog) -> None:
        # CY Desktop visual rule: the main window owns the product icon.
        # Secondary dialogs must have no title-bar icon at all; an empty Qt icon
        # alone lets Windows fall back to the generic application icon.
        dialog.setWindowIcon(QIcon())
        if sys.platform == "win32":
            try:
                import ctypes
                hwnd = int(dialog.winId())
                user32 = ctypes.windll.user32
                GWL_EXSTYLE = -20
                WS_EX_DLGMODALFRAME = 0x00000001
                WM_SETICON = 0x0080
                ICON_SMALL = 0
                ICON_BIG = 1
                SWP_NOSIZE = 0x0001
                SWP_NOMOVE = 0x0002
                SWP_NOZORDER = 0x0004
                SWP_NOACTIVATE = 0x0010
                SWP_FRAMECHANGED = 0x0020

                ex_style = user32.GetWindowLongW(hwnd, GWL_EXSTYLE)
                user32.SetWindowLongW(hwnd, GWL_EXSTYLE, ex_style | WS_EX_DLGMODALFRAME)
                user32.SendMessageW(hwnd, WM_SETICON, ICON_SMALL, 0)
                user32.SendMessageW(hwnd, WM_SETICON, ICON_BIG, 0)
                user32.SetWindowPos(
                    hwnd,
                    0,
                    0,
                    0,
                    0,
                    0,
                    SWP_NOSIZE | SWP_NOMOVE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED,
                )
            except Exception:
                pass
'''
main = replace_once(main, old_icon, new_icon, "secondary dialog icon helper")
main = replace_once(
    main,
    "    def _build(self):\n        outer = QVBoxLayout(self)\n        outer.setContentsMargins(12, 6, 12, 8)\n",
    "    def _build(self):\n        outer = QVBoxLayout(self)\n        outer.setContentsMargins(12, 8, 12, 8)\n",
    "InputTab _build top margin",
)
main = replace_once(
    main,
    "        confirm.setFixedHeight(244)\n",
    "        confirm.setFixedHeight(224)\n",
    "confirmation height",
)
main = replace_once(
    main,
    "        self.setMinimumSize(1120, 760)\n        self.resize(1120, 760)\n",
    "        self.setMinimumSize(1120, 740)\n        self.resize(1120, 740)\n",
    "main window size",
)
main_path.write_text(main, encoding="utf-8")


theme_path = ROOT / "app" / "theme.py"
theme = theme_path.read_text(encoding="utf-8")
old_titles = '''QGroupBox#confirmationCard::title, QGroupBox#inputSection::title, QGroupBox#incomeSection::title, QGroupBox#expenseSection::title {{
    subcontrol-origin: margin;
    subcontrol-position: top left;
    left: 10px;
    padding: 0 5px;
    background: {NEUTRAL_WHITE};
    color: {TEXT_PRIMARY};
    font-size: 11.5pt;
    font-weight: 600;
}}
'''
new_titles = '''QGroupBox#confirmationCard::title, QGroupBox#inputSection::title, QGroupBox#incomeSection::title, QGroupBox#expenseSection::title {{
    subcontrol-origin: margin;
    subcontrol-position: top left;
    left: 10px;
    padding: 0 2px;
    background: {NEUTRAL_WHITE};
    color: {TEXT_PRIMARY};
    font-size: 11.5pt;
    font-weight: 600;
}}
QDialog#settingsDialog QGroupBox {{
    border: 1px solid {NEUTRAL_BORDER};
    border-radius: 6px;
    margin-top: 10px;
    padding: 0;
    background: {NEUTRAL_WHITE};
    font-weight: normal;
}}
QDialog#settingsDialog QGroupBox::title {{
    subcontrol-origin: margin;
    subcontrol-position: top left;
    left: 10px;
    padding: 0 2px;
    background: {NEUTRAL_WHITE};
    color: {TEXT_PRIMARY};
    font-size: 11.5pt;
    font-weight: 600;
}}
'''
theme = replace_once(theme, old_titles, new_titles, "GroupBox legend and settings style")
theme_path.write_text(theme, encoding="utf-8")


dialogs_path = ROOT / "app" / "dialogs.py"
dialogs = dialogs_path.read_text(encoding="utf-8")
dialogs = replace_once(
    dialogs,
    '''        self.setModal(True)
        self.resize(760, 700)
        outer = QVBoxLayout(self)
        outer.setContentsMargins(16, 12, 16, 12)
        outer.setSpacing(10)
''',
    '''        self.setModal(True)
        self.resize(760, 660)
        outer = QVBoxLayout(self)
        outer.setContentsMargins(12, 8, 12, 8)
        outer.setSpacing(6)
''',
    "settings dialog outer layout",
)
dialogs = replace_once(
    dialogs,
    "        db_layout = QGridLayout(db_box)\n",
    '''        db_layout = QGridLayout(db_box)
        db_layout.setContentsMargins(12, 8, 12, 8)
        db_layout.setHorizontalSpacing(8)
        db_layout.setVerticalSpacing(6)
''',
    "settings database layout",
)
dialogs = replace_once(
    dialogs,
    "        lock_layout.setContentsMargins(12, 10, 12, 10)\n",
    "        lock_layout.setContentsMargins(12, 7, 12, 7)\n",
    "settings lock margins",
)
dialogs = replace_once(
    dialogs,
    "        backup_layout.setContentsMargins(12, 10, 12, 10)\n        backup_layout.setSpacing(16)\n",
    "        backup_layout.setContentsMargins(12, 8, 12, 8)\n        backup_layout.setSpacing(8)\n",
    "settings backup spacing",
)
dialogs = replace_once(
    dialogs,
    "        info_layout = QGridLayout(info_box)\n",
    '''        info_layout = QGridLayout(info_box)
        info_layout.setContentsMargins(12, 8, 12, 8)
        info_layout.setHorizontalSpacing(8)
        info_layout.setVerticalSpacing(4)
''',
    "settings info layout",
)
dialogs = replace_once(
    dialogs,
    "        outer.addWidget(info_box)\n        outer.addStretch()\n",
    "        outer.addWidget(info_box)\n        outer.addSpacing(2)\n",
    "settings bottom stretch",
)
dialogs_path.write_text(dialogs, encoding="utf-8")


build_path = ROOT / "BUILD"
if build_path.read_text(encoding="utf-8").strip() != "1":
    raise SystemExit("BUILD must be 1 before Build 2 refinement")
build_path.write_text("2\n", encoding="utf-8")

notes_path = ROOT / "V1.2.0.txt"
notes = notes_path.read_text(encoding="utf-8")
notes = replace_once(notes, "Build: 1\n", "Build: 2\n", "Build note")
notes = replace_once(
    notes,
    "MainWindow minimum size and initial size are both 1120x760",
    "MainWindow minimum size and initial size are both 1120x740",
    "window size note",
)
notes = replace_once(
    notes,
    "Secondary dialogs / management / settings windows suppress the app icon while native MessageBox and native QFileDialog keep OS-native shell behavior.",
    "Secondary dialogs / management / settings windows show no title-bar icon at all (including no Windows generic fallback icon), while native MessageBox and native QFileDialog keep OS-native shell behavior.",
    "dialog icon note",
)
notes = replace_once(
    notes,
    "- Settings and Import dialogs receive canonical spacing/density treatment while preserving their existing behavior and workflow.\n",
    "- Settings uses compact framed GroupBox sections for database location, lock, ledger, common summary, backup/restore, Google Drive and program information; excess inter-section whitespace is removed while behavior is unchanged. Import keeps its existing canonical spacing/density treatment.\n",
    "settings note",
)
notes_path.write_text(notes, encoding="utf-8")

print("CYAccounting V1.2.0 Build 2 UI refinement applied")
