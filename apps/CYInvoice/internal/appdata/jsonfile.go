package appdata

import (
	"bytes"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"os"
	"path/filepath"
)

const maxJSONFileSize = 64 << 20

func readJSON[T any](path string, destination *T) (bool, error) {
	file, err := os.Open(path)
	if errors.Is(err, os.ErrNotExist) {
		return false, nil
	}
	if err != nil {
		return false, fmt.Errorf("open %s: %w", filepath.Base(path), err)
	}
	defer file.Close()

	decoder := json.NewDecoder(io.LimitReader(file, maxJSONFileSize+1))
	if err := decoder.Decode(destination); err != nil {
		return false, fmt.Errorf("decode %s: %w", filepath.Base(path), err)
	}
	if err := ensureJSONEOF(decoder); err != nil {
		return false, fmt.Errorf("decode %s: %w", filepath.Base(path), err)
	}
	return true, nil
}

func ensureJSONEOF(decoder *json.Decoder) error {
	var extra any
	if err := decoder.Decode(&extra); errors.Is(err, io.EOF) {
		return nil
	} else if err != nil {
		return err
	}
	return errors.New("multiple JSON values")
}

func writeJSON(path string, value any) error {
	data, err := json.MarshalIndent(value, "", "  ")
	if err != nil {
		return fmt.Errorf("encode %s: %w", filepath.Base(path), err)
	}
	data = append(data, '\n')

	dir := filepath.Dir(path)
	if err := os.MkdirAll(dir, 0o700); err != nil {
		return fmt.Errorf("create Data directory: %w", err)
	}
	temporary, err := os.CreateTemp(dir, "."+filepath.Base(path)+"-*.tmp")
	if err != nil {
		return fmt.Errorf("create temporary %s: %w", filepath.Base(path), err)
	}
	temporaryPath := temporary.Name()
	defer os.Remove(temporaryPath)

	if err := temporary.Chmod(0o600); err != nil {
		temporary.Close()
		return fmt.Errorf("secure temporary %s: %w", filepath.Base(path), err)
	}
	if _, err := io.Copy(temporary, bytes.NewReader(data)); err != nil {
		temporary.Close()
		return fmt.Errorf("write temporary %s: %w", filepath.Base(path), err)
	}
	if err := temporary.Sync(); err != nil {
		temporary.Close()
		return fmt.Errorf("flush temporary %s: %w", filepath.Base(path), err)
	}
	if err := temporary.Close(); err != nil {
		return fmt.Errorf("close temporary %s: %w", filepath.Base(path), err)
	}
	if err := replaceFile(temporaryPath, path); err != nil {
		return fmt.Errorf("replace %s: %w", filepath.Base(path), err)
	}
	return nil
}
