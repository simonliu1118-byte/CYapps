from __future__ import annotations

from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def replace_once(path: Path, old: str, new: str, label: str) -> None:
    text = path.read_text(encoding="utf-8")
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"{label}: expected exactly one match, found {count}")
    path.write_text(text.replace(old, new, 1), encoding="utf-8")


def append_once(path: Path, marker: str, block: str, label: str) -> None:
    text = path.read_text(encoding="utf-8")
    if marker in text:
        raise SystemExit(f"{label}: marker already exists")
    path.write_text(text.rstrip() + "\n\n" + block.rstrip() + "\n", encoding="utf-8")


main_path = ROOT / "app" / "main.py"
theme_path = ROOT / "app" / "theme.py"
test_path = ROOT / "tests" / "test_app.py"
build_path = ROOT / "BUILD"
notes_path = ROOT / "V1.2.0.txt"

old_chrome = '''    @staticmethod
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

    def eventFilter(self, watched, event):  # noqa: N802
        if event.type() in (QEvent.Type.Polish, QEvent.Type.Show):
            if isinstance(watched, QDialogButtonBox):
                self._localize_dialog_box(watched)
            elif isinstance(watched, QMessageBox):
                QTimer.singleShot(0, lambda box=watched: self._localize_message_box(box))
            elif isinstance(watched, QDialog) and not isinstance(watched, QFileDialog):
                QTimer.singleShot(0, lambda dlg=watched: self._clear_secondary_dialog_icon(dlg))
        return False
'''
new_chrome = '''    @staticmethod
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

    @staticmethod
    def _clear_secondary_dialog_icon(dialog: QDialog) -> None:
        # Windows can still inherit a class icon after Qt creates the HWND.
        # WS_EX_DLGMODALFRAME + WM_SETICON(NULL) suppresses that fallback and,
        # unlike the old transparent-icon workaround, does not reserve an icon slot.
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

    def eventFilter(self, watched, event):  # noqa: N802
        if event.type() in (QEvent.Type.Polish, QEvent.Type.Show):
            if isinstance(watched, QDialogButtonBox):
                self._localize_dialog_box(watched)
            elif isinstance(watched, QMessageBox):
                QTimer.singleShot(0, lambda box=watched: self._localize_message_box(box))
            elif isinstance(watched, QDialog) and not isinstance(watched, QFileDialog):
                if event.type() == QEvent.Type.Polish:
                    self._prepare_secondary_dialog_chrome(watched)
                QTimer.singleShot(0, lambda dlg=watched: self._clear_secondary_dialog_icon(dlg))
        return False
'''
replace_once(main_path, old_chrome, new_chrome, "secondary dialog chrome")

old_main_groups = '''QGroupBox#confirmationCard, QGroupBox#inputSection, QGroupBox#incomeSection, QGroupBox#expenseSection {{
    border: 1px solid {NEUTRAL_BORDER};
    border-radius: 6px;
    margin-top: 10px;
    padding: 0;
    background: {NEUTRAL_WHITE};
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
'''
new_main_groups = '''QGroupBox#confirmationCard, QGroupBox#inputSection, QGroupBox#incomeSection, QGroupBox#expenseSection {{
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
'''
replace_once(theme_path, old_main_groups, new_main_groups, "main/settings groupbox style")

old_import = '''# Import is a genuinely multi-unit workflow, so Carded sections remain appropriate.
IMPORT_DIALOG_STYLE = f"""
QDialog#importDialog QGroupBox {{
    border: 1px solid {NEUTRAL_BORDER};
    border-radius: 6px;
    margin-top: 16px;
    padding: 14px 10px 10px 10px;
    background: {NEUTRAL_WHITE};
}}
QDialog#importDialog QGroupBox::title {{
    left: 10px;
    padding: 0 5px;
    background: {NEUTRAL_WHITE};
}}
"""
'''
new_import = '''# Import is a genuinely multi-unit workflow, but its section chrome must use
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
'''
replace_once(theme_path, old_import, new_import, "import groupbox style")

# Strengthen the existing UI regression test imports and add an effective-font test.
replace_once(
    test_path,
    "from PySide6.QtWidgets import QApplication, QPushButton\n",
    "from PySide6.QtWidgets import QApplication, QLabel, QGroupBox, QPushButton\n",
    "test QtWidgets imports",
)

font_test = r'''
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
'''
# Qt is already imported elsewhere only indirectly; add an explicit Qt import for this test.
replace_once(
    test_path,
    "from PySide6.QtGui import QColor, QPalette\n",
    "from PySide6.QtGui import QColor, QPalette\nfrom PySide6.QtCore import Qt\n",
    "test QtCore import",
)
append_once(test_path, "def test_phase1_section_group_titles_are_effectively_bold():", font_test, "effective group font test")

build_text = build_path.read_text(encoding="utf-8").strip()
if build_text != "3":
    raise SystemExit(f"BUILD guard: expected 3, found {build_text!r}")
build_path.write_text("4\n", encoding="utf-8")

notes = notes_path.read_text(encoding="utf-8")
if "Build: 3" not in notes:
    raise SystemExit("notes guard: Build 3 marker missing")
notes = notes.replace("Build: 3", "Build: 4", 1)
insert_after = "- Basic Information, Income, Expense and Input Confirmation use consistent white framed sections with standard GroupBox legend titles; the title subcontrol has no explicit fill, so its surrounding background is inherited naturally, and section titles use bold 700 weight.\n"
addition = (
    "- Build 4 corrects the Windows-effective legend weight: the section QGroupBox itself now carries Bold while descendant controls are explicitly reset to Regular, covering Basic Information / Income / Expense / Input Confirmation and every Settings section.\n"
    "- Import Source / Column Mapping / Import Preview now use the same no-explicit-title-fill, bold legend treatment as the main input page instead of the older import-only white title patch.\n"
)
if insert_after not in notes:
    raise SystemExit("notes guard: legend bullet missing")
notes = notes.replace(insert_after, insert_after + addition, 1)
old_icon_note = "- Main application window keeps the canonical ACC icon. Secondary dialogs / management / settings windows override the Windows class-icon fallback with a cached fully-transparent native HICON so no visible title-bar icon remains; native MessageBox and native QFileDialog keep OS-native shell behavior.\n"
new_icon_note = "- Main application window keeps the canonical ACC icon. Secondary dialogs / management / settings windows now use true no-system-menu dialog chrome plus WM_SETICON(NULL) / dialog-modal-frame cleanup on Windows, so the icon slot itself is removed and title text aligns left; native MessageBox and native QFileDialog keep OS-native shell behavior.\n"
if old_icon_note not in notes:
    raise SystemExit("notes guard: old icon note missing")
notes = notes.replace(old_icon_note, new_icon_note, 1)
notes_path.write_text(notes, encoding="utf-8")

print("Build 4 legend/chrome refinement applied")
