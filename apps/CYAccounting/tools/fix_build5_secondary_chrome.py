from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
main_path = ROOT / "app" / "main.py"
test_path = ROOT / "tests" / "test_app.py"
notes_path = ROOT / "V1.2.0.txt"


def replace_once(path: Path, old: str, new: str) -> None:
    text = path.read_text(encoding="utf-8")
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"{path}: expected exactly one guarded match, found {count}")
    path.write_text(text.replace(old, new, 1), encoding="utf-8")


old_prepare = '''    @staticmethod
    def _prepare_secondary_dialog_chrome(dialog: QDialog) -> None:
        # Keep the native Windows system menu: the Close button depends on it.
        # The icon is removed separately at the native non-client level after
        # the HWND exists.  Do not use CustomizeWindowHint here; doing so can
        # leave the X button disabled even when WindowCloseButtonHint is set.
        if dialog.property("cySecondaryChromePrepared"):
            return
        dialog.setProperty("cySecondaryChromePrepared", True)
        dialog.setWindowIcon(QIcon())
        try:
            flags = dialog.windowFlags()
            flags |= (
                Qt.WindowType.WindowTitleHint
                | Qt.WindowType.WindowSystemMenuHint
                | Qt.WindowType.WindowCloseButtonHint
            )
            flags &= ~Qt.WindowType.CustomizeWindowHint
            dialog.setWindowFlags(flags)
        except Exception:
            pass
'''
new_prepare = '''    @staticmethod
    def _prepare_secondary_dialog_chrome(dialog: QDialog) -> None:
        # Do not alter Qt window flags here. QDialog's native caption/system
        # menu/Close behavior is already correct; changing the hint mask can
        # disable the Windows X button. Icon suppression is handled only after
        # HWND creation by _clear_secondary_dialog_icon().
        if dialog.property("cySecondaryChromePrepared"):
            return
        dialog.setProperty("cySecondaryChromePrepared", True)
        dialog.setWindowIcon(QIcon())
'''
replace_once(main_path, old_prepare, new_prepare)

old_test = '''    # A secondary dialog must keep the native system menu because Windows uses
    # it to back the caption Close button. Build 4 accidentally removed it.
    main_module.ChineseStandardButtonFilter._prepare_secondary_dialog_chrome(dlg)
    chrome_flags = dlg.windowFlags()
    assert bool(chrome_flags & Qt.WindowType.WindowSystemMenuHint)
    assert bool(chrome_flags & Qt.WindowType.WindowCloseButtonHint)
    assert not bool(chrome_flags & Qt.WindowType.CustomizeWindowHint)
'''
new_test = '''    # Secondary-dialog preparation must never mutate Qt's native window flags.
    # The default QDialog caption owns the system menu and working Close button;
    # icon suppression is a native non-client operation performed after HWND creation.
    chrome_flags_before = dlg.windowFlags()
    main_module.ChineseStandardButtonFilter._prepare_secondary_dialog_chrome(dlg)
    assert dlg.windowFlags() == chrome_flags_before
'''
replace_once(test_path, old_test, new_test)

notes = notes_path.read_text(encoding="utf-8")
old_note = "- Build 5 restores the native secondary-dialog system menu and Close button while keeping the Windows no-icon treatment; no secondary dialog may disable the title-bar X as a side effect of icon suppression.\n"
new_note = "- Build 5 preserves each QDialog's default native window flags and working Close button while applying icon suppression only at the Windows non-client HWND layer; secondary-dialog icon removal must never change or disable title-bar behavior.\n"
if notes.count(old_note) != 1:
    raise RuntimeError("Build 5 chrome note guard failed")
notes_path.write_text(notes.replace(old_note, new_note, 1), encoding="utf-8")

print("Build 5 secondary chrome fix applied")
