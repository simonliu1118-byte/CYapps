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
        if (edits.Length < 6) return ErpMode.Unknown;

        var readOnly = 0;
        var writable = 0;
        foreach (var edit in edits)
        {
            var style = NativeMethods.GetWindowLongPtr(edit.Handle, NativeMethods.GWL_STYLE).ToInt64();
            if ((style & NativeMethods.ES_READONLY) != 0) readOnly++; else writable++;
        }
        _log.Info("state", $"mode signal total={edits.Length} readonly={readOnly} writable={writable}");

        if (writable == 0 && readOnly >= 6) return ErpMode.Browse;
        if (writable >= 4 && readOnly >= 2) return ErpMode.Input;
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
            progress.Report("ERP：啟用並光學定位商品明細…");
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

        // Delphi/DevExpress can visibly enter INPUT after the click before all TDBEdit
        // style bits have settled. Poll the proven readonly/writable signal instead of
        // making a single timing-sensitive decision.
        var deadline = Environment.TickCount64 + 3500;
        var checks = 0;
        while (Environment.TickCount64 < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Delay(150, cancellationToken);
            checks++;
            mode = DetectMode(root);
            if (mode == ErpMode.Input)
            {
                _log.Info("state", $"ERP entered input mode by optical 新增 click after_checks={checks} elapsed_ms={checks * 150}");
                return;
            }
        }
        throw new InvalidOperationException("已點擊「新增」，但等待 3.5 秒後仍無法確認 ERP 進入可輸入狀態。");
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
        var page = NativeMethods.GetParent(sheet);
        if (page == 0 || !NativeMethods.ClassName(page).Equals("TcxPageControl", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"ERP 頁籤「{group}」找不到 TcxPageControl parent。");

        if (!Win32Automation.PrepareForeground(root, _log))
            throw new InvalidOperationException("切換 ERP 頁籤前無法把 COPI08 帶到前景。");
        var p = await _textVision.FindTextAsync(page, [group, group.Replace("(一)", "（一）")], cancellationToken);
        if (p is null) throw new InvalidOperationException($"光學辨識找不到 ERP 頁籤「{group}」。");
        InputSender.Click(p.Value);

        var deadline = Environment.TickCount64 + 1200;
        while (Environment.TickCount64 < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (NativeMethods.IsWindowVisible(sheet))
            {
                _log.Info("tab", $"activated by optical real click group={group}");
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
            var desired = value.Equals("true", StringComparison.OrdinalIgnoreCase);
            var current = NativeMethods.SendMessage(target.Handle, 0x00F0, 0, 0) == 1;
            if (desired != current) InputSender.Click(center);
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

        if (field.Key is "cod" or "freight_fee")
        {
            InputSender.UnicodeText(value, 105);
            await Delay(140, cancellationToken);
            InputSender.Press(NativeMethods.VK_TAB);
            await Delay(180, cancellationToken);
            return;
        }

        if (field.Key == "order_type")
        {
            InputSender.EndBackspace(4);
        }
        else
        {
            var before = NativeMethods.WindowText(focus);
            if (before == value)
            {
                InputSender.Press(NativeMethods.VK_TAB);
                await Delay(field.Kind == FieldKind.Lookup ? 420 : 180, cancellationToken);
                return;
            }
            if (!string.IsNullOrWhiteSpace(before))
                throw new InvalidOperationException($"ERP 欄位「{field.Label}」目前已有內容；為避免覆蓋既有值已停止。");
        }

        InputSender.UnicodeText(value);
        await Delay(100, cancellationToken);
        InputSender.Press(NativeMethods.VK_TAB);
        await Delay(field.Kind == FieldKind.Lookup ? 420 : 220, cancellationToken);
    }

    private async Task FillDetailsAsync(nint root, IReadOnlyList<DetailRow> rows, IProgress<string> progress, CancellationToken cancellationToken)
    {
        if (!Win32Automation.PrepareForeground(root, _log))
            throw new InvalidOperationException("輸入明細前無法把 ERP 帶到前景。");

        var grid = FindDetailGrid(root) ?? throw new InvalidOperationException("找不到 ERP 商品明細 TcxGridSite。");

        // COPI08 does not expose the real first detail row until the user first clicks
        // the blank detail area. Build 4 OCR'd too early and therefore saw headers such
        // as 單位/庫別 but no 品號/數量. Reproduce the real ERP interaction first,
        // then refresh the grid and perform optical geometry analysis.
        await PrimeDetailGridAsync(root, grid, cancellationToken);
        (grid, var geometry) = await AnalyzePrimedDetailGridAsync(root, grid, cancellationToken);
        if (geometry.RowCenterY.Count == 0)
            throw new InvalidOperationException("光學辨識沒有找到任何可輸入的明細列。");

        // The priming click creates/activates the first row. Once optical geometry is
        // available, click the exact item-code cell again before entering its editor.
        await ActivateDetailFirstRowAsync(root, grid, geometry, cancellationToken);

        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress.Report($"ERP：輸入商品明細 {rowIndex + 1}/{rows.Count}…");

            if (rowIndex > 0)
            {
                if (!Win32Automation.PrepareForeground(root, _log))
                    throw new InvalidOperationException("開啟下一筆明細前無法把 ERP 帶到前景。");
                InputSender.Press(NativeMethods.VK_DOWN);
                await Delay(360, cancellationToken);
                _log.Info("detail", $"next row activated by Down logical_row={rowIndex + 1}");
            }

            var visibleRow = Math.Min(rowIndex, geometry.RowCenterY.Count - 1);
            if (visibleRow < 0)
                throw new InvalidOperationException("光學明細列位置異常；已停止以避免輸入錯列。");

            var row = rows[rowIndex];
            await SetDetailCellAsync(root, grid, geometry, visibleRow, 0, row.ItemCode, cancellationToken);

            if (!string.IsNullOrWhiteSpace(row.Unit))
                await SelectUnitAsync(root, grid, geometry, visibleRow, row.Unit, cancellationToken);

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

    private async Task PrimeDetailGridAsync(nint root, WindowControl grid, CancellationToken cancellationToken)
    {
        if (!Win32Automation.PrepareForeground(root, _log))
            throw new InvalidOperationException("啟用商品明細區前無法把 ERP 帶到前景。");
        if (!NativeMethods.GetWindowRect(grid.Handle, out var rect) || rect.Width <= 0 || rect.Height <= 45)
            throw new InvalidOperationException("商品明細區目前沒有有效畫面位置。");

        // This reproduces the proven first-row position from the Go implementation:
        // first item column at ~5.2% of the TcxGridSite width, first row center at y+33.
        // It is only used to materialize the ERP row; all actual field writes still use
        // OCR-derived geometry and editor/focus verification.
        var relativeX = (int)Math.Round(rect.Width * 0.052);
        relativeX = Math.Clamp(relativeX, 28, Math.Max(28, rect.Width - 24));
        var relativeY = Math.Clamp(33, 24, Math.Max(24, rect.Height - 12));
        var point = new Point(rect.Left + relativeX, rect.Top + relativeY);
        InputSender.Click(point);
        await Delay(320, cancellationToken);

        var focus = NativeMethods.FocusedControlOfForeground(root);
        _log.Info("detail", $"blank-grid prime click point={point.X},{point.Y} focus=0x{focus:X}/{NativeMethods.ClassName(focus)}");
    }

    private async Task<(WindowControl Grid, GridGeometry Geometry)> AnalyzePrimedDetailGridAsync(
        nint root,
        WindowControl initialGrid,
        CancellationToken cancellationToken)
    {
        Exception? last = null;
        var grid = initialGrid;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Win32Automation.PrepareForeground(root, _log))
                throw new InvalidOperationException("辨識商品明細前無法把 ERP 帶到前景。");

            grid = FindDetailGrid(root) ?? grid;
            try
            {
                var geometry = await _gridVision.AnalyzeDetailGridAsync(grid.Handle, cancellationToken);
                _log.Info("detail", $"geometry ready after grid prime attempt={attempt} rows={geometry.RowCenterY.Count} columns={geometry.ColumnX.Count}");
                return (grid, geometry);
            }
            catch (InvalidOperationException ex) when (attempt < 3 && ex.Message.StartsWith("Optical detail", StringComparison.Ordinal))
            {
                last = ex;
                _log.Warn("detail", $"geometry not ready after grid prime attempt={attempt}: {ex.Message}");
                await Delay(220, cancellationToken);
            }
        }

        throw new InvalidOperationException("已點擊商品明細區，但等待第一列建立後仍無法辨識品號／數量欄位。", last);
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

    private async Task ActivateDetailFirstRowAsync(nint root, WindowControl grid, GridGeometry geometry, CancellationToken cancellationToken)
    {
        if (!Win32Automation.PrepareForeground(root, _log))
            throw new InvalidOperationException("啟用商品明細第一列前無法把 ERP 帶到前景。");
        var (_, point, _) = await ResolveFreshDetailCellPointAsync(grid.Handle, geometry, 0, 0, cancellationToken);
        InputSender.Click(point);
        await Delay(240, cancellationToken);
        var focus = NativeMethods.FocusedControlOfForeground(root);
        _log.Info("detail", $"first-row activation point={point.X},{point.Y} focus=0x{focus:X}/{NativeMethods.ClassName(focus)}");
    }

    private async Task SetDetailCellAsync(nint root, WindowControl grid, GridGeometry geometry, int visibleRow, int column, string value, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(value)) return;

        for (var attempt = 1; attempt <= 2; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Win32Automation.PrepareForeground(root, _log))
                throw new InvalidOperationException("輸入明細前無法把 ERP 帶到前景。");

            var (_, point, freshRect) = await ResolveFreshDetailCellPointAsync(grid.Handle, geometry, visibleRow, column, cancellationToken);
            InputSender.Click(point);
            await Delay(180, cancellationToken);
            InputSender.Press(NativeMethods.VK_RETURN);
            var editor = await WaitGridEditorAsync(root, freshRect, cancellationToken);
            if (editor == 0)
            {
                var focus = NativeMethods.FocusedControlOfForeground(root);
                _log.Warn("detail", $"editor not ready row={visibleRow + 1} col={column} attempt={attempt} point={point.X},{point.Y} focus=0x{focus:X}/{NativeMethods.ClassName(focus)}");
                continue;
            }

            _log.Info("detail", $"editor ready row={visibleRow + 1} col={column} attempt={attempt} point={point.X},{point.Y} edit=0x{editor:X}/{NativeMethods.ClassName(editor)}");
            InputSender.UnicodeText(value, 35);
            await Delay(150, cancellationToken);
            InputSender.Press(NativeMethods.VK_RETURN);
            await Delay(300, cancellationToken);
            _log.Info("detail", $"cell committed row={visibleRow + 1} col={column} chars={value.Length}");
            return;
        }

        throw new InvalidOperationException($"ERP 明細 row={visibleRow + 1}, column={column} 連續兩次都沒有進入編輯模式。");
    }

    private async Task SelectUnitAsync(nint root, WindowControl grid, GridGeometry geometry, int visibleRow, string unit, CancellationToken cancellationToken)
    {
        if (!Win32Automation.PrepareForeground(root, _log))
            throw new InvalidOperationException("開啟單位 F2 前無法把 ERP 帶到前景。");
        var (_, point, _) = await ResolveFreshDetailCellPointAsync(grid.Handle, geometry, visibleRow, 4, cancellationToken);
        InputSender.Click(point);
        await Delay(140, cancellationToken);
        InputSender.Press(NativeMethods.VK_F2);

        var lookup = await WaitLookupAsync(cancellationToken);
        if (lookup == 0) throw new InvalidOperationException("按 F2 後沒有出現單位查詢視窗。");
        if (!Win32Automation.PrepareForeground(lookup, _log))
            throw new InvalidOperationException("F2 單位查詢視窗無法取得前景。");
        await Delay(120, cancellationToken);

        var unitPoint = await _gridVision.FindUnitAsync(lookup, unit, cancellationToken);
        InputSender.Click(unitPoint);
        await Delay(120, cancellationToken);

        var lookupFocus = NativeMethods.FocusedControlOfForeground(lookup);
        var lookupFocusClass = NativeMethods.ClassName(lookupFocus);
        if (lookupFocus == 0 || !lookupFocusClass.Equals("TcxGridSite", StringComparison.OrdinalIgnoreCase))
        {
            _log.Warn("detail", $"F2 row click did not focus grid first_try class={lookupFocusClass}");
            InputSender.Click(unitPoint);
            await Delay(120, cancellationToken);
            lookupFocus = NativeMethods.FocusedControlOfForeground(lookup);
            lookupFocusClass = NativeMethods.ClassName(lookupFocus);
        }
        if (lookupFocus == 0 || !lookupFocusClass.Equals("TcxGridSite", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("已光學點到指定單位，但 F2 表格沒有取得焦點；為避免 Enter 送到錯誤控制項已停止。");

        InputSender.Press(NativeMethods.VK_RETURN);
        if (!await WaitWindowClosedAsync(lookup, 1400, cancellationToken))
            throw new InvalidOperationException("已光學點選指定單位並送出 Enter，但 F2 視窗仍未關閉。");

        if (!Win32Automation.PrepareForeground(root, _log))
            throw new InvalidOperationException("F2 關閉後無法回到 ERP 前景。");
        await Delay(120, cancellationToken);
        _log.Info("detail", "F2 unit selected by OCR click + verified TcxGridSite focus + physical Enter");
    }

    private async Task<(GridGeometry Geometry, Point Point, Rectangle GridRect)> ResolveFreshDetailCellPointAsync(
        nint gridHwnd,
        GridGeometry geometry,
        int visibleRow,
        int column,
        CancellationToken cancellationToken)
    {
        if (!NativeMethods.GetWindowRect(gridHwnd, out var fresh) || fresh.Width <= 0 || fresh.Height <= 0)
            throw new InvalidOperationException("商品明細 grid 目前沒有有效畫面位置。");

        var activeGeometry = geometry;
        if (Math.Abs(fresh.Width - geometry.ScreenRect.Width) > 3 || Math.Abs(fresh.Height - geometry.ScreenRect.Height) > 3)
        {
            _log.Info("detail", $"grid size changed; reanalyzing old={geometry.ScreenRect.Width}x{geometry.ScreenRect.Height} new={fresh.Width}x{fresh.Height}");
            activeGeometry = await _gridVision.AnalyzeDetailGridAsync(gridHwnd, cancellationToken);
        }

        if (!activeGeometry.TryCellPoint(visibleRow, column, out var capturedPoint))
            throw new InvalidOperationException($"光學明細定位缺少 column={column}, row={visibleRow + 1}。");

        if (!NativeMethods.GetWindowRect(gridHwnd, out fresh) || fresh.Width <= 0 || fresh.Height <= 0)
            throw new InvalidOperationException("商品明細 grid 在點擊前失去有效畫面位置。");

        var point = new Point(
            capturedPoint.X + fresh.Left - activeGeometry.ScreenRect.Left,
            capturedPoint.Y + fresh.Top - activeGeometry.ScreenRect.Top);
        var rect = fresh.ToRectangle();
        if (!rect.Contains(point))
            throw new InvalidOperationException($"光學明細定位結果落在 grid 外：column={column}, row={visibleRow + 1}。");

        return (activeGeometry, point, rect);
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
