package version

import "testing"

func TestDisplay(t *testing.T) {
	originalValue, originalBuild := Value, Build
	t.Cleanup(func() { Value, Build = originalValue, originalBuild })

	Value = "1.2.3"
	Build = "0"
	if got, want := Display(), "V1.2.3"; got != want {
		t.Fatalf("Display() = %q, want %q", got, want)
	}

	Build = "2"
	if got, want := Display(), "V1.2.3 Build 2"; got != want {
		t.Fatalf("Display() = %q, want %q", got, want)
	}
}

func TestWindowTitle(t *testing.T) {
	originalValue, originalBuild := Value, Build
	t.Cleanup(func() { Value, Build = originalValue, originalBuild })

	Value = "1.2.3"
	Build = "1"
	if got, want := WindowTitle(), "CY 電子發票 V1.2.3 Build 1"; got != want {
		t.Fatalf("WindowTitle() = %q, want %q", got, want)
	}
}
