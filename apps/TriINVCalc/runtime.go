//go:build windows

package main

import (
	"bufio"
	"fmt"
	"os"
	"path/filepath"
	"strconv"
	"strings"
	"syscall"
	"time"
	"unsafe"
)

func moduleDir() string {
	buf := make([]uint16, 32768)
	n, _, _ := procGetModuleFileNameW.Call(0, uintptr(unsafe.Pointer(&buf[0])), uintptr(len(buf)))
	if n == 0 { return "." }
	return filepath.Dir(syscall.UTF16ToString(buf[:n]))
}

func describeEdit(hwnd uintptr) string {
	if hwnd == 0 { return "none" }
	meta, ok := editMetas[hwnd]
	if !ok { id,_,_:=procGetDlgCtrlID.Call(hwnd); return fmt.Sprintf("hwnd=%d,id=%d",hwnd,int32(id)) }
	if meta.kind==fieldDiscount{return "含稅折扣"}
	row:=meta.order/3+1;col:="品名";switch meta.kind{case fieldQty:col="數量";case fieldMoney:col="含稅單價"};return fmt.Sprintf("第%d列%s",row,col)
}

const(traceMaxLines=10000;traceKeepLines=8000;errorMaxEntries=100)

func readLogLines(path string)([]string,error){f,err:=os.Open(path);if err!=nil{if os.IsNotExist(err){return nil,nil};return nil,err};defer f.Close();scanner:=bufio.NewScanner(f);scanner.Buffer(make([]byte,64*1024),2*1024*1024);var lines []string;for scanner.Scan(){lines=append(lines,scanner.Text())};return lines,scanner.Err()}
func rewriteLines(path string,lines []string)error{tmp:=path+".tmp";f,err:=os.OpenFile(tmp,os.O_CREATE|os.O_WRONLY|os.O_TRUNC,0644);if err!=nil{return err};for _,line:=range lines{if _,err=f.WriteString(line+"\r\n");err!=nil{_ = f.Close();_ = os.Remove(tmp);return err}};_ = f.Sync();if err=f.Close();err!=nil{_ = os.Remove(tmp);return err};_ = os.Remove(path);return os.Rename(tmp,path)}

func initTraceLog(){filename:="三聯式發票計算機_Trace.log";candidates:=[]string{filepath.Join(moduleDir(),filename)};if cacheDir,err:=os.UserCacheDir();err==nil&&cacheDir!=""{fallbackDir:=filepath.Join(cacheDir,"三聯式發票計算機");_ = os.MkdirAll(fallbackDir,0755);candidates=append(candidates,filepath.Join(fallbackDir,filename))};for _,path:=range candidates{lines,err:=readLogLines(path);if err==nil&&len(lines)>traceMaxLines{lines=lines[len(lines)-traceKeepLines:];_ = rewriteLines(path,lines)};f,err:=os.OpenFile(path,os.O_CREATE|os.O_WRONLY|os.O_APPEND,0644);if err==nil{traceFile=f;tracePath=path;traceLineCount=len(lines);break}};traceLog("SESSION_SEPARATOR","============================================================");traceLog("APPLICATION_START","version=%s trace=%s",appVersion,tracePath)}
func trimTraceLocked(){if traceFile==nil||tracePath==""{return};_ = traceFile.Sync();_ = traceFile.Close();traceFile=nil;lines,err:=readLogLines(tracePath);if err==nil&&len(lines)>traceKeepLines{lines=lines[len(lines)-traceKeepLines:];_ = rewriteLines(tracePath,lines);traceLineCount=len(lines)};f,openErr:=os.OpenFile(tracePath,os.O_CREATE|os.O_WRONLY|os.O_APPEND,0644);if openErr==nil{traceFile=f}}
func traceLog(event,format string,args ...any){if traceFile==nil{return};traceMu.Lock();defer traceMu.Unlock();traceSequence++;tid,_,_:=procGetCurrentThreadId.Call();detail:="";if format!=""{detail=fmt.Sprintf(format,args...);detail=strings.ReplaceAll(detail,"\r"," ");detail=strings.ReplaceAll(detail,"\n"," | ")};line:=fmt.Sprintf("%s #%06d T%d %-28s",time.Now().Format("2006-01-02 15:04:05.000"),traceSequence,tid,event);if detail!=""{line+=" "+detail};if _,err:=traceFile.WriteString(line+"\r\n");err==nil{traceLineCount++};if traceLineCount%25==0||strings.Contains(event,"PANIC")||strings.Contains(event,"ERROR")||strings.Contains(event,"APPLICATION_EXIT"){_ = traceFile.Sync()};if traceLineCount>=traceMaxLines{trimTraceLocked()}}
func closeTraceLog(normal bool){if traceFile==nil{return};if normal{traceLog("APPLICATION_EXIT_NORMAL","")}else{traceLog("APPLICATION_EXIT_ABNORMAL","")};traceMu.Lock();if traceFile!=nil{_ = traceFile.Sync();_ = traceFile.Close();traceFile=nil};traceMu.Unlock()}

func writeCalculationErrorLog(res calcResult) error {
	path:=filepath.Join(moduleDir(),"三聯式發票計算機_ERROR.log");var b strings.Builder;fmt.Fprintf(&b,"時間：%s\r\n",time.Now().Format("2006-01-02 15:04:05"));fmt.Fprintf(&b,"程式版本：%s\r\n",appVersion);fmt.Fprintf(&b,"錯誤類型：%s\r\n",res.ErrorKind);fmt.Fprintf(&b,"錯誤訊息：%s\r\n",res.ErrorMessage)
	for i,rr:=range res.Rows{fmt.Fprintf(&b,"\r\n第%d列\r\n",i+1);if !rr.Active{b.WriteString("空白\r\n");continue};fmt.Fprintf(&b,"品名：%s\r\n",rr.DisplayName);fmt.Fprintf(&b,"數量：%d\r\n",rr.Qty);fmt.Fprintf(&b,"含稅單價：%s\r\n",formatMoney(rr.GrossUnitCents));fmt.Fprintf(&b,"含稅金額：%s\r\n",formatMoney(rr.GrossLineCents));fmt.Fprintf(&b,"未稅單價：%s（%d位小數）\r\n",formatScaledMoney(rr.NetUnitScaled,rr.NetUnitDecimals),rr.NetUnitDecimals);fmt.Fprintf(&b,"初步未稅金額：%s\r\n",formatMoney(rr.InitialNetCents));fmt.Fprintf(&b,"尾差調整：%s\r\n",formatMoney(rr.TailAdjustmentCents));fmt.Fprintf(&b,"最終未稅金額：%s\r\n",formatMoney(rr.AdjustedNetCents))}
	fmt.Fprintf(&b,"\r\n含稅商品合計：%s\r\n",formatMoney(res.GrossItemsCents));fmt.Fprintf(&b,"含稅折扣：%s\r\n",formatMoney(res.DiscountCents));fmt.Fprintf(&b,"含稅總計：%s\r\n",formatMoney(res.GrossTotalCents));fmt.Fprintf(&b,"營業稅：%s\r\n",formatMoney(res.TaxCents));fmt.Fprintf(&b,"未稅銷售額：%s\r\n",formatMoney(res.NetSalesCents));fmt.Fprintf(&b,"未稅折扣：%s\r\n",formatMoney(res.NetDiscountCents));fmt.Fprintf(&b,"總尾差：%s\r\n",formatMoney(res.TailCents));if len(res.TailRows)>0{rows:=make([]string,0,len(res.TailRows));for _,row:=range res.TailRows{rows=append(rows,strconv.Itoa(row+1))};fmt.Fprintf(&b,"尾差調整列：第%s列\r\n",strings.Join(rows,"、"))};if res.VerificationDetails!=""{fmt.Fprintf(&b,"核對資料：%s\r\n",res.VerificationDetails)};entry:=strings.TrimSpace(b.String())
	const delimiter="==================================================";content,_:=os.ReadFile(path);parts:=strings.Split(string(content),delimiter);entries:=make([]string,0,len(parts)+1);for _,part:=range parts{part=strings.TrimSpace(part);if part!=""{entries=append(entries,part)}};if len(entries)>=errorMaxEntries{entries=entries[len(entries)-(errorMaxEntries-1):]};entries=append(entries,entry);var out strings.Builder;for _,item:=range entries{out.WriteString(delimiter);out.WriteString("\r\n");out.WriteString(item);out.WriteString("\r\n")};out.WriteString(delimiter);out.WriteString("\r\n");return os.WriteFile(path,[]byte(out.String()),0644)
}

func scheduleRecalc(){traceLog("RECALC_QUEUE_REQUEST","ready=%t hwnd=%d pending=%t in_recalc=%t",appReady,mainHwnd,pendingRecalc,inRecalc);if !appReady||mainHwnd==0||pendingRecalc{return};pendingRecalc=true;posted,_,_:=procPostMessageW.Call(mainHwnd,WM_APP_RECALC,0,0);if posted==0{pendingRecalc=false;return}}
func recalc(showInputWarning bool){if inRecalc{scheduleRecalc();return};inRecalc=true;defer func(){inRecalc=false}();res:=calculate();currentResult=res;procInvalidateRect.Call(mainHwnd,0,1);if !res.Valid{isCalculationError:=res.ErrorKind=="calculation"||res.ErrorKind=="internal";key:=res.ErrorKind+"|"+res.ErrorMessage+res.VerificationDetails;if showInputWarning||isCalculationError{if key!=lastWarningKey{lastWarningKey=key;msg:=res.ErrorMessage;if res.VerificationDetails!=""{msg+="\n\n"+res.VerificationDetails};if isCalculationError{if err:=writeCalculationErrorLog(res);err!=nil{msg+="\n\nERROR LOG 無法寫入 EXE 同資料夾："+err.Error()}else{msg+="\n\n精簡錯誤資料已寫入「三聯式發票計算機_ERROR.log」。"}};showMessage(appTitle,msg,MB_OK|MB_ICONWARNING)}};return};lastWarningKey=""}
func clearAll(){suppressKillFocus=true;defer func(){suppressKillFocus=false}();for i:=0;i<5;i++{setText(nameEdits[i],"");setText(qtyEdits[i],"");setText(priceEdits[i],"")};setText(discountEdit,"");currentResult=calcResult{Valid:true};lastWarningKey="";pendingRecalc=false;procInvalidateRect.Call(mainHwnd,0,1);pendingFocusOrder=-1;if !pendingFocus{pendingFocus=true;procPostMessageW.Call(mainHwnd,WM_APP_FOCUS,0,0)}}
