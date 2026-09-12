//go:build windows

package main

import "strings"

func containsFold(value, query string) bool {
	return strings.Contains(strings.ToLower(value), strings.ToLower(strings.TrimSpace(query)))
}
