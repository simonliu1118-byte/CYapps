package fixeddecimal

import "testing"

func TestInclusiveUntaxedRoundTripAtSevenPlaces(t *testing.T) {
	inclusive, err := Parse("200")
	if err != nil {
		t.Fatal(err)
	}
	untaxed, err := MultiplyRatio(inclusive, 20, 21)
	if err != nil {
		t.Fatal(err)
	}
	if got := Format(untaxed); got != "190.4761905" {
		t.Fatalf("untaxed = %s", got)
	}
	back, err := MultiplyRatio(untaxed, 21, 20)
	if err != nil {
		t.Fatal(err)
	}
	if got := Format(back); got != "200" {
		t.Fatalf("inclusive again = %s", got)
	}
}

func TestParseRejectsMoreThanSevenPlaces(t *testing.T) {
	if _, err := Parse("1.12345678"); err == nil {
		t.Fatal("accepted more than seven decimal places")
	}
}

func TestMultiplyRoundsAtSevenPlaces(t *testing.T) {
	quantity, _ := Parse("3")
	price, _ := Parse("33.3333333")
	amount, err := Multiply(quantity, price)
	if err != nil {
		t.Fatal(err)
	}
	if got := Format(amount); got != "99.9999999" {
		t.Fatalf("amount = %s", got)
	}
	if got := RoundInt64(amount); got != 100 {
		t.Fatalf("rounded amount = %d", got)
	}
}

func TestDivideKeepsSevenPlacesWithoutFloat(t *testing.T) {
	amount, _ := Parse("100")
	quantity, _ := Parse("3")
	unitPrice, err := Divide(amount, quantity)
	if err != nil { t.Fatal(err) }
	if got := Format(unitPrice); got != "33.3333333" {
		t.Fatalf("unit price=%s", got)
	}
	if _, err := Divide(amount, 0); err == nil {
		t.Fatal("division by zero was accepted")
	}
}
