//go:build windows

package main

import (
	"fmt"
	"path/filepath"
	"strings"
	"time"
)

type fileEntry struct {
	Path   string
	Status string
}

type batchEvent struct {
	Kind    string
	File    string
	Result  ConvResult
	ErrText string
	Index   int
	Total   int
}

func startBatch() {
	if len(files) == 0 {
		return
	}
	if strings.TrimSpace(state.Settings.POSOutputDir) == "" {
		chooseOutputFolder()
		if strings.TrimSpace(state.Settings.POSOutputDir) == "" {
			return
		}
	}
	converting = true
	batchDone = false
	failures = nil
	for i := range files {
		files[i].Status = "…"
	}
	refreshFiles()
	updateButtons()
	pSendMessageW.Call(hwndProgress, PBM_SETPOS, 0, 0)
	setText(hwndStatus, "開始轉換…")
	paths := make([]string, len(files))
	for i := range files {
		paths[i] = files[i].Path
	}
	outDir := state.Settings.POSOutputDir
	detail := isLogChecked()
	diag("batch start files=%d out=%q detailed=%v", len(paths), outDir, detail)
	go runBatch(paths, outDir, detail)
}

func runBatch(paths []string, outDir string, detailed bool) {
	for i, p := range paths {
		postEvent(batchEvent{Kind: "progress", File: p, Index: i + 1, Total: len(paths)})
		if detailed {
			diag("convert begin %q", p)
		}
		res, err := convertFile(p, outDir)
		if err != nil {
			diag("convert FAIL %q: %v", p, err)
			postEvent(batchEvent{Kind: "failure", File: p, ErrText: err.Error(), Index: i + 1, Total: len(paths)})
		} else {
			if detailed {
				diag("convert OK %q -> %q", p, res.OutputPath)
			}
			postEvent(batchEvent{Kind: "success", File: p, Result: res, Index: i + 1, Total: len(paths)})
		}
	}
	postEvent(batchEvent{Kind: "done", Total: len(paths)})
}

func postEvent(ev batchEvent) {
	uiEvents <- ev
	pPostMessageW.Call(hwndMain, WM_UI_EVENT, 0, 0)
}

func handleUIEvents() {
	for {
		select {
		case ev := <-uiEvents:
			switch ev.Kind {
			case "progress":
				setText(hwndStatus, fmt.Sprintf("轉換中 %d/%d：%s", ev.Index, ev.Total, filepath.Base(ev.File)))
			case "failure":
				failures = append(failures, fmt.Sprintf("%s\r\n%s", filepath.Base(ev.File), ev.ErrText))
				setFileStatus(ev.File, "×")
				pSendMessageW.Call(hwndProgress, PBM_SETPOS, uintptr(ev.Index*1000/ev.Total), 0)
			case "success":
				setFileStatus(ev.File, "✓")
				state.History = append([]HistoryItem{{Date: time.Now().Format("2006/01/02"), OrderType: ev.Result.OrderType, OrderNo: ev.Result.OrderNo, Customer: ev.Result.Customer}}, state.History...)
				if len(state.History) > 99 {
					state.History = state.History[:99]
				}
				if err := saveState(state); err != nil {
					diag("save history error: %v", err)
				}
				pSendMessageW.Call(hwndProgress, PBM_SETPOS, uintptr(ev.Index*1000/ev.Total), 0)
			case "done":
				converting = false
				batchDone = true
				refreshFiles()
				refreshHistory()
				updateButtons()
				if len(failures) == 0 {
					setText(hwndStatus, "轉換完成：全部成功")
					message("轉換完成", "全部檔案轉換成功。", MB_OK|MB_ICONINFORMATION)
				} else {
					setText(hwndStatus, fmt.Sprintf("轉換完成：%d 個失敗", len(failures)))
					message("轉換完成", fmt.Sprintf("完成，但有 %d 個檔案轉換失敗。請查看「失敗紀錄」。", len(failures)), MB_OK|MB_ICONWARNING)
				}
				diag("batch done failures=%d", len(failures))
			}
		default:
			return
		}
	}
}

func setFileStatus(path, status string) {
	for i := range files {
		if strings.EqualFold(files[i].Path, path) {
			files[i].Status = status
			listSetText(hwndFiles, i, 1, status)
			return
		}
	}
}
func isLogChecked() bool {
	r, _, _ := pSendMessageW.Call(hwndLog, BM_GETCHECK, 0, 0)
	return r == BST_CHECKED
}
