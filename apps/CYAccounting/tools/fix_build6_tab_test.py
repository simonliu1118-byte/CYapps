from pathlib import Path

path = Path(__file__).resolve().parents[1] / "tests" / "test_app.py"
text = path.read_text(encoding="utf-8")
old = "    assert 'border-bottom: 2px solid #2563EB' not in main_module.APP_STYLE\n    assert 'border-bottom-color: #FFFFFF' in main_module.APP_STYLE\n"
new = "    assert 'QTabWidget#categoryManagerTabs QTabBar::tab {' in main_module.APP_STYLE\n    assert 'border-top-left-radius: 5px' in main_module.APP_STYLE\n    assert 'border-bottom-color: #FFFFFF' in main_module.APP_STYLE\n"
if text.count(old) != 1:
    raise RuntimeError(f"expected exactly one Build 6 tab-test block, found {text.count(old)}")
path.write_text(text.replace(old, new, 1), encoding="utf-8")
print("Build 6 tab regression test corrected")
