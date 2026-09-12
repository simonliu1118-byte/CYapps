package version

import "testing"

func TestDisplay(t *testing.T) {
	original := Value
	t.Cleanup(func() { Value = original })

	Value = "1.2.3"
	if got, want := Display(), "V1.2.3"; got != want {
		t.Fatalf("Display() = %q, want %q", got, want)
	}
}

func TestWindowTitle(t *testing.T) {
	original := Value
	t.Cleanup(func() { Value = original })

	Value = "1.0.0-rebuild.1"
	if got, want := WindowTitle(), "CY 電子發票 V1.0.0-rebuild.1"; got != want {
		t.Fatalf("WindowTitle() = %q, want %q", got, want)
	}
}

