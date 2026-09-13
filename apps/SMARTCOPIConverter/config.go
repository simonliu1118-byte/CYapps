package main

import (
	"encoding/json"
	"errors"
	"os"
	"path/filepath"
)

type Settings struct {
	POSOutputDir string `json:"pos_output_dir"`
}
type HistoryItem struct {
	Date      string `json:"date"`
	OrderType string `json:"order_type"`
	OrderNo   string `json:"order_no"`
	Customer  string `json:"customer"`
}
type AppState struct {
	Settings Settings      `json:"settings"`
	History  []HistoryItem `json:"history"`
}

func appDataDir() (string, error) {
	base := os.Getenv("LOCALAPPDATA")
	if base == "" {
		return "", errors.New("找不到 LOCALAPPDATA")
	}
	dir := filepath.Join(base, "Chihyuan", "SMARTCOPIConverter")
	if err := os.MkdirAll(dir, 0o700); err != nil {
		return "", err
	}
	return dir, nil
}
func statePath() (string, error) {
	d, e := appDataDir()
	if e != nil {
		return "", e
	}
	return filepath.Join(d, "settings.json"), nil
}
func loadState() AppState {
	var st AppState
	p, e := statePath()
	if e != nil {
		return st
	}
	b, e := os.ReadFile(p)
	if e != nil {
		return st
	}
	if json.Unmarshal(b, &st) != nil {
		return AppState{}
	}
	if len(st.History) > 99 {
		st.History = st.History[:99]
	}
	return st
}
func saveState(st AppState) error {
	if len(st.History) > 99 {
		st.History = st.History[:99]
	}
	p, e := statePath()
	if e != nil {
		return e
	}
	b, e := json.MarshalIndent(st, "", "  ")
	if e != nil {
		return e
	}
	tmp := p + ".tmp"
	if e = os.WriteFile(tmp, b, 0o600); e != nil {
		return e
	}
	if e = os.Rename(tmp, p); e != nil {
		_ = os.Remove(tmp)
		return e
	}
	return nil
}
