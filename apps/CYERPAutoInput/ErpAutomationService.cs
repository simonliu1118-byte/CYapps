namespace CYERPAutoInput;

internal enum ErpMode { Unknown, Browse, Input }

internal sealed class ErpAutomationService
{
    private readonly AppLogger _log;
    private readonly GridVisionService _gridVision;
    private readonly OpticalTextLocator _textVision;

    public ErpAutomationService(AppLogger log)
    {
        _log = log;
        var ocr = new WindowsOcrService(log);
        _gridVision = new GridVisionService(ocr, log);
        _textVision = new OpticalTextLocator(ocr, log);
    }

    public nint FindErp() => Win32Automation.FindCopi08Window();

    public ErpMode DetectMode(nint root)
    {
        if (root == 0 || !NativeMethods.GetWindowRect(root, out var rr)) return ErpMode.Unknown;
        var edits = Win32Automation.EnumerateChildren(root)
            .Where(c => c.Visible && c.ClassName.Equals("TDBEdit", StringComparison.OrdinalIgnoreCase))
            .Where(c => c.Rect.Top - rr.Top is >= 140 and <= 245)
            .ToArray();
        if (edits.Length < 3) return ErpMode.Unknown;

        var readOnly = 0;
        var writable = 0;
        foreach (var edit in edits)
        {
            var style = NativeMethods.GetWindowLongPtr(edit.Handle, NativeMethods.GWL_STYLE).ToInt64();
            if ((style & NativeMethods.ES_READONLY) != 0) readOnly++; else writable++;
        }
        _log.Info("state", $"mode signal readonly={readOnly} writable={writable}");
        if (readOnly >= 2 && writable <= 1) return ErpMode.Browse;
        if (writable >= 2) return ErpMode.Input;
        return ErpMode.Unknown;
    }

    public async Task RunAsync(FormSnapshot snapshot, IProgress<string> progress, CancellationToken cancellationToken)
    {
        var root = FindErp();
        if (root == 0) throw new InvalidOperationException("找不到 SMART ERP COPI08 視窗。\n請先開啟銷貨單建立作業。");
        if (!Win32Automation.PrepareForeground(root, _log)) throw new InvalidOperationException("無法把 COPI08 帶到前景。");

        progress.Report("ERP：確認新增狀態…");
        await EnsureInputModeAsync(root, cancellationToken);

        progress.Report("ERP：輸入表頭…");
        await FillHeaderAsync(root, snapshot, cancellationToken);
        await FillTabGroupAsync(root, snapshot, "交易資料", cancellationToken);
        await FillTabGroupAsync(root, snapshot, "送貨資料", cancellationToken);
        await FillTabGroupAsync(root, snapshot, "發票資料(一)", cancellationToken);

        if (snapshot.Details.Count > 0)
        {
            progress.Report("ERP：光學定位商品明細…");
            await FillDetailsAsync(root, snapshot.Details, progress, cancellationToken);
        }

        Win32Automation.PrepareForeground(root, _log);
        progress.Report("ERP：輸入完成；依目前安全規則未自動儲存。");
        _log.Info("automation", "input completed; ERP save intentionally not invoked");
    }

    private async Task EnsureInputModeAsync(nint root, CancellationToken cancellationToken)
    {
        var mode = DetectMode(root);
        if (mode == ErpMode.Input) return;
        if (mode != ErpMode.Browse) throw new InvalidOperationException("無法可靠判斷 ERP 目前是瀏覽或新增狀態。");

        var addPoint = await _textVision.FindTextAsync(root, ["新增"], cancellationToken);
        if (addPoint is null) throw new InvalidOperationException("光學辨識找不到 ERP 的「新增」按鈕；未進行猜測點擊。");
        InputSender.Click(addPoint.Value);
        await Delay(450, cancellationToken);
        if (DetectMode(root) != ErpMode.Input)
            throw new InvalidOperationException("已點擊「新增」，但 ERP 沒有進入可輸入狀態。");
        _log.Info("state", "ERP entered input mode by optical 新增 click");
    }

    private async Task FillHeaderAsync(nint root, FormSnapshot snapshot, CancellationToken cancellationToken)
    {
        foreach (var field in FieldCatalog.All.Where(f => f.Group == "表頭"))
        {
            if (!snapshot.Values.TryGetValue(field.Key, out var value) || string.IsNullOrWhiteSpace(value)) continue;
            cancellationToken.ThrowIfCancellationRequested();
            var target = ErpLayoutResolver.ResolveHeader(root, field.Key);
            await SetFieldAsync(root, target, field, value, cancellationToken);
            _log.Info("field", $"filled group={field.Group} key={field.Key} chars={value.Length}");
        }
    }

    private async Task FillTabGroupAsync(nint root, FormSnapshot snapshot, string group, CancellationToken cancellationToken)
    {
        var fields = FieldCatalog.All
            .Where(f => f.Group == group)
            .Where(f => snapshot.Values.TryGetValue(f.Key, out var value) && !string.IsNullOrWhiteSpace(value))
            .ToArray();
        if (fields.Length == 0) return;

        var sheet = await ActivateTabAsync(root, group, cancellationToken);
        foreach (var field in fields)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var value = snapshot.Values[field.Key];
            var target = ErpLayoutResolver.ResolveTabField(sheet, group, field.Key);
            await SetFieldAsync(root, target, field, value, cancellationToken);
            _log.Info("field", $"filled group={field.Group} key={field.Key} chars={value.Length}");
        }
    }

    private async Task<nint> ActivateTabAsync(nint root, string group, CancellationToken cancellationToken)
    {
        var sheet = ErpLayoutResolver.FindTabSheet(root, group);
        if (sheet == 0) throw new InvalidOperationException($"找不到 ERP 頁籤物件「{group}」。");
        if (NativeMethods.IsWindowVisible(sheet)) return sheet;

        var p = await _textVision.FindTextAsync(root, [group, group.Replace("(一)", "（一）")], cancellationToken);
        if (p is null) throw new InvalidOperationException($"光學辨識找不到 ERP 頁籤「{group}」。");
        InputSender.Click(p.Value);
        var deadline = Environment.TickCount64 + 1200;
        while (Environment.TickCount64 < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (NativeMethods.IsWindowVisible(sheet))
            {
                _log.Info("tab", $"activated group={group}");
                return sheet;
            }
            await Task.Delay(50, cancellationToken);
        }
        throw new InvalidOperationException($"已點擊 ERP 頁籤「{group}」，但頁籤沒有啟用。");
    }

    private async Task SetFieldAsync(nint root, WindowControl target, FieldDefinition field, string value, CancellationToken cancellationToken)
    {
        Win32Automation.PrepareForeground(root, _log);
        var center = new Point((target.Rect.Left + target.Rect.Right) / 2, (target.Rect.Top + target.Rect.Bottom) / 2);

        if (field.Kind == FieldKind.Boolean)
        {
            if (value.Equals("true", StringComparison.OrdinalIgnoreCase)) InputSender.Click(center);
            await Delay(160, cancellationToken);
            return;
        }

        if (field.Kind == FieldKind.Combo || target.ClassName.Contains("IMAGECOMBOBOX", StringComparison.OrdinalIgnoreCase))
        {
            InputSender.Click(new Point(Math.Max(target.Rect.Left + 4, target.Rect.Right - 9), center.Y));
            await Delay(140, cancellationToken);
            InputSender.UnicodeText(value);
            InputSender.Press(NativeMethods.VK_RETURN);
            await Delay(220, cancellationToken);
            return;
        }

        InputSender.Click(center);
        await Delay(100, cancellationToken);
        var focus = await WaitFocusedEditAsync(root, target.Rect, cancellationToken);
        if (focus == 0) throw new InvalidOperationException($"ERP 欄位「{field.Label}」沒有取得可輸入焦點。");

        if (field.Kind == FieldKind.Date)
        {
            var digits = new string(value.Where(char.IsDigit).ToArray());
            if (digits.Length != 8) throw new InvalidOperationException($"{field.Label} 必須是 8 位日期。");
            InputSender.Press(NativeMethods.VK_HOME);
            await Delay(80, cancellationToken);
            InputSender.UnicodeText(digits, 95);
            await Delay(120, cancellationToken);
            InputSender.Press(NativeMethods.VK_TAB);
            await Delay(240, cancellationToken);
            return;
        }

        if (field.Key == "order_type")
        {
            // Confirmed SMART ERP behavior from the Go prototype: End + Backspace x4 reliably clears the order type.
            InputSender.EndBackspace(4);
        }
        else
        {
            var before = NativeMethods.WindowText(focus);
            InputSender.Press(NativeMethods.VK_END);
            var erase = Math.Clamp(before.Length + 8, 12, 128);
            for (var i = 0; i < erase; i++) InputSender.Press(NativeMethods.VK_BACK);
        }

        InputSender.UnicodeText(value);
        await Delay(100, cancellationToken);
        InputSender.Press(field.Kind == FieldKind.Lookup ? NativeMethods.VK_RETURN : NativeMethods.VK_TAB);
        await Delay(220, cancellationToken);
    }

    private async Task FillDetailsAsync(nint root, IReadOnlyList<DetailRow> rows, IProgress<string> progress, CancellationToken cancellationToken)
    {
        Win32Automation.PrepareForeground(root, _log);
        var grid = FindDetailGrid(root) ?? throw new InvalidOperationException("找不到 ERP 商品明細 TcxGridSite。");
        var geometry = await _gridVision.AnalyzeDetailGridAsync(grid.Handle, cancellationToken);

        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress.Report($"ERP：輸入商品明細 {rowIndex + 1}/{rows.Count}…");

            var visibleRow = rowIndex;
            if (visibleRow >= geometry.RowCenterY.Count)
            {
                InputSender.Press(NativeMethods.VK_DOWN);
                await Delay(180, cancellationToken);
                geometry = await _gridVision.AnalyzeDetailGridAsync(grid.Handle, cancellationToken);
                visibleRow = geometry.RowCenterY.Count - 1;
                if (visibleRow < 0) throw new InvalidOperationException("明細捲動後無法重新辨識列位置。");
            }

            var row = rows[rowIndex];
            await SetDetailCellAsync(root, grid, geometry, visibleRow, 0, row.ItemCode, cancellationToken);

            if (!string.IsNullOrWhiteSpace(row.Unit))
                await SelectUnitAsync(root, geometry, visibleRow, row.Unit, cancellationToken);

            await SetDetailCellAsync(root, grid, geometry, visibleRow, 1, row.Quantity, cancellationToken);
            if (!string.IsNullOrWhiteSpace(row.GiftQuantity))
                await SetDetailCellAsync(root, grid, geometry, visibleRow, 3, row.GiftQuantity, cancellationToken);
            if (!string.IsNullOrWhiteSpace(row.Warehouse))
                await SetDetailCellAsync(root, grid, geometry, visibleRow, 6, row.Warehouse, cancellationToken);
            if (!string.IsNullOrWhiteSpace(row.UnitPrice))
                await SetDetailCellAsync(root, grid, geometry, visibleRow, 7, row.UnitPrice, cancellationToken);
            if (!string.IsNullOrWhiteSpace(row.Batch))
                _log.Info("detail", $"batch requested row={rowIndex + 1}; batch automation remains deferred");
        }
    }

    private WindowControl? FindDetailGrid(nint root)
    {
        NativeMethods.GetWindowRect(root, out var rr);
        var candidates = Win32Automation.EnumerateChildren(root)
            .Where(c => c.Visible && c.ClassName.Equals("TcxGridSite", StringComparison.OrdinalIgnoreCase))
            .Where(c => c.Rect.Width >= 280 && c.Rect.Height >= 45)
            .ToArray();
        var preferred = candidates
            .Where(c => (c.Rect.Top + c.Rect.Bottom) / 2 >= rr.Top + rr.Height * 45 / 100)
            .Where(c => c.Rect.Width >= rr.Width * 35 / 100)
            .OrderByDescending(c => c.Rect.Width * c.Rect.Height)
            .FirstOrDefault();
        var selected = preferred ?? candidates.OrderByDescending(c => c.Rect.Width * c.Rect.Height).FirstOrDefault();
        if (selected is not null)
            _log.Info("detail", $"grid selected size={selected.Rect.Width}x{selected.Rect.Height}");
        return selected;
    }

    private async Task SetDetailCellAsync(nint root, WindowControl grid, GridGeometry geometry, int visibleRow, int column, string value, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        if (!geometry.TryCellPoint(visibleRow, column, out var point))
            throw new InvalidOperationException($"光學明細定位缺少 column={column}, row={visibleRow + 1}。");

        Win32Automation.PrepareForeground(root, _log);
        InputSender.Click(point);
        await Delay(130, cancellationToken);
        InputSender.Press(NativeMethods.VK_RETURN);
        var editor = await WaitGridEditorAsync(root, grid.Rect.ToRectangle(), cancellationToken);
        if (editor == 0) throw new InvalidOperationException($"ERP 明細 row={visibleRow + 1}, column={column} 沒有進入編輯模式。");

        InputSender.UnicodeText(value, 35);
        await Delay(120, cancellationToken);
        InputSender.Press(NativeMethods.VK_RETURN);
        await Delay(260, cancellationToken);
        _log.Info("detail", $"cell committed row={visibleRow + 1} col={column} chars={value.Length}");
    }

    private async Task SelectUnitAsync(nint root, GridGeometry geometry, int visibleRow, string unit, CancellationToken cancellationToken)
    {
        if (!geometry.TryCellPoint(visibleRow, 4, out var point))
            throw new InvalidOperationException("光學明細定位沒有辨識到「單位」欄。");

        Win32Automation.PrepareForeground(root, _log);
        InputSender.Click(point);
        await Delay(140, cancellationToken);
        InputSender.Press(NativeMethods.VK_F2);

        var lookup = await WaitLookupAsync(cancellationToken);
        if (lookup == 0) throw new InvalidOperationException("按 F2 後沒有出現單位查詢視窗。");
        Win32Automation.PrepareForeground(lookup, _log);
        await Delay(120, cancellationToken);

        var unitPoint = await _gridVision.FindUnitAsync(lookup, unit, cancellationToken);
        InputSender.Click(unitPoint);
        await Delay(100, cancellationToken);
        // Confirmed manual ERP behavior: click requested unit -> Enter. No confirm-button fallback is allowed.
        InputSender.Press(NativeMethods.VK_RETURN);
        if (!await WaitWindowClosedAsync(lookup, 1200, cancellationToken))
            throw new InvalidOperationException("已光學點選指定單位並送出 Enter，但 F2 視窗仍未關閉。");

        Win32Automation.PrepareForeground(root, _log);
        await Delay(120, cancellationToken);
        _log.Info("detail", "F2 unit selected by OCR click + physical Enter");
    }

    private static async Task<nint> WaitLookupAsync(CancellationToken cancellationToken)
    {
        var stop = Environment.TickCount64 + 2500;
        while (Environment.TickCount64 < stop)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var hwnd = Win32Automation.FindTopLevelByTitleContains("F2開窗查詢");
            if (hwnd != 0) return hwnd;
            await Task.Delay(50, cancellationToken);
        }
        return 0;
    }

    private static async Task<bool> WaitWindowClosedAsync(nint hwnd, int timeoutMs, CancellationToken cancellationToken)
    {
        var stop = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < stop)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!NativeMethods.IsWindowVisible(hwnd)) return true;
            await Task.Delay(40, cancellationToken);
        }
        return !NativeMethods.IsWindowVisible(hwnd);
    }

    private static async Task<nint> WaitFocusedEditAsync(nint root, NativeMethods.RECT targetRect, CancellationToken cancellationToken)
    {
        var stop = Environment.TickCount64 + 800;
        while (Environment.TickCount64 < stop)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var focus = NativeMethods.FocusedControlOfForeground(root);
            if (focus != 0 && NativeMethods.ClassName(focus).Contains("EDIT", StringComparison.OrdinalIgnoreCase))
            {
                NativeMethods.GetWindowRect(focus, out var r);
                var cx = (r.Left + r.Right) / 2;
                var cy = (r.Top + r.Bottom) / 2;
                if (cx >= targetRect.Left - 8 && cx <= targetRect.Right + 8 && cy >= targetRect.Top - 8 && cy <= targetRect.Bottom + 8)
                    return focus;
            }
            await Task.Delay(35, cancellationToken);
        }
        return 0;
    }

    private static async Task<nint> WaitGridEditorAsync(nint root, Rectangle gridRect, CancellationToken cancellationToken)
    {
        var stop = Environment.TickCount64 + 950;
        while (Environment.TickCount64 < stop)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var focus = NativeMethods.FocusedControlOfForeground(root);
            if (focus != 0 && NativeMethods.ClassName(focus).Contains("EDIT", StringComparison.OrdinalIgnoreCase))
            {
                NativeMethods.GetWindowRect(focus, out var r);
                var center = new Point((r.Left + r.Right) / 2, (r.Top + r.Bottom) / 2);
                if (gridRect.Contains(center)) return focus;
            }
            await Task.Delay(35, cancellationToken);
        }
        return 0;
    }

    private static Task Delay(int milliseconds, CancellationToken cancellationToken) => Task.Delay(milliseconds, cancellationToken);
}
