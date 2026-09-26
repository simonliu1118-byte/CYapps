namespace CYERPAutoInput;

internal enum ErpMode { Unknown, Browse, Input }

internal sealed class ErpAutomationService
{
    private const int ProvenDetailRowPitch = 24;
    private const ushort VkLeft = 0x25;
    private const ushort VkRight = 0x27;

    private readonly AppLogger _log;
    private readonly FastDetailGridVisionService _gridVision;
    private readonly OpticalTextLocator _textVision;
    private readonly F2UnitCellLocator _unitCellLocator;
    private readonly F2BatchCellLocator _batchCellLocator;

    public ErpAutomationService(AppLogger log)
    {
        _log = log;
        var ocr = new WindowsOcrService(log);
        _gridVision = new FastDetailGridVisionService(ocr, log);
        _textVision = new OpticalTextLocator(ocr, log);
        _unitCellLocator = new F2UnitCellLocator(ocr, log);
        _batchCellLocator = new F2BatchCellLocator(ocr, log);
        _ = Task.Run(() => WarmUpOcrAsync(ocr));
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

    public async Task<AutomationRunResult> RunAsync(FormSnapshot snapshot, IProgress<string> progress, CancellationToken cancellationToken)
    {
        var result = new AutomationRunResult();
        if (snapshot.Values.TryGetValue("order_type", out var orderType))
            result.SalesOrderType = orderType.Trim();
        var root = FindErp();
        if (root == 0) throw new InvalidOperationException("找不到 SMART ERP COPI08 視窗。\n請先開啟銷貨單建立作業。");

        // Build 15 deliberately standardizes ERP geometry: automation always runs
        // against a maximized COPI08 window instead of maintaining a second windowed
        // coordinate/viewport path.
        NativeMethods.ShowWindow(root, NativeMethods.SW_MAXIMIZE);
        await Delay(220, cancellationToken);
        _log.Info("window", "COPI08 maximize requested before automation");

        if (!Win32Automation.PrepareForeground(root, _log)) throw new InvalidOperationException("無法把 COPI08 帶到前景。");

        progress.Report("ERP：確認新增狀態…");
        await EnsureInputModeAsync(root, cancellationToken);

        progress.Report("ERP：輸入表頭…");
        await FillHeaderAsync(root, snapshot, result, cancellationToken);
        await FillTabGroupAsync(root, snapshot, "交易資料", cancellationToken);
        await FillTabGroupAsync(root, snapshot, "送貨資料", cancellationToken);
        await FillTabGroupAsync(root, snapshot, "發票資料(一)", cancellationToken);

        if (snapshot.Details.Count > 0)
        {
            progress.Report("ERP：啟用並光學定位商品明細…");
            await FillDetailsAsync(root, snapshot.Details, result, progress, cancellationToken);
        }

        Win32Automation.PrepareForeground(root, _log);
        progress.Report("ERP：輸入完成；依目前安全規則未自動儲存。");
        _log.Info("automation", $"input completed document={result.DocumentKey} warnings={result.Warnings.Count}; ERP save intentionally not invoked");
        return result;
    }

    private async Task WarmUpOcrAsync(WindowsOcrService ocr)
    {
        try
        {
            using var bitmap = new Bitmap(160, 48);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.White);
                using var font = new Font("Microsoft JhengHei UI", 14F, FontStyle.Regular, GraphicsUnit.Pixel);
                graphics.DrawString("品號", font, Brushes.Black, new PointF(8, 10));
            }
            _ = await ocr.RecognizeAsync(bitmap, CancellationToken.None, requireChinese: true);
            _log.Info("vision", "PaddleOCR background warm-up completed");
        }
        catch (Exception ex)
        {
            _log.Warn("vision", $"PaddleOCR background warm-up skipped: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private async Task EnsureInputModeAsync(nint root, CancellationToken cancellationToken)
    {
        var mode = DetectMode(root);
        if (mode == ErpMode.Input) return;
        if (mode != ErpMode.Browse) throw new InvalidOperationException("無法可靠判斷 ERP 目前是瀏覽或新增狀態。");

        var addPoint = await _textVision.FindTextAsync(root, ["新增"], cancellationToken);
        if (addPoint is null) throw new InvalidOperationException("找不到 ERP 的「新增」按鈕；未進行猜測點擊。");
        InputSender.Click(addPoint.Value);

        var deadline = Environment.TickCount64 + 3500;
        var checks = 0;
        while (Environment.TickCount64 < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Delay(120, cancellationToken);
            checks++;
            mode = DetectMode(root);
            if (mode == ErpMode.Input)
            {
                _log.Info("state", $"ERP entered input mode after_checks={checks} elapsed_ms={checks * 120}");
                return;
            }
        }
        throw new InvalidOperationException("已點擊「新增」，但等待 3.5 秒後仍無法確認 ERP 進入可輸入狀態。");
    }

    private async Task FillHeaderAsync(nint root, FormSnapshot snapshot, AutomationRunResult result, CancellationToken cancellationToken)
    {
        var expectedDate = snapshot.Values.TryGetValue("order_date", out var orderDate)
  ? new string(orderDate.Where(char.IsDigit).ToArray())
  : string.Empty;

        foreach (var field in FieldCatalog.All.Where(f => f.Group == "表頭"))
        {
  if (!snapshot.Values.TryGetValue(field.Key, out var value) || string.IsNullOrWhiteSpace(value)) continue;
  cancellationToken.ThrowIfCancellationRequested();
  var target = ErpLayoutResolver.ResolveHeader(root, field.Key);
  await SetFieldAsync(root, target, field, value, cancellationToken);
  _log.Info("field", $"filled group={field.Group} key={field.Key} chars={value.Length}");
  if (field.Key == "order_date" && string.IsNullOrWhiteSpace(result.SalesOrderNumber))
      result.SalesOrderNumber = await CaptureSalesOrderNumberAsync(root, expectedDate, cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(result.SalesOrderNumber))
  result.SalesOrderNumber = await CaptureSalesOrderNumberAsync(root, expectedDate, cancellationToken);
    }

    private async Task<string> CaptureSalesOrderNumberAsync(nint root, string expectedDate, CancellationToken cancellationToken)
    {
        if (expectedDate.Length != 8 || !expectedDate.All(char.IsDigit))
  throw new InvalidOperationException("單據日期尚未正規化為 8 位 YYYYMMDD，無法驗證 ERP 銷貨單號。");
        if (!Win32Automation.PrepareForeground(root, _log))
  throw new InvalidOperationException("讀取銷貨單號前無法把 COPI08 帶到前景。");

        var deadline = Environment.TickCount64 + 2500;
        var started = Environment.TickCount64;
        var attempt = 0;
        var lastDirect = string.Empty;
        var lastCopied = string.Empty;

        while (Environment.TickCount64 < deadline)
        {
  cancellationToken.ThrowIfCancellationRequested();
  attempt++;
  var target = ErpLayoutResolver.ResolveSalesOrderNumber(root);
  lastDirect = NativeMethods.WindowText(target.Handle).Trim();
  if (IsExpectedSalesOrderNumber(lastDirect, expectedDate))
  {
      _log.Info("document", $"sales order number captured sales_no={lastDirect} source=win32-text attempts={attempt} elapsed_ms={Environment.TickCount64 - started}");
      return lastDirect;
  }

  if (attempt == 1 || attempt % 3 == 0)
  {
      lastCopied = await CopyControlTextExactlyAsync(target.Handle, cancellationToken);
      if (IsExpectedSalesOrderNumber(lastCopied, expectedDate))
      {
          _log.Info("document", $"sales order number captured sales_no={lastCopied} source=clipboard attempts={attempt} elapsed_ms={Environment.TickCount64 - started}");
          return lastCopied;
      }
  }

  await Delay(60, cancellationToken);
        }

        _log.Warn("document", $"sales order number wait timed out expected_date={expectedDate} attempts={attempt} direct_len={lastDirect.Length} copied_len={lastCopied.Length}");
        throw new InvalidOperationException("ERP 已輸入銷貨單別／日期，但等待 2.5 秒後仍無法精確取得符合 YYYYMMDDXXX 的銷貨單號；已停止避免錯單。");
    }

    private async Task<string> CopyControlTextExactlyAsync(nint handle, CancellationToken cancellationToken)
    {
        string previousText = string.Empty;
        var hadText = false;
        var sentinel = $"CYERP_{Guid.NewGuid():N}";
        try
        {
  if (Clipboard.ContainsText())
  {
      previousText = Clipboard.GetText();
      hadText = true;
  }
  Clipboard.SetText(sentinel);
  NativeMethods.SendMessage(handle, NativeMethods.EM_SETSEL, nint.Zero, new nint(-1));
  NativeMethods.SendMessage(handle, NativeMethods.WM_COPY, nint.Zero, nint.Zero);
  await Delay(35, cancellationToken);
  if (!Clipboard.ContainsText()) return string.Empty;
  var copied = Clipboard.GetText().Trim();
  return copied == sentinel ? string.Empty : copied;
        }
        catch (Exception ex)
        {
  _log.Warn("document", $"sales order clipboard copy raised {ex.GetType().Name}");
  return string.Empty;
        }
        finally
        {
  try
  {
      if (hadText) Clipboard.SetText(previousText);
      else Clipboard.Clear();
  }
  catch { }
        }
    }

    private static bool IsExpectedSalesOrderNumber(string value, string expectedDate)
    {
        if (value.Length != 11 || !value.All(char.IsDigit)) return false;
        if (!value.StartsWith(expectedDate, StringComparison.Ordinal)) return false;
        return int.TryParse(value.AsSpan(8, 3), out var sequence) && sequence is >= 1 and <= 999;
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
        if (p is null) throw new InvalidOperationException($"找不到 ERP 頁籤「{group}」。");
        InputSender.Click(p.Value);

        var deadline = Environment.TickCount64 + 1500;
        var stableVisibleChecks = 0;
        while (Environment.TickCount64 < deadline)
        {
  cancellationToken.ThrowIfCancellationRequested();
  if (NativeMethods.IsWindowVisible(sheet))
  {
      stableVisibleChecks++;
      if (stableVisibleChecks >= 3)
      {
          _log.Info("tab", $"activated and stable group={group} checks={stableVisibleChecks}");
          return sheet;
      }
  }
  else
  {
      stableVisibleChecks = 0;
  }
  await Task.Delay(40, cancellationToken);
        }
        throw new InvalidOperationException($"已點擊 ERP 頁籤「{group}」，但頁籤沒有穩定進入可用狀態。");
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

        nint focus = 0;
        for (var focusAttempt = 1; focusAttempt <= 2 && focus == 0; focusAttempt++)
        {
            InputSender.Click(center);
            focus = await WaitFocusedEditAsync(root, target.Rect, cancellationToken, 700);
            if (focus != 0)
            {
                _log.Info("field", $"focus confirmed key={field.Key} attempt={focusAttempt} class={NativeMethods.ClassName(focus)}");
                break;
            }
            _log.Warn("field", $"focus not ready key={field.Key} attempt={focusAttempt}; retrying same verified field");
            await Delay(90, cancellationToken);
        }
        if (focus == 0) throw new InvalidOperationException($"ERP 欄位「{field.Label}」連續兩次都沒有取得可輸入焦點。");

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

    private async Task FillDetailsAsync(nint root, IReadOnlyList<DetailRow> rows, AutomationRunResult result, IProgress<string> progress, CancellationToken cancellationToken)
    {
        if (!Win32Automation.PrepareForeground(root, _log))
            throw new InvalidOperationException("輸入明細前無法把 ERP 帶到前景。");

        var grid = FindDetailGrid(root) ?? throw new InvalidOperationException("找不到 ERP 商品明細 TcxGridSite。");

        // Established COPI08 flow: after every upper section is complete, click the
        // detail area exactly once to materialize the first row. Only then analyze it.
        await PrimeDetailGridAsync(root, grid, cancellationToken);
        (grid, var geometry) = await AnalyzePrimedDetailGridAsync(root, grid, cancellationToken);
        if (geometry.RowCenterY.Count == 0)
            throw new InvalidOperationException("光學辨識沒有找到任何可輸入的明細列。");

        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress.Report($"ERP：輸入商品明細 {rowIndex + 1}/{rows.Count}…");

            if (rowIndex > 0)
            {
                if (!Win32Automation.PrepareForeground(root, _log))
                    throw new InvalidOperationException("開啟下一筆明細前無法把 ERP 帶到前景。");
                InputSender.Press(NativeMethods.VK_DOWN);
                await Delay(180, cancellationToken);
                await HandleRowTransitionDialogsAsync(root, result, rowIndex, rows[rowIndex - 1], cancellationToken);
                await Delay(140, cancellationToken);
                _log.Info("detail", $"next row activated by Down logical_row={rowIndex + 1}");
            }

            var visibleRow = rowIndex;
            var row = rows[rowIndex];

            // Proven COPI08 detail order. Batch is intentionally last because ERP
            // validates lot availability against committed unit / quantity / warehouse.
            geometry = await SetDetailCellAsync(root, grid, geometry, visibleRow, 0, row.ItemCode, cancellationToken);

            if (!string.IsNullOrWhiteSpace(row.Unit))
                geometry = await SelectUnitAsync(root, grid, geometry, visibleRow, row.Unit, cancellationToken);

            geometry = await SetDetailCellAsync(root, grid, geometry, visibleRow, 1, row.Quantity, cancellationToken);
            if (!string.IsNullOrWhiteSpace(row.GiftQuantity))
                geometry = await SetDetailCellAsync(root, grid, geometry, visibleRow, 3, row.GiftQuantity, cancellationToken);
            if (!string.IsNullOrWhiteSpace(row.Warehouse))
                geometry = await SetDetailCellAsync(root, grid, geometry, visibleRow, 6, row.Warehouse, cancellationToken);
            if (!string.IsNullOrWhiteSpace(row.UnitPrice))
                geometry = await SetDetailCellAsync(root, grid, geometry, visibleRow, 7, row.UnitPrice, cancellationToken);

            geometry = await SelectBatchIfRequiredAsync(root, grid, geometry, visibleRow, row.ItemCode, cancellationToken);
        }
    }

    private async Task PrimeDetailGridAsync(nint root, WindowControl grid, CancellationToken cancellationToken)
    {
        if (!Win32Automation.PrepareForeground(root, _log))
            throw new InvalidOperationException("啟用商品明細區前無法把 ERP 帶到前景。");
        if (!NativeMethods.GetWindowRect(grid.Handle, out var rect) || rect.Width <= 0 || rect.Height <= 45)
            throw new InvalidOperationException("商品明細區目前沒有有效畫面位置。");

        var relativeX = (int)Math.Round(rect.Width * 0.052);
        relativeX = Math.Clamp(relativeX, 28, Math.Max(28, rect.Width - 24));
        var relativeY = Math.Clamp(33, 24, Math.Max(24, rect.Height - 12));
        var point = new Point(rect.Left + relativeX, rect.Top + relativeY);
        InputSender.Click(point);
        await Delay(320, cancellationToken);

        var focus = NativeMethods.FocusedControlOfForeground(root);
        _log.Info("detail", $"first-row prime click once point={point.X},{point.Y} focus=0x{focus:X}/{NativeMethods.ClassName(focus)}");
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
                _log.Info("detail", $"geometry ready after first-row prime attempt={attempt} rows={geometry.RowCenterY.Count} columns={geometry.ColumnX.Count}");
                return (grid, geometry);
            }
            catch (InvalidOperationException ex) when (attempt < 3 && ex.Message.StartsWith("Optical detail", StringComparison.Ordinal))
            {
                last = ex;
                _log.Warn("detail", $"geometry not ready after first-row prime attempt={attempt}: {ex.Message}");
                await Delay(220, cancellationToken);
            }
        }

        throw new InvalidOperationException("已點擊商品明細區建立第一列，但仍無法辨識品號／數量欄位。", last);
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

    private async Task<GridGeometry> SetDetailCellAsync(
        nint root,
        WindowControl grid,
        GridGeometry geometry,
        int visibleRow,
        int column,
        string value,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(value)) return geometry;
        geometry = await EnsureDetailColumnVisibleAsync(root, grid, geometry, visibleRow, column, cancellationToken);

        for (var attempt = 1; attempt <= 2; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Win32Automation.PrepareForeground(root, _log))
                throw new InvalidOperationException("輸入明細前無法把 ERP 帶到前景。");

            var (activeGeometry, point, freshRect) = await ResolveFreshDetailCellPointAsync(grid.Handle, geometry, visibleRow, column, cancellationToken);
            geometry = activeGeometry;
            InputSender.Click(point);
            await Delay(120, cancellationToken);

            // A real click may already activate TcxCustomInnerTextEdit. Do not send an
            // extra Enter when the editor is already live: on some COPI08 builds that
            // extra transition leaves the first subsequent character duplicated.
            var editor = await WaitGridEditorAsync(root, freshRect, point, cancellationToken, 180);
            if (editor == 0)
            {
                InputSender.Press(NativeMethods.VK_RETURN);
                editor = await WaitGridEditorAsync(root, freshRect, point, cancellationToken, 720);
            }
            if (editor == 0)
            {
                var focus = NativeMethods.FocusedControlOfForeground(root);
                _log.Warn("detail", $"editor not ready row={visibleRow + 1} col={column} attempt={attempt} point={point.X},{point.Y} focus=0x{focus:X}/{NativeMethods.ClassName(focus)}");
                continue;
            }

            _log.Info("detail", $"editor ready row={visibleRow + 1} col={column} attempt={attempt} point={point.X},{point.Y} edit=0x{editor:X}/{NativeMethods.ClassName(editor)}");
            await Delay(90, cancellationToken);
            InputSender.UnicodeText(value, 35);
            await Delay(140, cancellationToken);
            // Every detail cell is committed by physical Enter. This is the validated
            // grid behavior and is intentionally different from the upper form fields.
            InputSender.Press(NativeMethods.VK_RETURN);
            await Delay(280, cancellationToken);
            _log.Info("detail", $"cell committed by Enter row={visibleRow + 1} col={column} chars={value.Length}");
            return geometry;
        }

        throw new InvalidOperationException($"ERP 明細 row={visibleRow + 1}, column={column} 連續兩次都沒有進入編輯模式。");
    }

    private async Task<GridGeometry> SelectUnitAsync(
        nint root,
        WindowControl grid,
        GridGeometry geometry,
        int visibleRow,
        string unit,
        CancellationToken cancellationToken)
    {
        geometry = await EnsureDetailColumnVisibleAsync(root, grid, geometry, visibleRow, 4, cancellationToken);
        if (!Win32Automation.PrepareForeground(root, _log))
            throw new InvalidOperationException("開啟單位 F2 前無法把 ERP 帶到前景。");

        var (activeGeometry, mappedPoint, freshRect) = await ResolveFreshDetailCellPointAsync(grid.Handle, geometry, visibleRow, 4, cancellationToken);
        geometry = activeGeometry;
        var point = mappedPoint;

        // Prefer the exact OCR match for the 單位 header. Build 14 could enter the
        // grid-line fallback and overwrite a correct unit X with the preceding
        // 贈/備品量 interval. The fast analyzer keeps exact OCR X separately.
        if (_gridVision.TryGetExactColumnX(grid.Handle, 4, out var exactUnitX))
        {
            var exactPoint = new Point(freshRect.Left + exactUnitX, mappedPoint.Y);
            if (freshRect.Contains(exactPoint))
            {
                point = exactPoint;
                _log.Info("detail", $"unit cell uses exact OCR header x={exactUnitX} point={point.X},{point.Y}");
            }
        }

        InputSender.Click(point);
        await Delay(160, cancellationToken);
        InputSender.Press(NativeMethods.VK_F2);

        var lookup = await WaitLookupAsync(cancellationToken);
        if (lookup == 0) throw new InvalidOperationException("已點擊單位欄並按 F2，但沒有出現單位查詢視窗。");
        if (!Win32Automation.PrepareForeground(lookup, _log))
            throw new InvalidOperationException("F2 單位查詢視窗無法取得前景。");
        await Delay(120, cancellationToken);

        Point unitPoint;
        try
        {
            unitPoint = await _gridVision.FindUnitAsync(lookup, unit, cancellationToken);
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("OCR 無法在 F2", StringComparison.Ordinal))
        {
            _log.Warn("vision", $"F2 whole-window PaddleOCR missed requested unit; trying isolated-cell OCR target=\"{unit}\"");
            var fallback = await _unitCellLocator.FindAsync(lookup, unit, cancellationToken);
            if (fallback is null)
                throw;
            unitPoint = fallback.Value;
        }

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
        _log.Info("detail", "F2 unit selected by exact unit-column targeting + optical row click + physical Enter");
        return geometry;
    }


    private enum BatchMarkerVisualState { Blank, Marker, Uncertain }

    private async Task<GridGeometry> SelectBatchIfRequiredAsync(
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

        var (markerState, foregroundRatio) = InspectBatchMarkerVisual(freshRect, point);
        _log.Info("detail", $"batch marker visual row={visibleRow + 1} item={itemCode} state={markerState} foreground_ratio={foregroundRatio:F4}");
        if (markerState == BatchMarkerVisualState.Blank)
        {
  _log.Info("detail", $"batch not required row={visibleRow + 1} item={itemCode} reason=visually-blank-batch-cell");
  return geometry;
        }

        // Marker or uncertain: use ERP's own F2 behavior as the final fallback signal.
        InputSender.Click(point);
        var focus = await WaitGridFocusAsync(root, grid.Handle, cancellationToken, 500);
        if (focus == 0)
        {
  InputSender.Click(point);
  focus = await WaitGridFocusAsync(root, grid.Handle, cancellationToken, 500);
        }
        if (focus == 0)
  throw new InvalidOperationException($"品號 {itemCode}：批號欄點擊後焦點未留在商品明細；已停止避免後續欄位錯位。");

        InputSender.Press(NativeMethods.VK_F2);
        var lookup = await WaitLookupAsync(cancellationToken, 1000);
        if (lookup == 0)
        {
  if (markerState == BatchMarkerVisualState.Marker)
      throw new InvalidOperationException($"品號 {itemCode} 的批號欄明確偵測到批號標記，但按 F2 後沒有出現批號查詢視窗；已停止避免錯位。");

  _log.Info("detail", $"batch not required row={visibleRow + 1} item={itemCode} reason=uncertain-marker-and-no-f2-lookup");
  return geometry;
        }

        _log.Info("detail", $"batch lookup opened row={visibleRow + 1} item={itemCode} marker_state={markerState}");
        if (!Win32Automation.PrepareForeground(lookup, _log))
  throw new InvalidOperationException("F2 批號查詢視窗無法取得前景。");

        var selection = await _batchCellLocator.FindFirstPositiveStockAsync(lookup, cancellationToken);
        InputSender.Click(selection.Point);

        var lookupGrid = Win32Automation.EnumerateChildren(lookup)
  .Where(c => c.Visible && c.ClassName.Equals("TcxGridSite", StringComparison.OrdinalIgnoreCase))
  .OrderByDescending(c => c.Rect.Width * c.Rect.Height)
  .FirstOrDefault();
        if (lookupGrid is null)
  throw new InvalidOperationException("批號 F2 視窗中的表格在選取後無法確認；已停止避免誤送 Enter。");

        var lookupFocus = await WaitGridFocusAsync(lookup, lookupGrid.Handle, cancellationToken, 700);
        if (lookupFocus == 0)
        {
  InputSender.Click(selection.Point);
  lookupFocus = await WaitGridFocusAsync(lookup, lookupGrid.Handle, cancellationToken, 700);
        }
        if (lookupFocus == 0)
  throw new InvalidOperationException("已點到正庫存批號列，但 F2 表格沒有取得可驗證焦點；為避免 Enter 送到錯誤控制項已停止。");

        InputSender.Press(NativeMethods.VK_RETURN);
        if (!await WaitWindowClosedAsync(lookup, 1600, cancellationToken))
  throw new InvalidOperationException("已選取正庫存批號並送出 Enter，但 F2 視窗仍未關閉。");

        if (!Win32Automation.PrepareForeground(root, _log))
  throw new InvalidOperationException("批號 F2 關閉後無法回到 ERP 前景。");
        _log.Info("detail", $"batch selected row={visibleRow + 1} item={itemCode} lookup_row={selection.RowNumber} positive_stock={selection.Stock}");
        return geometry;
    }

    private static (BatchMarkerVisualState State, double ForegroundRatio) InspectBatchMarkerVisual(Rectangle gridRect, Point batchCellPoint)
    {
        var wanted = Rectangle.FromLTRB(batchCellPoint.X - 38, batchCellPoint.Y - 9, batchCellPoint.X + 34, batchCellPoint.Y + 9);
        var crop = Rectangle.Intersect(gridRect, wanted);
        if (crop.Width < 20 || crop.Height < 10)
  return (BatchMarkerVisualState.Uncertain, 1.0);

        using var image = ScreenCapture.Capture(crop);
        var histogram = new Dictionary<int, int>();
        for (var y = 1; y < image.Height - 1; y++)
        for (var x = 1; x < image.Width - 1; x++)
        {
  var c = image.GetPixel(x, y);
  var key = ((c.R >> 4) << 8) | ((c.G >> 4) << 4) | (c.B >> 4);
  histogram[key] = histogram.TryGetValue(key, out var n) ? n + 1 : 1;
        }
        if (histogram.Count == 0) return (BatchMarkerVisualState.Uncertain, 1.0);

        var backgroundKey = histogram.OrderByDescending(p => p.Value).First().Key;
        var br = ((backgroundKey >> 8) & 0xF) * 17 + 8;
        var bg = ((backgroundKey >> 4) & 0xF) * 17 + 8;
        var bb = (backgroundKey & 0xF) * 17 + 8;
        var foreground = 0;
        var total = 0;
        for (var y = 2; y < image.Height - 2; y++)
        for (var x = 2; x < image.Width - 2; x++)
        {
  var c = image.GetPixel(x, y);
  total++;
  var delta = Math.Abs(c.R - br) + Math.Abs(c.G - bg) + Math.Abs(c.B - bb);
  if (delta >= 95) foreground++;
        }

        var ratio = total == 0 ? 1.0 : foreground / (double)total;
        if (ratio <= 0.012) return (BatchMarkerVisualState.Blank, ratio);
        if (ratio >= 0.035) return (BatchMarkerVisualState.Marker, ratio);
        return (BatchMarkerVisualState.Uncertain, ratio);
    }

    private static async Task<nint> WaitGridFocusAsync(nint root, nint gridHandle, CancellationToken cancellationToken, int timeoutMs)
    {
        var stop = Environment.TickCount64 + Math.Max(100, timeoutMs);
        while (Environment.TickCount64 < stop)
        {
  cancellationToken.ThrowIfCancellationRequested();
  var focus = NativeMethods.FocusedControlOfForeground(root);
  if (focus != 0 && Win32Automation.IsInside(focus, gridHandle))
      return focus;
  await Task.Delay(35, cancellationToken);
        }
        return 0;
    }

    private async Task HandleRowTransitionDialogsAsync(
        nint root,
        AutomationRunResult result,
        int completedDetailRow,
        DetailRow completedRow,
        CancellationToken cancellationToken)
    {
        var peers = Win32Automation.FindVisibleProcessPeerWindows(root);
        if (peers.Count == 0) return;

        const string knownWarning = "庫存量或批號量不足";
        foreach (var peer in peers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var known = Win32Automation.WindowTreeContainsText(peer, knownWarning);
            if (!known)
                known = await _textVision.ContainsAllFragmentsAsync(peer, ["庫存量", "批號量", "不足"], cancellationToken);
            if (!known) continue;

            if (!Win32Automation.PrepareForeground(peer, _log))
                throw new InvalidOperationException("ERP 批號存量不足警告已出現，但無法安全取得警告視窗焦點。");
            InputSender.Press(NativeMethods.VK_RETURN);
            if (!await WaitWindowClosedAsync(peer, 1200, cancellationToken))
                throw new InvalidOperationException("已對批號存量不足警告送出 Enter，但警告視窗沒有關閉；已停止避免後續錯位。");

            var warning = new AutomationWarning(
                "BATCH_STOCK_INSUFFICIENT",
                result.SalesOrderType,
                result.SalesOrderNumber,
                completedDetailRow + 1,
                completedRow.ItemCode,
                "批號存量不足，需人工確認");
            result.Warnings.Add(warning);
            _log.Warn("document", $"warning recorded code={warning.Code} document={warning.DocumentKey} detail_row={warning.DetailRow} item={warning.ItemCode}");

            if (!Win32Automation.PrepareForeground(root, _log))
                throw new InvalidOperationException("關閉批號存量不足警告後無法回到 COPI08；已停止避免後續錯位。");
            return;
        }

        throw new InvalidOperationException("切換商品明細下一列時 ERP 出現未預期視窗；為避免資料填入錯誤欄位已立即停止目前單據。");
    }

    private async Task<GridGeometry> EnsureDetailColumnVisibleAsync(
        nint root,
        WindowControl grid,
        GridGeometry geometry,
        int visibleRow,
        int column,
        CancellationToken cancellationToken)
    {
        if (geometry.ColumnX.ContainsKey(column)) return geometry;
        if (!GridVisionService.TryGetLogicalOrder(column, out var targetOrder))
            throw new InvalidOperationException($"明細欄位 column={column} 沒有可用的語意順序。");

        for (var cycle = 1; cycle <= 4; cycle++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var mapped = new List<(int Column, int Order, int X)>();
            foreach (var pair in geometry.ColumnX)
            {
                if (GridVisionService.TryGetLogicalOrder(pair.Key, out var order))
                    mapped.Add((pair.Key, order, pair.Value));
            }

            if (mapped.Count == 0)
            {
                geometry = await _gridVision.AnalyzeDetailGridAsync(grid.Handle, cancellationToken);
                if (geometry.ColumnX.ContainsKey(column)) return geometry;
                continue;
            }

            var minOrder = mapped.Min(x => x.Order);
            var maxOrder = mapped.Max(x => x.Order);
            var moveRight = targetOrder > maxOrder;
            var moveLeft = targetOrder < minOrder;
            if (!moveRight && !moveLeft)
            {
                // OCR may have missed a visible label. Re-read once with the normalized
                // Traditional/Simplified matcher before moving the viewport.
                geometry = await _gridVision.AnalyzeDetailGridAsync(grid.Handle, cancellationToken);
                if (geometry.ColumnX.ContainsKey(column)) return geometry;

                mapped.Clear();
                foreach (var pair in geometry.ColumnX)
                    if (GridVisionService.TryGetLogicalOrder(pair.Key, out var order))
                        mapped.Add((pair.Key, order, pair.Value));
                if (mapped.Count == 0) continue;
                minOrder = mapped.Min(x => x.Order);
                maxOrder = mapped.Max(x => x.Order);
                moveRight = targetOrder >= (minOrder + maxOrder) / 2.0;
                moveLeft = !moveRight;
            }

            if (!Win32Automation.PrepareForeground(root, _log))
                throw new InvalidOperationException("水平捲動商品明細前無法把 ERP 帶到前景。");

            var focus = NativeMethods.FocusedControlOfForeground(root);
            if (focus == 0 || !Win32Automation.IsInside(focus, grid.Handle))
            {
                var anchor = moveRight
                    ? mapped.OrderByDescending(x => x.X).First()
                    : mapped.OrderBy(x => x.X).First();
                var rowSlot = Math.Clamp(visibleRow, 0, Math.Max(0, geometry.RowCenterY.Count - 1));
                if (geometry.TryCellPoint(rowSlot, anchor.Column, out var anchorPoint))
                {
                    InputSender.Click(anchorPoint);
                    await Delay(80, cancellationToken);
                }
            }

            var key = moveRight ? VkRight : VkLeft;
            for (var i = 0; i < 3; i++)
            {
                InputSender.Press(key);
                await Delay(55, cancellationToken);
            }
            await Delay(140, cancellationToken);

            var liveGrid = FindDetailGrid(root) ?? grid;
            geometry = await _gridVision.AnalyzeDetailGridAsync(liveGrid.Handle, cancellationToken);
            _log.Info("detail", $"horizontal viewport moved direction={(moveRight ? "right" : "left")} cycle={cycle} target_col={column} mapped={geometry.ColumnX.Count}");
            if (geometry.ColumnX.ContainsKey(column)) return geometry;
        }

        throw new InvalidOperationException($"ERP 明細 column={column} 目前不在可視範圍，嘗試水平捲動後仍無法定位。");
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

        Point capturedPoint;
        if (!activeGeometry.TryCellPoint(visibleRow, column, out capturedPoint))
        {
            if (visibleRow <= 0 || activeGeometry.RowCenterY.Count == 0 || !activeGeometry.ColumnX.TryGetValue(column, out var x))
                throw new InvalidOperationException($"光學明細定位缺少 column={column}, row={visibleRow + 1}。");

            var firstY = activeGeometry.RowCenterY[0];
            var maxVisible = Math.Max(0, (activeGeometry.ScreenRect.Height - firstY - 6) / ProvenDetailRowPitch);
            var clickRow = Math.Min(visibleRow, maxVisible);
            var y = firstY + clickRow * ProvenDetailRowPitch;
            capturedPoint = new Point(activeGeometry.ScreenRect.Left + x, activeGeometry.ScreenRect.Top + y);
            _log.Info("detail", $"row point extrapolated logical_row={visibleRow + 1} visible_slot={clickRow + 1} pitch={ProvenDetailRowPitch}");
        }

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

    private static async Task<nint> WaitLookupAsync(CancellationToken cancellationToken, int timeoutMs = 2500)
    {
        var stop = Environment.TickCount64 + Math.Max(100, timeoutMs);
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

    private static async Task<nint> WaitFocusedEditAsync(nint root, NativeMethods.RECT targetRect, CancellationToken cancellationToken, int timeoutMs = 800)
    {
        var stop = Environment.TickCount64 + Math.Max(100, timeoutMs);
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

    private static async Task<nint> WaitGridEditorAsync(
        nint root,
        Rectangle gridRect,
        Point expectedPoint,
        CancellationToken cancellationToken,
        int timeoutMs = 950)
    {
        var stop = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < stop)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var focus = NativeMethods.FocusedControlOfForeground(root);
            if (focus != 0 && NativeMethods.ClassName(focus).Contains("EDIT", StringComparison.OrdinalIgnoreCase))
            {
                NativeMethods.GetWindowRect(focus, out var r);
                var rect = r.ToRectangle();
                var center = new Point((r.Left + r.Right) / 2, (r.Top + r.Bottom) / 2);
                if (gridRect.Contains(center) &&
                    (rect.Contains(expectedPoint) ||
                     (Math.Abs(center.Y - expectedPoint.Y) <= 14 && expectedPoint.X >= rect.Left - 8 && expectedPoint.X <= rect.Right + 8)))
                    return focus;
            }
            await Task.Delay(30, cancellationToken);
        }
        return 0;
    }

    private static Task Delay(int milliseconds, CancellationToken cancellationToken) => Task.Delay(milliseconds, cancellationToken);
}
