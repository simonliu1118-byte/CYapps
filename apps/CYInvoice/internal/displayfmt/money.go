package displayfmt

import (
	"strconv"
	"strings"
)

// Integer formats a whole-number amount for UI display without changing the
// value used by invoice calculations, storage or API payloads.
func Integer(value int64) string { return Decimal(strconv.FormatInt(value, 10)) }

// Decimal adds thousands separators to the integer portion while preserving the
// exact decimal digits. Non-numeric input is returned trimmed and unchanged.
func Decimal(value string) string {
	value = strings.TrimSpace(value)
	if value == "" { return "" }
	sign := ""
	unsigned := value
	if unsigned[0] == '-' || unsigned[0] == '+' {
		sign, unsigned = unsigned[:1], unsigned[1:]
	}
	parts := strings.SplitN(unsigned, ".", 2)
	if parts[0] == "" || !digitsOnly(parts[0]) {
		return value
	}
	if len(parts) == 2 && (parts[1] == "" || !digitsOnly(parts[1])) {
		return value
	}
	integer := parts[0]
	for index := len(integer) - 3; index > 0; index -= 3 {
		integer = integer[:index] + "," + integer[index:]
	}
	if len(parts) == 2 {
		return sign + integer + "." + parts[1]
	}
	return sign + integer
}

func digitsOnly(value string) bool {
	for _, digit := range value {
		if digit < '0' || digit > '9' { return false }
	}
	return true
}
