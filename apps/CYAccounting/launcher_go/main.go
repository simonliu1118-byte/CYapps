//go:build windows

package main

import (
	"fmt"
	"os"
	"os/exec"
	"path/filepath"
	"syscall"
	"time"
	"unsafe"
)

const (
	appTitle       = "志遠記帳系統"
	startupTimeout = 30 * time.Second
)

var (
	user32          = syscall.NewLazyDLL("user32.dll")
	procMessageBoxW = user32.NewProc("MessageBoxW")
)

func messageBox(title, text string, flags uintptr) {
	titlePtr, _ := syscall.UTF16PtrFromString(title)
	textPtr, _ := syscall.UTF16PtrFromString(text)
	procMessageBoxW.Call(0, uintptr(unsafe.Pointer(textPtr)), uintptr(unsafe.Pointer(titlePtr)), flags)
}

func appendLog(path, text string) {
	f, err := os.OpenFile(path, os.O_CREATE|os.O_WRONLY|os.O_APPEND, 0644)
	if err != nil {
		return
	}
	defer f.Close()
	_, _ = fmt.Fprintf(f, "[%s] %s\r\n", time.Now().Format("2006-01-02T15:04:05.000"), text)
	_ = f.Sync()
}

type startupWaitResult struct {
	ready     bool
	timedOut  bool
	exitCode  int
	runErr    error
}

func waitForStartup(
	readyPath string,
	done <-chan error,
	poll <-chan time.Time,
	timeout <-chan time.Time,
	exitCode func() int,
	kill func() error,
) startupWaitResult {
	for {
		select {
		case runErr := <-done:
			code := exitCode()
			if _, readyErr := os.Stat(readyPath); readyErr == nil {
				return startupWaitResult{ready: true, exitCode: code, runErr: runErr}
			}
			return startupWaitResult{exitCode: code, runErr: runErr}
		case <-poll:
			if _, err := os.Stat(readyPath); err == nil {
				return startupWaitResult{ready: true}
			}
		case <-timeout:
			_ = kill()
			return startupWaitResult{timedOut: true}
		}
	}
}

func main() {
	exePath, err := os.Executable()
	if err != nil {
		messageBox(appTitle, "無法取得程式路徑。", 0x10)
		return
	}
	root := filepath.Dir(exePath)
	pythonExe := filepath.Join(root, "runtime", "pythonw.exe")
	script := filepath.Join(root, "app", "main.py")
	dataDir := filepath.Join(root, "Data")
	logPath := filepath.Join(dataDir, "startup_trace.log")
	readyPath := filepath.Join(dataDir, "startup_ready.flag")

	_ = os.MkdirAll(dataDir, 0755)
	_ = os.Remove(readyPath)
	header := fmt.Sprintf("[%s] CY Accounting launcher started\r\n[%s] root=%s\r\n",
		time.Now().Format("2006-01-02T15:04:05.000"),
		time.Now().Format("2006-01-02T15:04:05.000"), root)
	_ = os.WriteFile(logPath, []byte(header), 0644)

	if _, err := os.Stat(pythonExe); err != nil {
		appendLog(logPath, "ERROR: runtime\\pythonw.exe not found")
		messageBox(appTitle, "找不到內建執行環境。\n\n請先完整解壓縮 ZIP，再從解壓後的資料夾執行程式。", 0x10)
		return
	}
	if _, err := os.Stat(script); err != nil {
		appendLog(logPath, "ERROR: app\\main.py not found")
		messageBox(appTitle, "找不到主程式檔案。\n\n請重新下載並完整解壓縮套件。", 0x10)
		return
	}

	logFile, logErr := os.OpenFile(logPath, os.O_CREATE|os.O_WRONLY|os.O_APPEND, 0644)
	if logErr != nil {
		messageBox(appTitle, "無法建立啟動紀錄檔。\n\n請確認程式資料夾具有寫入權限。", 0x10)
		return
	}

	cmd := exec.Command(pythonExe, script)
	cmd.Dir = root
	cmd.Env = append(os.Environ(),
		"PYTHONUTF8=1",
		"PYTHONDONTWRITEBYTECODE=1",
		"QT_AUTO_SCREEN_SCALE_FACTOR=1",
		"CY_STARTUP_TRACE_INITIALIZED=1",
	)
	// pythonw.exe 本身不會建立主控台；不可設定 HideWindow，否則 Windows 會把 Qt 的第一個主視窗以 SW_HIDE 啟動。
	appendLog(logPath, "starting Python without SW_HIDE so the Qt window can be shown")
	cmd.Stdout = logFile
	cmd.Stderr = logFile

	appendLog(logPath, "starting embedded Python process")
	if err := cmd.Start(); err != nil {
		_ = logFile.Close()
		appendLog(logPath, fmt.Sprintf("ERROR: process start failed: %v", err))
		messageBox(appTitle, fmt.Sprintf("程式無法啟動。\n\n啟動紀錄：\n%s", logPath), 0x10)
		return
	}
	appendLog(logPath, fmt.Sprintf("Python process started, PID=%d", cmd.Process.Pid))

	done := make(chan error, 1)
	go func() { done <- cmd.Wait() }()
	ticker := time.NewTicker(200 * time.Millisecond)
	timeout := time.NewTimer(startupTimeout)
	defer ticker.Stop()
	defer timeout.Stop()

	result := waitForStartup(
		readyPath, done, ticker.C, timeout.C,
		func() int {
			if cmd.ProcessState != nil {
				return cmd.ProcessState.ExitCode()
			}
			return -1
		},
		cmd.Process.Kill,
	)
	_ = logFile.Close()
	if result.ready {
		if result.runErr != nil || result.exitCode != 0 {
			appendLog(logPath, fmt.Sprintf("Python exited after ready flag, code=%d; launcher exiting normally", result.exitCode))
		} else {
			appendLog(logPath, "startup ready flag detected; launcher exiting normally")
		}
		return
	}
	if result.timedOut {
		appendLog(logPath, "ERROR: startup timed out after 30 seconds; terminating Python process")
		messageBox(appTitle,
			fmt.Sprintf("程式啟動超過30秒仍未完成，已停止啟動程序。\n\n啟動紀錄：\n%s\n\n請將 startup_trace.log 提供給我。", logPath),
			0x10)
		return
	}
	appendLog(logPath, fmt.Sprintf("Python exited before startup completed, code=%d, error=%v", result.exitCode, result.runErr))
	messageBox(appTitle,
		fmt.Sprintf("程式啟動失敗（代碼 %d）。\n\n啟動紀錄：\n%s\n\n請將 startup_trace.log 提供給我。", result.exitCode, logPath),
		0x10)
}
