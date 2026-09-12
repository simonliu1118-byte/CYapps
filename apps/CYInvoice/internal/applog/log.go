package applog

import (
	"fmt"
	"os"
	"path/filepath"
	"sync"
	"time"
)

type Logger struct {
	mu        sync.Mutex
	appFile   *os.File
	errorFile *os.File
}

func New(baseDir string) (*Logger, error) {
	logsDir := filepath.Join(baseDir, "Logs")
	if err := os.MkdirAll(logsDir, 0o755); err != nil {
		return nil, fmt.Errorf("create Logs directory: %w", err)
	}

	date := time.Now().Format("20060102")
	appFile, err := os.OpenFile(
		filepath.Join(logsDir, "CYInvoice_"+date+".log"),
		os.O_CREATE|os.O_APPEND|os.O_WRONLY,
		0o600,
	)
	if err != nil {
		return nil, fmt.Errorf("open application log: %w", err)
	}

	return &Logger{appFile: appFile}, nil
}

func (l *Logger) Infof(format string, args ...any) {
	if l == nil {
		return
	}
	l.mu.Lock()
	defer l.mu.Unlock()
	l.writeLine(l.appFile, "INFO", format, args...)
}

func (l *Logger) Errorf(format string, args ...any) {
	if l == nil {
		return
	}
	l.mu.Lock()
	defer l.mu.Unlock()

	if l.errorFile == nil && l.appFile != nil {
		logsDir := filepath.Dir(l.appFile.Name())
		date := time.Now().Format("20060102")
		file, err := os.OpenFile(
			filepath.Join(logsDir, "ERROR_"+date+".log"),
			os.O_CREATE|os.O_APPEND|os.O_WRONLY,
			0o600,
		)
		if err == nil {
			l.errorFile = file
		}
	}

	l.writeLine(l.appFile, "ERROR", format, args...)
	l.writeLine(l.errorFile, "ERROR", format, args...)
}

func (l *Logger) Close() error {
	if l == nil {
		return nil
	}
	l.mu.Lock()
	defer l.mu.Unlock()

	var firstErr error
	if l.errorFile != nil {
		if err := l.errorFile.Close(); err != nil {
			firstErr = err
		}
		l.errorFile = nil
	}
	if l.appFile != nil {
		if err := l.appFile.Close(); err != nil && firstErr == nil {
			firstErr = err
		}
		l.appFile = nil
	}
	return firstErr
}

func (l *Logger) writeLine(file *os.File, level, format string, args ...any) {
	if file == nil {
		return
	}
	message := fmt.Sprintf(format, args...)
	_, _ = fmt.Fprintf(file, "%s [%s] %s\n", time.Now().Format("2006/01/02 15:04:05.000"), level, message)
}

