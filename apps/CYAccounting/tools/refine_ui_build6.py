from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
MAIN = ROOT / "app" / "main.py"
DIALOGS = ROOT / "app" / "dialogs.py"
THEME = ROOT / "app" / "theme.py"
TESTS = ROOT / "tests" / "test_app.py"
BUILD = ROOT / "BUILD"
VERSION_NOTE = ROOT / "V1.2.0.txt"


def replace_once(path: Path, old: str, new: str, label: str) -> None:
    text = path.read_text(encoding="utf-8")
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"{label}: expected exactly one guarded match in {path}, found {count}")
    path.write_text(text.replace(old, new, 1), encoding="utf-8")


# Build identity.
if BUILD.read_text(encoding="utf-8").strip() != "5":
    raise RuntimeError("BUILD is not 5 before Build 6 refinement")
BUILD.write_text("6\n", encoding="utf-8")

# Main window footer uses the repository-policy copyright wording and version source.
replace_once(
    MAIN,
    "    APP_NAME,\n    APP_VERSION,\n    MAX_AMOUNT,\n",
    "    APP_NAME,\n    APP_VERSION,\n    APP_RELEASE_DATE,\n    MAX_AMOUNT,\n",
    "main util import",
)

old_chrome = '''    @staticmethod
    def _prepare_secondary_dialog_chrome(dialog: QDialog) -> None:
        # Do not alter Qt window flags here. QDialog's native caption/system
        # menu/Close behavior is already correct; changing the hint mask can
        # disable the Windows X button. Icon suppression is handled only after
        # HWND creation by _clear_secondary_dialog_icon().
        if dialog.property("cySecondaryChromePrepared"):
            return
        dialog.setProperty("cySecondaryChromePrepared", True)
        dialog.setWindowIcon(QIcon())

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

                # WS_EX_DLGMODALFRAME is the native no-caption-icon style.
                # It does not remove WS_SYSMENU, so the Windows Close button
                # remains active. WM_SETICON(NULL) then clears any inherited
                # QApplication/window icon after the native handle exists.
                get_ex_style = getattr(user32, "GetWindowLongPtrW", user32.GetWindowLongW)
                set_ex_style = getattr(user32, "SetWindowLongPtrW", user32.SetWindowLongW)
                ex_style = get_ex_style(hwnd, GWL_EXSTYLE)
                set_ex_style(hwnd, GWL_EXSTYLE, ex_style | WS_EX_DLGMODALFRAME)
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
new_chrome = '''    @staticmethod
    def _apply_secondary_dialog_no_icon(dialog: QDialog) -> None:
        """Emulate WinForms ShowIcon=False without changing Qt caption flags.

        CYInvoice's proven WinForms dialogs use ShowIcon=False while retaining
        the native system menu and Close button. Qt Widgets has no equivalent
        property, so apply the corresponding Windows dialog-modal-frame style
        directly to the HWND. This is intentionally done both before first show
        and after show because Qt can refresh the non-client frame while showing.
        """
        if sys.platform != "win32":
            return
        try:
            import ctypes
            hwnd = int(dialog.winId())  # Force the native HWND before first show.
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
            get_ex_style = getattr(user32, "GetWindowLongPtrW", user32.GetWindowLongW)
            set_ex_style = getattr(user32, "SetWindowLongPtrW", user32.SetWindowLongW)
            ex_style = get_ex_style(hwnd, GWL_EXSTYLE)
            set_ex_style(hwnd, GWL_EXSTYLE, ex_style | WS_EX_DLGMODALFRAME)
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

    @staticmethod
    def _prepare_secondary_dialog_chrome(dialog: QDialog) -> None:
        # Never mutate Qt window flags: the default QDialog caption owns the
        # working Windows system menu and Close button.
        if dialog.property("cySecondaryChromePrepared"):
            return
        dialog.setProperty("cySecondaryChromePrepared", True)
        ChineseStandardButtonFilter._apply_secondary_dialog_no_icon(dialog)

    @staticmethod
    def _clear_secondary_dialog_icon(dialog: QDialog) -> None:
        ChineseStandardButtonFilter._apply_secondary_dialog_no_icon(dialog)
'''
replace_once(MAIN, old_chrome, new_chrome, "secondary dialog chrome")

old_page = '''        page_layout.addWidget(self.pages)
        central_layout.addWidget(page_container, 1)
        self.setCentralWidget(central)
'''
new_page = '''        page_layout.addWidget(self.pages)
        central_layout.addWidget(page_container, 1)

        footer = QLabel(
            f"{APP_NAME} {APP_VERSION}   |   "
            f"Copyright © {APP_RELEASE_DATE[:4]} C.C. Liu, Chihyuan Co. All Rights Reserved."
        )
        footer.setObjectName("appFooter")
        footer.setAlignment(Qt.AlignmentFlag.AlignRight | Qt.AlignmentFlag.AlignVCenter)
        central_layout.addWidget(footer, 0)
        self.setCentralWidget(central)
'''
replace_once(MAIN, old_page, new_page, "main footer")

# Settings: program/version information moves to the main footer. The destructive
# action remains available in its own compact, semantically clear section.
replace_once(DIALOGS, "        self.resize(760, 660)\n", "        self.resize(760, 640)\n", "settings height")
old_info = '''        info_box = QGroupBox("程式資訊")
        info_layout = QGridLayout(info_box)
        info_layout.setContentsMargins(12, 8, 12, 8)
        info_layout.setHorizontalSpacing(8)
        info_layout.setVerticalSpacing(4)
        info_layout.addWidget(QLabel("程式名稱："), 0, 0)
        info_layout.addWidget(QLabel(APP_NAME), 0, 1)
        info_layout.addWidget(QLabel("版本："), 1, 0)
        info_layout.addWidget(QLabel(APP_VERSION), 1, 1)
        clear = no_tab_button("清除所有記帳資料與期初餘額")
        clear.setObjectName("dangerButton")
        clear.clicked.connect(self.clear_data)
        info_layout.setColumnStretch(2, 1)
        info_layout.addWidget(clear, 0, 3, 2, 1, Qt.AlignmentFlag.AlignVCenter)
        outer.addWidget(info_box)
'''
new_info = '''        reset_box = QGroupBox("帳本重置")
        reset_layout = QHBoxLayout(reset_box)
        reset_layout.setContentsMargins(12, 7, 12, 7)
        reset_layout.setSpacing(8)
        reset_note = QLabel("清除本機帳本資料與期初餘額；既有備份保留。")
        reset_layout.addWidget(reset_note)
        reset_layout.addStretch(1)
        clear = no_tab_button("清除所有記帳資料與期初餘額")
        clear.setObjectName("dangerButton")
        clear.clicked.connect(self.clear_data)
        reset_layout.addWidget(clear)
        outer.addWidget(reset_box)
'''
replace_once(DIALOGS, old_info, new_info, "settings program info removal")

# APP_VERSION is no longer displayed by SettingsDialog.
replace_once(DIALOGS, "    APP_NAME,\n    APP_VERSION,\n    MAX_AMOUNT,\n", "    APP_NAME,\n    MAX_AMOUNT,\n", "dialogs util import")

# Category manager tabs: large conventional/native tab affordance, not the
# header-only underline treatment. This remains within the visual guide's
# allowed native-tab direction while making the two management modes obvious.
old_tabs = '''/* Category manager uses the canonical Header-only Custom tab direction:
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
'''
new_tabs = '''/* Category manager deliberately uses large conventional tabs rather than
   header-only navigation: these are two peer management pages and need a
   stronger, old-style tab affordance. Keep native QTabWidget behavior. */
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
    margin: 0 2px 0 0;
    border: 1px solid {NEUTRAL_BORDER};
    border-bottom-color: {NEUTRAL_BORDER};
    border-top-left-radius: 5px;
    border-top-right-radius: 5px;
    background: {NEUTRAL_WINDOW};
    color: {TEXT_SECONDARY};
    font-size: 11.5pt;
    font-weight: 600;
}}
QTabWidget#categoryManagerTabs QTabBar::tab:selected {{
    color: {TEXT_PRIMARY};
    background: {NEUTRAL_WHITE};
    border-color: {NEUTRAL_BORDER};
    border-bottom-color: {NEUTRAL_WHITE};
    font-weight: 700;
}}
QTabWidget#categoryManagerTabs QTabBar::tab:hover:!selected {{
    color: {TEXT_PRIMARY};
    background: #EEF2F6;
}}
'''
replace_once(THEME, old_tabs, new_tabs, "category manager conventional tabs")

# Low-presence footer styling.
theme_text = THEME.read_text(encoding="utf-8")
footer_style = '''
QLabel#appFooter {{
    color: {TEXT_SECONDARY};
    font-size: 9pt;
    padding: 3px 2px 0 0;
    background: transparent;
}}
'''
anchor = '''QLabel {{
    background: transparent;
}}
'''
if footer_style.strip() not in theme_text:
    if theme_text.count(anchor) != 1:
        raise RuntimeError("footer style anchor not unique")
    theme_text = theme_text.replace(anchor, anchor + footer_style, 1)
    THEME.write_text(theme_text, encoding="utf-8")

# Regression checks in tests that are actually invoked by tests/test_app.py.
test_text = TESTS.read_text(encoding="utf-8")
old_category_assert = '''    assert category_tabs.count() == 2
    assert category_tabs.tabBar().expanding() is True

    # Secondary-dialog preparation must never mutate Qt's native window flags.
'''
new_category_assert = '''    assert category_tabs.count() == 2
    assert category_tabs.tabBar().expanding() is True
    assert 'border-bottom: 2px solid #2563EB' not in main_module.APP_STYLE
    assert 'border-bottom-color: #FFFFFF' in main_module.APP_STYLE

    # Secondary-dialog preparation must never mutate Qt's native window flags.
'''
if test_text.count(old_category_assert) != 1:
    raise RuntimeError("category tab regression anchor not unique")
test_text = test_text.replace(old_category_assert, new_category_assert, 1)

old_main_assert = '''    assert win.minimumWidth() == 1120
    assert win.minimumHeight() == 710
    selected_button = win.input_tab.account_buttons[win.input_tab.selected_account]
'''
new_main_assert = '''    assert win.minimumWidth() == 1120
    assert win.minimumHeight() == 710
    footer = win.findChild(QLabel, 'appFooter')
    assert footer is not None
    assert APP_VERSION in footer.text()
    assert 'Copyright © 2026 C.C. Liu, Chihyuan Co. All Rights Reserved.' in footer.text()
    selected_button = win.input_tab.account_buttons[win.input_tab.selected_account]
'''
if test_text.count(old_main_assert) != 1:
    raise RuntimeError("main footer regression anchor not unique")
test_text = test_text.replace(old_main_assert, new_main_assert, 1)

old_settings_assert = '''    assert dlg.common_summary_min.findChild(QPushButton) is None
    # The settings page intentionally remains an administrator override that
'''
new_settings_assert = '''    assert dlg.common_summary_min.findChild(QPushButton) is None
    settings_titles = {group.title() for group in dlg.findChildren(QGroupBox)}
    assert '程式資訊' not in settings_titles
    assert '帳本重置' in settings_titles
    # The settings page intentionally remains an administrator override that
'''
if test_text.count(old_settings_assert) != 1:
    raise RuntimeError("settings section regression anchor not unique")
test_text = test_text.replace(old_settings_assert, new_settings_assert, 1)
TESTS.write_text(test_text, encoding="utf-8")

# Refresh version notes, removing stale statements from earlier unsuccessful icon strategies.
note = VERSION_NOTE.read_text(encoding="utf-8")
lines = note.splitlines()
lines = [line for line in lines if not line.startswith("Build:")]
lines.insert(1, "Build: 6")
filtered = []
for line in lines:
    if "true no-system-menu dialog chrome" in line:
        continue
    if line.startswith("- Build 5 gives Income Category / Expense Category large equal-width Header-only Custom tabs"):
        continue
    if line.startswith("- Settings uses compact framed GroupBox sections"):
        continue
    filtered.append(line)
insert_at = 5 if len(filtered) > 5 else len(filtered)
new_notes = [
    "- Build 6 moves program/version information to a low-presence main-window footer and uses the repository-policy notice: Copyright © 2026 C.C. Liu, Chihyuan Co. All Rights Reserved.; Settings keeps the destructive reset action in a dedicated Account Reset section.",
    "- Build 6 changes Income Category / Expense Category to large equal-width conventional QTabWidget tabs with clear selected-page chrome instead of the header-only underline treatment; native QTabWidget lifecycle and keyboard behavior remain intact.",
    "- Build 6 aligns secondary-dialog icon suppression with CYInvoice's proven ShowIcon=false intent: Qt caption flags are untouched, WS_EX_DLGMODALFRAME is applied to the HWND before first show and reapplied after show, and no transparent/empty icon workaround is used.",
]
filtered[insert_at:insert_at] = new_notes
VERSION_NOTE.write_text("\n".join(filtered).rstrip() + "\n", encoding="utf-8")

print("Build 6 UI refinement applied")
