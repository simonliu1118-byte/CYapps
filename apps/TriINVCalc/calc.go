//go:build windows

package main

import (
	"fmt"
	"strconv"
	"strings"
)

func parseQty(s string) int64 {
	s = strings.TrimSpace(s)
	if s == "" { return 0 }
	v, err := strconv.ParseInt(s, 10, 64)
	if err != nil || v < 0 { return 0 }
	return v
}

func parseMoneyCents(s string) (int64, bool) {
	s = strings.TrimSpace(s)
	if s == "" { return 0, true }
	if !validMoneyText(s) || s == "." { return 0, false }
	parts := strings.SplitN(s, ".", 2)
	whole := int64(0)
	if parts[0] != "" {
		v, err := strconv.ParseInt(parts[0], 10, 64)
		if err != nil || v < 0 { return 0, false }
		whole = v
	}
	frac := int64(0)
	if len(parts) == 2 && parts[1] != "" {
		fs := parts[1]
		if len(fs) == 1 { fs += "0" }
		v, err := strconv.ParseInt(fs, 10, 64)
		if err != nil { return 0, false }
		frac = v
	}
	if whole > (1<<62)/100 { return 0, false }
	return whole*100 + frac, true
}

func parseWholeMoneyCents(s string) (int64, bool) {
	s = strings.TrimSpace(s)
	if s == "" { return 0, true }
	if !validDiscountText(s) { return 0, false }
	v, err := strconv.ParseInt(s, 10, 64)
	if err != nil || v < 0 || v > (1<<62)/100 { return 0, false }
	return v * 100, true
}

func safeMul(a, b int64) (int64, bool) {
	if a < 0 || b < 0 { return 0, false }
	if a == 0 || b == 0 { return 0, true }
	const maxInt64 = int64(^uint64(0) >> 1)
	if a > maxInt64/b { return 0, false }
	return a * b, true
}

func safeAdd(a, b int64) (int64, bool) {
	const maxInt64 = int64(^uint64(0) >> 1)
	if a < 0 || b < 0 || a > maxInt64-b { return 0, false }
	return a + b, true
}

func roundDivHalfUp(num, den int64) int64 {
	if den <= 0 { return 0 }
	if num >= 0 { return (num + den/2) / den }
	return -((-num + den/2) / den)
}

func fitUnitAtScale(grossPriceCents, qty, targetAmountCents, scale int64) (scaled int64, distortion int64, ok bool) {
	if qty <= 0 || targetAmountCents < 0 || targetAmountCents%100 != 0 || (scale != 100 && scale != 1000) { return 0, 0, false }
	targetDollars := targetAmountCents / 100
	half := scale / 2
	lowerNumerator := targetDollars*scale - half
	if lowerNumerator < 0 { lowerNumerator = 0 }
	upperNumerator := targetDollars*scale + half - 1
	lower := (lowerNumerator + qty - 1) / qty
	upper := upperNumerator / qty
	if lower > upper { return 0, 0, false }
	baseNumerator, okMul := safeMul(grossPriceCents, scale)
	if !okMul { return 0, 0, false }
	base := roundDivHalfUp(baseNumerator, 105)
	if base < lower { base = lower } else if base > upper { base = upper }
	left, okMul := safeMul(base, 105*(1000/scale))
	if !okMul { return 0, 0, false }
	right, okMul := safeMul(grossPriceCents, 1000)
	if !okMul { return 0, 0, false }
	if left >= right { distortion = left - right } else { distortion = right - left }
	return base, distortion, true
}

func fitDisplayUnit(grossPriceCents, qty, targetAmountCents int64) (scaled int64, decimals int, distortion int64, ok bool) {
	if v, d, found := fitUnitAtScale(grossPriceCents, qty, targetAmountCents, 100); found { return v, 2, d, true }
	if v, d, found := fitUnitAtScale(grossPriceCents, qty, targetAmountCents, 1000); found { return v, 3, d, true }
	return 0, 0, 0, false
}

func formatScaledMoney(v int64, decimals int) string {
	if decimals != 2 && decimals != 3 { return "0" }
	sign := ""
	if v < 0 { sign = "-"; v = -v }
	scale := int64(100); if decimals == 3 { scale = 1000 }
	whole := v / scale; frac := v % scale
	if frac == 0 { return sign + formatIntComma(whole) }
	fracText := fmt.Sprintf("%0*d", decimals, frac)
	fracText = strings.TrimRight(fracText, "0")
	return sign + formatIntComma(whole) + "." + fracText
}

func applyTailDifference(res *calcResult) bool {
	for i := range res.Rows { res.Rows[i].AdjustedNetCents = res.Rows[i].InitialNetCents; res.Rows[i].TailAdjustmentCents = 0 }
	if res.TailCents == 0 { return true }
	type candidate struct { row int; target int64; scaled int64; decimals int; distortion int64 }
	best := candidate{row: -1}
	for i := range res.Rows {
		rr := res.Rows[i]; if !rr.ValidForTail { continue }
		target := rr.InitialNetCents + res.TailCents; if target < 0 { continue }
		scaled, decimals, distortion, ok := fitDisplayUnit(rr.GrossUnitCents, rr.Qty, target); if !ok { continue }
		if best.row < 0 || distortion < best.distortion || (distortion == best.distortion && i > best.row) { best = candidate{i, target, scaled, decimals, distortion} }
	}
	if best.row >= 0 {
		rr := &res.Rows[best.row]; rr.AdjustedNetCents = best.target; rr.TailAdjustmentCents = res.TailCents; rr.NetUnitScaled = best.scaled; rr.NetUnitDecimals = best.decimals; rr.DisplayUnitReady = true; res.TailRows = append(res.TailRows, best.row)
		traceLog("TAIL_APPLIED_SINGLE", "row=%d tail=%s amount=%s unit=%s", best.row+1, formatMoney(res.TailCents), formatMoney(best.target), formatScaledMoney(best.scaled, best.decimals)); return true
	}
	if res.TailCents > 0 { return false }
	remaining := -res.TailCents
	for i := len(res.Rows)-1; i >= 0 && remaining > 0; i-- {
		rr := &res.Rows[i]; if !rr.ValidForTail || rr.AdjustedNetCents <= 0 { continue }
		deduct := remaining; if deduct > rr.AdjustedNetCents { deduct = rr.AdjustedNetCents }
		target := rr.AdjustedNetCents - deduct
		scaled, decimals, _, ok := fitDisplayUnit(rr.GrossUnitCents, rr.Qty, target); if !ok { continue }
		rr.AdjustedNetCents = target; rr.TailAdjustmentCents -= deduct; rr.NetUnitScaled = scaled; rr.NetUnitDecimals = decimals; rr.DisplayUnitReady = true; res.TailRows = append(res.TailRows, i); remaining -= deduct
		traceLog("TAIL_APPLIED_SPLIT", "row=%d deduction=%s remaining=%s amount=%s unit=%s", i+1, formatMoney(deduct), formatMoney(remaining), formatMoney(target), formatScaledMoney(scaled, decimals))
	}
	return remaining == 0
}

func calculate() calcResult {
	traceLog("CALCULATE_START", "")
	res := calcResult{Valid: true}
	defer func(){ traceLog("CALCULATE_END", "valid=%t has_data=%t gross=%s tax=%s net=%s tail=%s kind=%s error=%q", res.Valid,res.HasData,formatMoney(res.GrossTotalCents),formatMoney(res.TaxCents),formatMoney(res.NetSalesCents),formatMoney(res.TailCents),res.ErrorKind,res.ErrorMessage) }()
	for i:=0;i<5;i++ {
		name:=getText(nameEdits[i]); qtyText:=strings.TrimSpace(getText(qtyEdits[i])); priceText:=strings.TrimSpace(getText(priceEdits[i])); qty:=parseQty(qtyText)
		if qtyText!="" && (qty<1||qty>999){ res.Valid=false;res.ErrorKind="input";res.ErrorMessage=fmt.Sprintf("第 %d 列的數量必須介於 1～999。",i+1);return res }
		priceCents,ok:=parseMoneyCents(priceText); if !ok { res.Valid=false;res.ErrorKind="input";res.ErrorMessage=fmt.Sprintf("第 %d 列的含稅單價格式不正確。",i+1);return res }
		if priceText!="" && (priceCents<100||priceCents>999900){res.Valid=false;res.ErrorKind="input";res.ErrorMessage=fmt.Sprintf("第 %d 列的含稅單價必須介於 1～9,999 元。",i+1);return res}
		active:=name!=""||qtyText!=""||priceText!=""; if active {res.HasData=true}
		rr:=rowResult{Qty:qty,Active:active}; if active {
			if name==""&&(qtyText!=""||priceText!=""){rr.DisplayName="商品"}else{rr.DisplayName=name}; rr.GrossUnitCents=priceCents
			grossRaw,okMul:=safeMul(qty,priceCents);if !okMul{res.Valid=false;res.ErrorKind="internal";res.ErrorMessage=fmt.Sprintf("第 %d 列的數量與單價乘積過大。",i+1);return res}
			rr.GrossLineCents=roundDivHalfUp(grossRaw,100)*100;rr.InitialNetCents=roundDivHalfUp(rr.GrossLineCents,105)*100;rr.AdjustedNetCents=rr.InitialNetCents;rr.ValidForTail=qty>0&&priceCents>0
			var okAdd bool;res.GrossItemsCents,okAdd=safeAdd(res.GrossItemsCents,rr.GrossLineCents);if !okAdd{res.Valid=false;res.ErrorKind="internal";res.ErrorMessage="商品含稅合計金額過大。";return res};res.InitialNetSumCents,okAdd=safeAdd(res.InitialNetSumCents,rr.InitialNetCents);if !okAdd{res.Valid=false;res.ErrorKind="internal";res.ErrorMessage="商品未稅合計金額過大。";return res}
		}
		res.Rows[i]=rr
	}
	discountCents,ok:=parseWholeMoneyCents(strings.TrimSpace(getText(discountEdit)));if !ok{res.Valid=false;res.ErrorKind="input";res.ErrorMessage="含稅折扣只能輸入半形整數。";return res};res.DiscountCents=discountCents;if discountCents>0{res.HasData=true};if discountCents>res.GrossItemsCents{res.Valid=false;res.ErrorKind="input";res.ErrorMessage="含稅折扣不可大於商品含稅合計。";return res}
	res.GrossTotalCents=res.GrossItemsCents-discountCents
	if res.GrossTotalCents/100>999999999{res.Valid=false;res.ErrorKind="input";res.ErrorMessage="總計超過固定中文大寫欄位可顯示的 999,999,999 元。";return res}
	res.TaxDollars=roundDivHalfUp(res.GrossTotalCents,2100);res.TaxCents=res.TaxDollars*100;res.NetSalesCents=res.GrossTotalCents-res.TaxCents;res.NetDiscountCents=roundDivHalfUp(discountCents,105)*100;res.TailCents=res.NetSalesCents-(res.InitialNetSumCents-res.NetDiscountCents)
	if !applyTailDifference(&res){res.Valid=false;res.ErrorKind="calculation";res.ErrorMessage="尾差無法安全分配至商品明細。";return res}
	for i:=range res.Rows {rr:=&res.Rows[i];if !rr.Active{continue};if rr.ValidForTail{if !rr.DisplayUnitReady{scaled,dec,_,okFit:=fitDisplayUnit(rr.GrossUnitCents,rr.Qty,rr.AdjustedNetCents);if !okFit{res.Valid=false;res.ErrorKind="calculation";res.ErrorMessage=fmt.Sprintf("第 %d 列無法在三位小數內建立可核對的未稅單價。",i+1);return res};rr.NetUnitScaled=scaled;rr.NetUnitDecimals=dec;rr.DisplayUnitReady=true}}else if rr.GrossUnitCents>0{num,okMul:=safeMul(rr.GrossUnitCents,100);if !okMul{res.Valid=false;res.ErrorKind="internal";res.ErrorMessage=fmt.Sprintf("第 %d 列的未稅單價過大。",i+1);return res};rr.NetUnitScaled=roundDivHalfUp(num,105);rr.NetUnitDecimals=2;rr.DisplayUnitReady=true}else{rr.NetUnitScaled=0;rr.NetUnitDecimals=2;rr.DisplayUnitReady=true}}
	adjustedSum:=int64(0);displayMathOK:=true
	for i:=range res.Rows{rr:=res.Rows[i];var okAdd bool;adjustedSum,okAdd=safeAdd(adjustedSum,rr.AdjustedNetCents);if !okAdd{res.Valid=false;res.ErrorKind="internal";res.ErrorMessage="調整後商品未稅合計金額過大。";return res};if rr.ValidForTail{if !rr.DisplayUnitReady{displayMathOK=false;continue};scale:=int64(100);if rr.NetUnitDecimals==3{scale=1000};prod,okMul:=safeMul(rr.Qty,rr.NetUnitScaled);if !okMul||roundDivHalfUp(prod,scale)*100!=rr.AdjustedNetCents{displayMathOK=false}}}
	ok1:=res.GrossItemsCents-res.DiscountCents==res.GrossTotalCents;ok2:=res.NetSalesCents+res.TaxCents==res.GrossTotalCents;ok3:=adjustedSum-res.NetDiscountCents==res.NetSalesCents
	if !(ok1&&ok2&&ok3&&displayMathOK){res.Valid=false;res.ErrorKind="calculation";if !displayMathOK{res.ErrorMessage="明細單價、數量與金額無法一致。"}else{res.ErrorMessage="尾差調整後，金額核對仍不一致。"};res.VerificationDetails=fmt.Sprintf("含稅商品合計=%s；含稅折扣=%s；含稅總計=%s；未稅銷售額=%s；營業稅=%s；調整後明細合計=%s；未稅折扣=%s",formatMoney(res.GrossItemsCents),formatMoney(res.DiscountCents),formatMoney(res.GrossTotalCents),formatMoney(res.NetSalesCents),formatMoney(res.TaxCents),formatMoney(adjustedSum),formatMoney(res.NetDiscountCents));return res}
	return res
}

func formatMoney(cents int64) string { sign:="";if cents<0{sign="-";cents=-cents};whole:=cents/100;frac:=cents%100;wholeStr:=formatIntComma(whole);if frac==0{return sign+wholeStr};if frac%10==0{return fmt.Sprintf("%s%s.%d",sign,wholeStr,frac/10)};return fmt.Sprintf("%s%s.%02d",sign,wholeStr,frac)}
func formatIntComma(v int64) string{s:=strconv.FormatInt(v,10);if len(s)<=3{return s};var b strings.Builder;first:=len(s)%3;if first==0{first=3};b.WriteString(s[:first]);for i:=first;i<len(s);i+=3{b.WriteByte(',');b.WriteString(s[i:i+3])};return b.String()}
