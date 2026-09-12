package fixeddecimal

import (
	"errors"
	"math/big"
	"strings"
)

const Scale int64 = 10_000_000

type Value int64

func Parse(text string) (Value, error) {
	text = strings.TrimSpace(strings.ReplaceAll(text, ",", ""))
	if text == "" {
		return 0, errors.New("數值不可空白")
	}
	sign := int64(1)
	if text[0] == '-' || text[0] == '+' {
		if text[0] == '-' {
			sign = -1
		}
		text = text[1:]
	}
	if text == "" {
		return 0, errors.New("數值格式錯誤")
	}
	parts := strings.Split(text, ".")
	if len(parts) > 2 || (parts[0] == "" && (len(parts) == 1 || parts[1] == "")) {
		return 0, errors.New("數值格式錯誤")
	}
	whole := parts[0]
	if whole == "" {
		whole = "0"
	}
	if !digitsOnly(whole) {
		return 0, errors.New("數值格式錯誤")
	}
	fraction := ""
	if len(parts) == 2 {
		fraction = parts[1]
		if fraction == "" || len(fraction) > 7 || !digitsOnly(fraction) {
			return 0, errors.New("最多只能輸入小數點後 7 位")
		}
	}
	for len(fraction) < 7 {
		fraction += "0"
	}
	combined := strings.TrimLeft(whole+fraction, "0")
	if combined == "" {
		return 0, nil
	}
	integer := new(big.Int)
	if _, ok := integer.SetString(combined, 10); !ok {
		return 0, errors.New("數值格式錯誤")
	}
	if sign < 0 {
		integer.Neg(integer)
	}
	if !integer.IsInt64() {
		return 0, errors.New("數值超出範圍")
	}
	return Value(integer.Int64()), nil
}

func FromInt64(value int64) (Value, error) {
	integer := new(big.Int).Mul(big.NewInt(value), big.NewInt(Scale))
	if !integer.IsInt64() {
		return 0, errors.New("數值超出範圍")
	}
	return Value(integer.Int64()), nil
}

func Format(value Value) string {
	raw := int64(value)
	negative := raw < 0
	abs := new(big.Int).SetInt64(raw)
	if negative {
		abs.Neg(abs)
	}
	digits := abs.String()
	for len(digits) <= 7 {
		digits = "0" + digits
	}
	whole := digits[:len(digits)-7]
	fraction := strings.TrimRight(digits[len(digits)-7:], "0")
	result := whole
	if fraction != "" {
		result += "." + fraction
	}
	if negative && result != "0" {
		result = "-" + result
	}
	return result
}

func Add(left, right Value) (Value, error) {
	result := new(big.Int).Add(big.NewInt(int64(left)), big.NewInt(int64(right)))
	if !result.IsInt64() {
		return 0, errors.New("數值超出範圍")
	}
	return Value(result.Int64()), nil
}

func Multiply(left, right Value) (Value, error) {
	product := new(big.Int).Mul(big.NewInt(int64(left)), big.NewInt(int64(right)))
	return roundedQuotient(product, big.NewInt(Scale))
}

func Divide(left, right Value) (Value, error) {
	if right == 0 { return 0, errors.New("除數不可為零") }
	numerator := new(big.Int).Mul(big.NewInt(int64(left)), big.NewInt(Scale))
	return roundedQuotient(numerator, big.NewInt(int64(right)))
}

func MultiplyRatio(value Value, numerator, denominator int64) (Value, error) {
	if denominator == 0 {
		return 0, errors.New("除數不可為零")
	}
	product := new(big.Int).Mul(big.NewInt(int64(value)), big.NewInt(numerator))
	return roundedQuotient(product, big.NewInt(denominator))
}

func RoundInt64(value Value) int64 {
	result, _ := roundedQuotient(big.NewInt(int64(value)), big.NewInt(Scale))
	return int64(result)
}

func Float64(value Value) float64 {
	return float64(value) / float64(Scale)
}

func roundedQuotient(numerator, denominator *big.Int) (Value, error) {
	if denominator.Sign() == 0 {
		return 0, errors.New("除數不可為零")
	}
	negative := numerator.Sign()*denominator.Sign() < 0
	absNumerator := new(big.Int).Abs(new(big.Int).Set(numerator))
	absDenominator := new(big.Int).Abs(new(big.Int).Set(denominator))
	quotient, remainder := new(big.Int), new(big.Int)
	quotient.QuoRem(absNumerator, absDenominator, remainder)
	if new(big.Int).Lsh(remainder, 1).Cmp(absDenominator) >= 0 {
		quotient.Add(quotient, big.NewInt(1))
	}
	if negative {
		quotient.Neg(quotient)
	}
	if !quotient.IsInt64() {
		return 0, errors.New("數值超出範圍")
	}
	return Value(quotient.Int64()), nil
}

func digitsOnly(text string) bool {
	for _, character := range text {
		if character < '0' || character > '9' {
			return false
		}
	}
	return true
}
