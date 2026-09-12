//go:build windows

package main

import (
	"errors"
	"os"
	"path/filepath"
	"testing"
	"time"
)

func waitChannels() (chan error, chan time.Time, chan time.Time) {
	return make(chan error, 1), make(chan time.Time, 1), make(chan time.Time, 1)
}

func TestWaitForStartupDetectsReadyFlag(t *testing.T) {
	ready := filepath.Join(t.TempDir(), "startup_ready.flag")
	if err := os.WriteFile(ready, []byte("ready"), 0644); err != nil {
		t.Fatal(err)
	}
	done, poll, timeout := waitChannels()
	poll <- time.Now()
	killed := false
	result := waitForStartup(ready, done, poll, timeout, func() int { return -1 }, func() error { killed = true; return nil })
	if !result.ready || result.timedOut || killed {
		t.Fatalf("unexpected result: %+v killed=%v", result, killed)
	}
}

func TestWaitForStartupReportsEarlyExit(t *testing.T) {
	done, poll, timeout := waitChannels()
	done <- errors.New("failed")
	result := waitForStartup(filepath.Join(t.TempDir(), "missing"), done, poll, timeout, func() int { return 7 }, func() error { return nil })
	if result.ready || result.timedOut || result.exitCode != 7 || result.runErr == nil {
		t.Fatalf("unexpected result: %+v", result)
	}
}

func TestWaitForStartupKillsOnTimeout(t *testing.T) {
	done, poll, timeout := waitChannels()
	timeout <- time.Now()
	killed := false
	result := waitForStartup(filepath.Join(t.TempDir(), "missing"), done, poll, timeout, func() int { return -1 }, func() error { killed = true; return nil })
	if !result.timedOut || !killed || result.ready {
		t.Fatalf("unexpected result: %+v killed=%v", result, killed)
	}
}
