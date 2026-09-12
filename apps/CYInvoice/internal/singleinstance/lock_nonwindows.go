//go:build !windows

package singleinstance

type Lock struct{}

func Acquire(string) (*Lock, bool, error) { return &Lock{}, true, nil }
func (*Lock) Close() {}

