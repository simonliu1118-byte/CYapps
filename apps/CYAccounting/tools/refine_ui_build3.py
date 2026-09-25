from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def replace_once(text: str, old: str, new: str, label: str) -> str:
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"{label}: expected 1 occurrence, found {count}")
    return text.replace(old, new, 1)


def replace_in_class(text: str, class_name: str, next_class_name: str, old: str, new: str, label: str) -> str:
    start = text.index(f"class {class_name}")
    end = text.index(f"\nclass {next_class_name}", start)
    section = text[start:end]
    count = section.count(old)
    if count != 1:
        raise SystemExit(f"{label}: expected 1 occurrence in {class_name}, found {count}")
    section = section.replace(old, new, 1)
    return text[:start] + section + text[end:]


main_path = ROOT / "app" / "main.py"
main = main_path.read_text(encoding="utf-8")

old_icon = '''    @staticmethod
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
new_icon = '''    @staticmethod
    def _clear_secondary_dialog_icon(dialog: QDialog) -> None:
        # CY Desktop visual rule: only the main window displays the product icon.
        # WM_SETICON(NULL) lets Windows fall back to the application/class icon,
        # so secondary dialogs receive a cached fully-transparent native HICON.
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

                blank_icon = getattr(ChineseStandardButtonFilter, "_blank_dialog_hicon", None)
                if not blank_icon:
                    create_icon = user32.CreateIcon
                    create_icon.restype = ctypes.c_void_p
                    # 16x16 monochrome icon: AND=1 and XOR=0 means fully transparent.
                    and_mask = (ctypes.c_ubyte * 32)(*([0xFF] * 32))
                    xor_mask = (ctypes.c_ubyte * 32)(*([0x00] * 32))
                    blank_icon = create_icon(None, 16, 16, 1, 1, and_mask, xor_mask)
                    if blank_icon:
                        ChineseStandardButtonFilter._blank_dialog_hicon = blank_icon

                ex_style = user32.GetWindowLongW(hwnd, GWL_EXSTYLE)
                user32.SetWindowLongW(hwnd, GWL_EXSTYLE, ex_style | WS_EX_DLGMODALFRAME)
                if blank_icon:
                    user32.SendMessageW(hwnd, WM_SETICON, ICON_SMALL, blank_icon)
                    user32.SendMessageW(hwnd, WM_SETICON, ICON_BIG, blank_icon)
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
main = replace_once(main, old_icon, new_icon, "secondary dialog transparent native icon")

main = replace_in_class(
    main,
    "InputTab(QWidget):",
    "LedgerTab(QWidget):",
    "        outer.setContentsMargins(12, 8, 12, 8)\n",
    "        outer.setContentsMargins(12, 14, 12, 8)\n",
    "InputTab top spacing",
)
main = replace_once(
    main,
    "        self.setMinimumSize(1120, 740)\n        self.resize(1120, 740)\n",
    "        self.setMinimumSize(1120, 710)\n        self.resize(1120, 710)\n",
    "main window compact height",
)
main_path.write_text(main, encoding="utf-8")


theme_path = ROOT / "app" / "theme.py"
theme = theme_path.read_text(encoding="utf-8")
old_main_title = '''QGroupBox#confirmationCard::title, QGroupBox#inputSection::title, QGroupBox#incomeSection::title, QGroupBox#expenseSection::title {{
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
new_main_title = '''QGroupBox#confirmationCard::title, QGroupBox#inputSection::title, QGroupBox#incomeSection::title, QGroupBox#expenseSection::title {{
    subcontrol-origin: margin;
    subcontrol-position: top left;
    left: 10px;
    padding: 0 2px;
    color: {TEXT_PRIMARY};
    font-size: 11.5pt;
    font-weight: 700;
}}
'''
theme = replace_once(theme, old_main_title, new_main_title, "main GroupBox native legend fill")

old_settings_title = '''QDialog#settingsDialog QGroupBox::title {{
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
new_settings_title = '''QDialog#settingsDialog QGroupBox::title {{
    subcontrol-origin: margin;
    subcontrol-position: top left;
    left: 10px;
    padding: 0 2px;
    color: {TEXT_PRIMARY};
    font-size: 11.5pt;
    font-weight: 700;
}}
'''
theme = replace_once(theme, old_settings_title, new_settings_title, "settings GroupBox native legend fill")
theme_path.write_text(theme, encoding="utf-8")


tests_path = ROOT / "tests" / "test_app.py"
tests = tests_path.read_text(encoding="utf-8")
tests = replace_once(
    tests,
    "    assert len(win.input_tab.confirm_labels) == 10\n",
    "    assert len(win.input_tab.confirm_labels) == 10\n    assert win.minimumWidth() == 1120\n    assert win.minimumHeight() == 710\n",
    "Build 3 main-window size regression assertion",
)
tests_path.write_text(tests, encoding="utf-8")


build_path = ROOT / "BUILD"
if build_path.read_text(encoding="utf-8").strip() != "2":
    raise SystemExit("BUILD must be 2 before Build 3 refinement")
build_path.write_text("3\n", encoding="utf-8")

notes_path = ROOT / "V1.2.0.txt"
notes = notes_path.read_text(encoding="utf-8")
notes = replace_once(notes, "Build: 2\n", "Build: 3\n", "Build note")
notes = replace_once(
    notes,
    "MainWindow minimum size and initial size are both 1120x740",
    "MainWindow minimum size and initial size are both 1120x710",
    "window size note",
)
notes = replace_once(
    notes,
    "Basic Information, Income, Expense and Input Confirmation use consistent white framed sections with standard GroupBox legend titles: the border naturally breaks behind the title text and the title background matches the section background.",
    "Basic Information, Income, Expense and Input Confirmation use consistent white framed sections with standard GroupBox legend titles; the title subcontrol has no explicit fill, so its surrounding background is inherited naturally, and section titles use bold 700 weight.",
    "legend note",
)
notes = replace_once(
    notes,
    "Main application window keeps the canonical ACC icon. Secondary dialogs / management / settings windows show no title-bar icon at all (including no Windows generic fallback icon), while native MessageBox and native QFileDialog keep OS-native shell behavior.",
    "Main application window keeps the canonical ACC icon. Secondary dialogs / management / settings windows override the Windows class-icon fallback with a cached fully-transparent native HICON so no visible title-bar icon remains; native MessageBox and native QFileDialog keep OS-native shell behavior.",
    "dialog icon note",
)
notes = replace_once(
    notes,
    "Settings uses compact framed GroupBox sections for database location, lock, ledger, common summary, backup/restore, Google Drive and program information; excess inter-section whitespace is removed while behavior is unchanged.",
    "Settings uses compact framed GroupBox sections for database location, lock, ledger, common summary, backup/restore, Google Drive and program information; their legends use the same no-explicit-fill, bold treatment as the main input page, and excess inter-section whitespace remains removed while behavior is unchanged.",
    "settings legend note",
)
notes_path.write_text(notes, encoding="utf-8")

print("CYAccounting V1.2.0 Build 3 visual refinement applied")
