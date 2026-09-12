package phone

import (
	"fmt"
	"regexp"
	"strings"
	"unicode"
)

var extensionPattern = regexp.MustCompile(`(?i)(?:#|分機|ext\.?)[\s：:]*(\d+)\s*$`)

type Parsed struct {
	Number    string
	Extension string
}

func Parse(input string) (Parsed, error) {
	value := strings.TrimSpace(input)
	if value == "" {
		return Parsed{}, nil
	}
	ext := ""
	if match := extensionPattern.FindStringSubmatch(value); len(match) == 2 {
		ext = match[1]
		value = strings.TrimSpace(value[:strings.Index(value, match[0])])
	}
	digits := onlyDigits(value)
	if strings.HasPrefix(strings.ReplaceAll(value, " ", ""), "+886") {
		intl := onlyDigits(strings.TrimPrefix(strings.ReplaceAll(value, " ", ""), "+"))
		if strings.HasPrefix(intl, "886") {
			digits = "0" + strings.TrimPrefix(intl, "886")
		}
	}
	formatted, ok := formatDigits(digits)
	if !ok {
		return Parsed{}, fmt.Errorf("無法辨識臺灣電話號碼：%s", input)
	}
	return Parsed{Number: formatted, Extension: ext}, nil
}

func onlyDigits(value string) string {
	var b strings.Builder
	for _, r := range value {
		if unicode.IsDigit(r) {
			b.WriteRune(r)
		}
	}
	return b.String()
}

func formatDigits(digits string) (string, bool) {
	if len(digits) == 10 && strings.HasPrefix(digits, "09") {
		return digits[:4] + "-" + digits[4:7] + "-" + digits[7:], true
	}
	if len(digits) == 10 && (strings.HasPrefix(digits, "0800") || strings.HasPrefix(digits, "0809")) {
		return digits[:4] + "-" + digits[4:7] + "-" + digits[7:], true
	}
	type rule struct {
		Prefix     string
		Subscriber int
		Split      int
	}
	rules := []rule{
		{"0826", 5, 2}, {"0836", 5, 2},
		{"037", 7, 3}, {"049", 7, 3}, {"089", 6, 3}, {"082", 6, 3},
		{"02", 8, 4}, {"04", 8, 4},
		{"03", 7, 3}, {"05", 7, 3}, {"06", 7, 3}, {"07", 7, 3}, {"08", 7, 3},
	}
	for _, r := range rules {
		if !strings.HasPrefix(digits, r.Prefix) {
			continue
		}
		rest := digits[len(r.Prefix):]
		if len(rest) != r.Subscriber {
			continue
		}
		return "(" + r.Prefix + ") " + rest[:r.Split] + "-" + rest[r.Split:], true
	}
	return "", false
}
