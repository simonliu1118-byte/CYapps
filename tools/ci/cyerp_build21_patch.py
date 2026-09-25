from pathlib import Path

ROOT = Path("apps/CYERPAutoInput")


def read(name: str) -> str:
    return (ROOT / name).read_text(encoding="utf-8").replace("\r\n", "\n")


def write(name: str, text: str) -> None:
    with open(ROOT / name, "w", encoding="utf-8", newline="\n") as f:
        f.write(text)


def rep(name: str, old: str, new: str, count: int = 1) -> None:
    text = read(name)
    if old not in text:
        raise SystemExit(f"Expected source block not found in {name}: {old[:120]!r}")
    write(name, text.replace(old, new, count))


write("BUILD", "21\n")
rep("AppLogger.cs", "CYERPAutoInput V0.1.0 Build 20 starting", "CYERPAutoInput V0.1.0 Build 21 starting")
rep("Build10UiPatch.cs", "V0.1.0 Build 20 · Esc：緊急停止 · 不自動儲存 ERP", "V0.1.0 Build 21 · Esc：緊急停止 · 不自動儲存 ERP")
rep("MainForm.cs", "V0.1.0 Build 20 · Esc：緊急停止 · 不自動儲存 ERP", "V0.1.0 Build 21 · Esc：緊急停止 · 不自動儲存 ERP")

path = "ErpAutomationService.cs"
text = read(path)
start_marker = "    private async Task<GridGeometry> SelectBatchIfRequiredAsync(\n"
end_marker = "    private async Task HandleRowTransitionDialogsAsync(\n"
start = text.find(start_marker)
end = text.find(end_marker, start)
if start < 0 or end < 0:
    raise SystemExit("Build 21 batch method markers not found")

new_method = '''    private async Task<GridGeometry> SelectBatchIfRequiredAsync(
        nint root,
        WindowControl grid,
        GridGeometry geometry,
        int visibleRow,
        string itemCode,
        CancellationToken cancellationToken)
    {
        geometry = await EnsureDetailColumnVisibleAsync(root, grid, geometry, visibleRow, 5, cancellationToken);
        if (!Win32Automation.PrepareForeground(root, _log))
            throw new InvalidOperationException("檢查批號欄前無法把 ERP 帶到前景。");

        var (activeGeometry, point, freshRect) = await ResolveFreshDetailCellPointAsync(grid.Handle, geometry, visibleRow, 5, cancellationToken);
        geometry = activeGeometry;

        // A non-batch-managed item has a real batch column cell, but COPI08 does not
        // create an editor for that cell. Build 20 incorrectly treated "no editor" as
        // a hard failure. Build 21 verifies that the click stayed inside the live grid,
        // then lets ERP itself answer the question: F2 opens only when the batch lookup
        // is available. No Enter is sent merely to probe the cell.
        InputSender.Click(point);
        await Delay(120, cancellationToken);

        var focus = NativeMethods.FocusedControlOfForeground(root);
        if (focus == 0 || !Win32Automation.IsInside(focus, grid.Handle))
        {
            InputSender.Click(point);
            await Delay(120, cancellationToken);
            focus = NativeMethods.FocusedControlOfForeground(root);
        }
        if (focus == 0 || !Win32Automation.IsInside(focus, grid.Handle))
            throw new InvalidOperationException($"品號 {itemCode}：批號欄點擊後焦點未留在商品明細；為避免後續欄位錯位已停止。");

        var editor = await WaitGridEditorAsync(root, freshRect, point, cancellationToken, 180);
        var marker = editor == 0 ? string.Empty : NativeMethods.WindowText(editor).Trim();
        var starCount = marker.Count(ch => ch == '*');
        _log.Info("detail", $"batch probe row={visibleRow + 1} item={itemCode} focus={NativeMethods.ClassName(focus)} editor={(editor == 0 ? "none" : NativeMethods.ClassName(editor))} marker_chars={marker.Length} stars={starCount}");

        InputSender.Press(NativeMethods.VK_F2);
        var lookup = await WaitLookupAsync(cancellationToken, 850);
        if (lookup == 0)
        {
            if (starCount > 0)
                throw new InvalidOperationException($"品號 {itemCode} 的批號欄顯示批號標記，但按 F2 後沒有出現批號查詢視窗；已停止避免錯位。");

            _log.Info("detail", $"batch not required row={visibleRow + 1} item={itemCode} reason=no-f2-lookup-after-verified-grid-focus");
            return geometry;
        }

        _log.Info("detail", $"batch lookup opened row={visibleRow + 1} item={itemCode} marker_chars={marker.Length} stars={starCount}");
        if (!Win32Automation.PrepareForeground(lookup, _log))
            throw new InvalidOperationException("F2 批號查詢視窗無法取得前景。");
        await Delay(120, cancellationToken);

        var selection = await _batchCellLocator.FindFirstPositiveStockAsync(lookup, cancellationToken);
        InputSender.Click(selection.Point);
        await Delay(120, cancellationToken);

        var lookupFocus = NativeMethods.FocusedControlOfForeground(lookup);
        if (lookupFocus == 0 || !NativeMethods.ClassName(lookupFocus).Equals("TcxGridSite", StringComparison.OrdinalIgnoreCase))
        {
            _log.Warn("detail", $"F2 batch row click did not focus grid first_try class={NativeMethods.ClassName(lookupFocus)}");
            InputSender.Click(selection.Point);
            await Delay(120, cancellationToken);
            lookupFocus = NativeMethods.FocusedControlOfForeground(lookup);
        }
        if (lookupFocus == 0 || !NativeMethods.ClassName(lookupFocus).Equals("TcxGridSite", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("已點到正庫存批號列，但 F2 表格沒有取得可驗證焦點；為避免 Enter 送到錯誤控制項已停止。");

        InputSender.Press(NativeMethods.VK_RETURN);
        if (!await WaitWindowClosedAsync(lookup, 1400, cancellationToken))
            throw new InvalidOperationException("已選取正庫存批號並送出 Enter，但 F2 視窗仍未關閉。");

        if (!Win32Automation.PrepareForeground(root, _log))
            throw new InvalidOperationException("批號 F2 關閉後無法回到 ERP 前景。");
        await Delay(120, cancellationToken);
        _log.Info("detail", $"batch selected row={visibleRow + 1} item={itemCode} lookup_row={selection.RowNumber} positive_stock={selection.Stock}");
        return geometry;
    }

'''
text = text[:start] + new_method + text[end:]
old_wait = "    private static async Task<nint> WaitLookupAsync(CancellationToken cancellationToken)\n    {\n        var stop = Environment.TickCount64 + 2500;\n"
new_wait = "    private static async Task<nint> WaitLookupAsync(CancellationToken cancellationToken, int timeoutMs = 2500)\n    {\n        var stop = Environment.TickCount64 + Math.Max(100, timeoutMs);\n"
if old_wait not in text:
    raise SystemExit("WaitLookupAsync block not found")
text = text.replace(old_wait, new_wait, 1)
write(path, text)
