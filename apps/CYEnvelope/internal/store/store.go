package store

import (
	"database/sql"
	"encoding/json"
	"errors"
	"fmt"
	"os"
	"path/filepath"
	"sync"

	"cyenvelope/internal/model"
	_ "modernc.org/sqlite"
)

type Store struct {
	mu sync.Mutex
	db *sql.DB
}

func Open(baseDir string) (*Store, error) {
	dataDir := filepath.Join(baseDir, "Data")
	if err := os.MkdirAll(dataDir, 0o755); err != nil {
		return nil, fmt.Errorf("create Data: %w", err)
	}
	db, err := sql.Open("sqlite", filepath.Join(dataDir, "CYEnvelope.db"))
	if err != nil {
		return nil, fmt.Errorf("open database: %w", err)
	}
	db.SetMaxOpenConns(1)
	if _, err = db.Exec(`CREATE TABLE IF NOT EXISTS app_state (
		id INTEGER PRIMARY KEY CHECK (id = 1),
		json TEXT NOT NULL,
		updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
	)`); err != nil {
		db.Close()
		return nil, fmt.Errorf("create schema: %w", err)
	}
	s := &Store{db: db}
	if _, err = s.Load(); err != nil {
		if !errors.Is(err, sql.ErrNoRows) {
			db.Close()
			return nil, err
		}
		state := model.DefaultState()
		if err = s.Save(state); err != nil {
			db.Close()
			return nil, err
		}
	}
	return s, nil
}

func (s *Store) Close() error { return s.db.Close() }

func (s *Store) Load() (model.State, error) {
	s.mu.Lock()
	defer s.mu.Unlock()
	return s.loadUnlocked()
}

func (s *Store) loadUnlocked() (model.State, error) {
	var raw string
	err := s.db.QueryRow(`SELECT json FROM app_state WHERE id = 1`).Scan(&raw)
	if err != nil {
		return model.State{}, err
	}
	var state model.State
	if err := json.Unmarshal([]byte(raw), &state); err != nil {
		return model.State{}, fmt.Errorf("decode database: %w", err)
	}
	model.Migrate(&state)
	return state, nil
}

func (s *Store) Save(state model.State) error {
	s.mu.Lock()
	defer s.mu.Unlock()
	return s.saveUnlocked(state)
}

func (s *Store) saveUnlocked(state model.State) error {
	raw, err := json.Marshal(state)
	if err != nil {
		return fmt.Errorf("encode database: %w", err)
	}
	_, err = s.db.Exec(`INSERT INTO app_state(id,json,updated_at) VALUES(1,?,CURRENT_TIMESTAMP)
		ON CONFLICT(id) DO UPDATE SET json=excluded.json, updated_at=CURRENT_TIMESTAMP`, string(raw))
	if err != nil {
		return fmt.Errorf("save database: %w", err)
	}
	return nil
}

func (s *Store) Update(fn func(*model.State) error) error {
	s.mu.Lock()
	defer s.mu.Unlock()
	state, err := s.loadUnlocked()
	if err != nil {
		return err
	}
	if err = fn(&state); err != nil {
		return err
	}
	return s.saveUnlocked(state)
}
