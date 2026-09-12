package phone

import "testing"

func TestParseTaiwanNumbers(t *testing.T) {
	tests := map[string]string{
		"02-23810435":      "(02) 2381-0435",
		"03 431 3366":      "(03) 431-3366",
		"0377703000":       "(037) 770-3000",
		"0492902233":       "(049) 290-2233",
		"089351376":        "(089) 351-376",
		"0912345678":       "0912-345-678",
		"+886 2 2381 0435": "(02) 2381-0435",
	}
	for input, want := range tests {
		got, err := Parse(input)
		if err != nil {
			t.Fatalf("Parse(%q): %v", input, err)
		}
		if got.Number != want {
			t.Fatalf("Parse(%q)=%q want %q", input, got.Number, want)
		}
	}
}

func TestParseExtension(t *testing.T) {
	got, err := Parse("(02) 2381-0435 分機123")
	if err != nil {
		t.Fatal(err)
	}
	if got.Number != "(02) 2381-0435" || got.Extension != "123" {
		t.Fatalf("unexpected: %#v", got)
	}
}
