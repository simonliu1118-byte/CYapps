package main

import (
	"fmt"
	"log"
	"os"
	"path/filepath"
	"sync"
	"time"
)

var logMu sync.Mutex
var logFile *os.File
var logPathCurrent string

func initStartupLog() {
	exe, err := os.Executable()
	base := "."
	if err == nil {
		base = filepath.Dir(exe)
	}
	dir := filepath.Join(base, "log")
	if err := os.MkdirAll(dir, 0o755); err != nil {
		return
	}
	name := fmt.Sprintf("app(%s).log", time.Now().Format("20060102-150405"))
	f, err := os.OpenFile(filepath.Join(dir, name), os.O_CREATE|os.O_WRONLY|os.O_APPEND, 0o644)
	if err != nil {
		return
	}
	logFile = f
	logPathCurrent = f.Name()
	log.SetOutput(f)
	log.SetFlags(log.Ldate | log.Ltime | log.Lmicroseconds)
	diag("logger ready path=%q", logPathCurrent)
}

func closeStartupLog() {
	diag("STOP")
	if logFile != nil {
		_ = logFile.Close()
		logFile = nil
	}
}

func diag(format string, args ...any) {
	logMu.Lock()
	defer logMu.Unlock()
	if logFile != nil {
		log.Printf(format, args...)
	}
}
