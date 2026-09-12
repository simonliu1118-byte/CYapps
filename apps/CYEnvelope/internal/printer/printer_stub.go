//go:build !windows

package printer

import "errors"

func Names() []string                  { return nil }
func DefaultName() string              { return "" }
func Properties(uintptr, string) error { return errors.New("Windows only") }
func Print(string, Job) error          { return errors.New("Windows only") }
