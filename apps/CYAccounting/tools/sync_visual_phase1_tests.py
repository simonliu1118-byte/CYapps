from pathlib import Path

path = Path(__file__).resolve().parents[1] / "tests" / "test_app.py"
text = path.read_text(encoding="utf-8")
replacements = {
    "assert '#98a2b3' in win.input_tab.confirm_lines[0]": "assert '#98A2B3' in win.input_tab.confirm_lines[0]",
    "assert 'background-color:#e7efe9' in win.input_tab.confirm_lines[0]": "assert 'background-color:#EDF5F2' in win.input_tab.confirm_lines[0]",
}
for old, new in replacements.items():
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"Expected exactly one visual assertion to update: {old!r}; found {count}")
    text = text.replace(old, new, 1)
path.write_text(text, encoding="utf-8", newline="\n")
print("CYAccounting Phase 1 visual assertions synchronized")
