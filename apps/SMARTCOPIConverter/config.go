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
	dir, err := appDataDir()
	if err != nil {
		return "", err
	}
	return filepath.Join(dir, "settings.json"), nil
}

func loadState() AppState {
	var st AppState
	p, err := statePath()
	if err != nil {
		return st
	}
	b, err := os.ReadFile(p)
	if err != nil {
		return st
	}
	_ = json.Unmarshal(b, &st)
	if len(st.History) > 99 {
		st.History = st.History[:99]
	}
	return st
}

func saveState(st AppState) error {
	if len(st.History) > 99 {
		st.History = st.History[:99]
	}
	p, err := statePath()
	if err != nil {
		return err
	}
	b, err := json.MarshalIndent(st, "", "  ")
	if err != nil {
		return err
	}
	return os.WriteFile(p, b, 0o600)
}

func logPath() (string, error) {
	dir, err := appDataDir()
	if err != nil {
		return "", err
	}
	return filepath.Join(dir, "SMART_COPI_Converter.log"), nil
}
