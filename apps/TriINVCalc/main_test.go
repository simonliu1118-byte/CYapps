//go:build windows

package main

import "testing"

func TestRoundDivHalfUp(t *testing.T) {
	cases := []struct {
		num, den, want int64
	}{
		{149, 100, 1},
		{150, 100, 2},
		{340000, 2100, 162},
		{17000, 2100, 8},
	}
	for _, tc := range cases {
		if got := roundDivHalfUp(tc.num, tc.den); got != tc.want {
			t.Fatalf("roundDivHalfUp(%d,%d)=%d, want %d", tc.num, tc.den, got, tc.want)
		}
	}
}

func TestFitDisplayUnitTwoDecimals(t *testing.T) {
	scaled, decimals, _, ok := fitDisplayUnit(17500, 10, 166700)
	if !ok || decimals != 2 || scaled != 16667 {
		t.Fatalf("got scaled=%d decimals=%d ok=%v, want 16667/2/true", scaled, decimals, ok)
	}
	if roundDivHalfUp(scaled*10, 100) != 1667 {
		t.Fatalf("display unit does not reproduce line amount")
	}
}

func TestFitDisplayUnitFallsBackToThreeDecimals(t *testing.T) {
	scaled, decimals, _, ok := fitDisplayUnit(157, 101, 15100)
	if !ok || decimals != 3 {
		t.Fatalf("expected 3-decimal fallback, got scaled=%d decimals=%d ok=%v", scaled, decimals, ok)
	}
	if roundDivHalfUp(scaled*101, 1000) != 151 {
		t.Fatalf("3-decimal display unit does not reproduce target amount")
	}
}

func TestInputRangeBoundaryMath(t *testing.T) {
	grossRaw, ok := safeMul(999, 999900)
	if !ok {
		t.Fatal("maximum supported quantity/unit price should not overflow")
	}
	grossLine := roundDivHalfUp(grossRaw, 100)
	if grossLine != 9989001 {
		t.Fatalf("max line gross=%d, want 9989001", grossLine)
	}
}
