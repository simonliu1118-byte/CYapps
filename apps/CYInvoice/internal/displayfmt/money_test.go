package displayfmt

import "testing"

func TestIntegerAddsThousandsSeparators(t *testing.T) {
	for value, want := range map[int64]string{
		0: "0", 148: "148", 3100: "3,100", 1234567890: "1,234,567,890", -3100: "-3,100",
	} {
		if got := Integer(value); got != want { t.Fatalf("Integer(%d) = %q, want %q", value, got, want) }
	}
}

func TestDecimalPreservesExactFraction(t *testing.T) {
	for value, want := range map[string]string{
		"200": "200", "12345.67": "12,345.67", "190.4761905": "190.4761905",
		"-12345.6700000": "-12,345.6700000", "not-a-number": "not-a-number",
	} {
		if got := Decimal(value); got != want { t.Fatalf("Decimal(%q) = %q, want %q", value, got, want) }
	}
}
