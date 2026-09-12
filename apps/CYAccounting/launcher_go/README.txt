Windows GUI 啟動器（Go）

以 windowsgui 子系統編譯，啟動內建 runtime\pythonw.exe。
V1.0.5 不再使用 SysProcAttr.HideWindow；pythonw.exe 本身不建立主控台，
而 HideWindow 會讓 Windows 將 Qt 主視窗的第一次 ShowWindow 呼叫套用為 SW_HIDE。
