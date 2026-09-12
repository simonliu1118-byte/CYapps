package postal

import "testing"

func TestLookup(t *testing.T) {
	cases := map[string]string{
		"高雄市三民區建國二路": "807",
		"台北市大安區忠孝東路": "106",
		"220 新北市板橋區": "220",
		"臺中市大安區中山南路": "439",
		"彰化縣鹿港鎮中山路":  "505",
		"花蓮縣玉里鎮中山路":  "981",
		"屏東縣琉球鄉民生路":  "929",
	}
	for input, want := range cases {
		if got := Lookup(input); got != want {
			t.Fatalf("Lookup(%q)=%q want %q", input, got, want)
		}
	}
}
