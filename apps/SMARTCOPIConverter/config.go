package main

import (
	"encoding/json"
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

func statePath() (string, error) {
	exe, err := os.Executable()
	if err != nil {
		return "", err
	}
	return filepath.Join(filepath.Dir(exe), "settings.json"), nil
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
