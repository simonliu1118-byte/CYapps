package applog

import (
	"os"
	"path/filepath"
	"strings"
	"testing"
)

func TestLoggerCreatesSeparateErrorLog(t *testing.T) {
	baseDir := t.TempDir()
	logger, err := New(baseDir)
	if err != nil {
		t.Fatalf("New() error: %v", err)
	}

	logger.Infof("startup")
	logger.Errorf("test error %d", 7)
	if err := logger.Close(); err != nil {
		t.Fatalf("Close() error: %v", err)
	}

	appLogs, err := filepath.Glob(filepath.Join(baseDir, "Logs", "CYInvoice_*.log"))
	if err != nil || len(appLogs) != 1 {
		t.Fatalf("application logs = %v, error = %v", appLogs, err)
	}
	errorLogs, err := filepath.Glob(filepath.Join(baseDir, "Logs", "ERROR_*.log"))
	if err != nil || len(errorLogs) != 1 {
		t.Fatalf("error logs = %v, error = %v", errorLogs, err)
	}

	content, err := os.ReadFile(errorLogs[0])
	if err != nil {
		t.Fatalf("ReadFile() error: %v", err)
	}
	if !strings.Contains(string(content), "test error 7") {
		t.Fatalf("error log missing message: %q", content)
	}
}

