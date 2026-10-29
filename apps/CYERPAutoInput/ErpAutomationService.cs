using System.Globalization;
namespace CYERPAutoInput;

internal sealed class ErpAutomationService
{
    /// <summary>COPI08 detail rows are 22 px high (user, 2026-10-13: it does not change).</summary>
    private const int ProvenDetailRowPitch = FastDetailGridVisionService.ErpDetailRowHeight;
    private const ushort VkLeft = 0x25;
    private const ushort VkRight = 0x27;

    private readonly AppLogger _log;
    private readonly FastDetailGridVisionService _gridVision;
    /// <summary>
    /// Where ERP's editor really opened for each detail row (center Y inside the grid), so the
    /// row pitch is measured instead of assumed: rows were 22 px on the user's PC (2026-10-12)
    /// and the 24 px pitch put the 7th row's click below the row.
    /// </summary>
    private readonly Dictionary<int, int> _editorRowCenter = [];
    private readonly OpticalTextLocator _textVision;
    private HashSet<string> _noBatchItemCodes = new(StringComparer.OrdinalIgnoreCase);
    private readonly F2UnitCellLocator _unitCellLocator;
    private readonly F2BatchCellLocator _batchCellLocator;

    /// <summary>The shared OCR engine (also used to read Shopee order screenshots).</summary>
    internal PaddleOcrService Ocr { get; }

    public ErpAutomationService(AppLogger log)
    {
        _log = log;
        var ocr = new PaddleOcrService(log);
        Ocr = ocr;
        _gridVision = new FastDetailGridVisionService(ocr, log);
        _textVision = new OpticalTextLocator(ocr, log);
        _unitCellLocator = new F2UnitCellLocator(ocr, log);
        _batchCellLocator = new F2BatchCellLocator(ocr, log);
        _ = Task.Run(() => WarmUpOcrAsync(ocr));
    }

    private ErpMode DetectMode(nint root)
    {
        var (mode, total, readOnly, writable) = ReadModeSignal(root);
        if (total > 0)
            _log.Info("state", $"mode signal total={total} readonly={readOnly} writable={writable}");
        return mode;
    }

    /// <summary>The COPI08 the last run filled (to abandon that one only).</summary>
    private nint _lastRoot;

    /// <summary>
    /// The COPI08 to use, as CYERPAutoQuery picks its forms (user, 2026-10-17): the only one;
    /// with several, the first (top of the z-order) in 檢視 with no dialog over it; 0 when none
    /// is open or none of several is usable. <paramref name="count"/> is how many are open.
    /// </summary>
    public nint SelectCopi08(out int count)
    {
        var windows = Win32Automation.FindCopi08Windows();
        count = windows.Count;
        if (windows.Count <= 1) return windows.Count == 1 ? windows[0] : 0;
        return windows.FirstOrDefault(w => NativeMethods.IsWindowEnabled(w) && ReadModeSignal(w).Mode == ErpMode.Browse);
    }

    /// <summary>
    /// The COPI08 to fill: <see cref="SelectCopi08"/>; ERP minimized is restored first, and
    /// with no COPI08 open it is opened from the ERP main window through 作業速查 (user,
    /// 2026-10-17, as CYERPAutoQuery opens INVI02).
    /// </summary>
    private async Task<nint> ResolveCopi08Async(IProgress<string> progress, CancellationToken cancellationToken)
    {
        var root = SelectCopi08(out var count);
        if (count == 0 && Win32Automation.FindMinimizedErp() is var (app, hidden))
        {
            await RestoreErpAsync(app, hidden, cancellationToken);
            root = SelectCopi08(out count);
        }
        if (count == 0)
        {
            progress.Report("ERP：開啟銷貨單建立作業（作業速查）…");
            await OpenCopi08Async(cancellationToken);
            root = SelectCopi08(out count);
            if (count == 0) throw new InvalidOperationException("已從作業速查開啟 COPI08，但找不到銷貨單建立作業視窗。");
        }
        if (root == 0)
            throw new InvalidOperationException($"開著 {count} 個銷貨單建立作業（COPI08），但沒有一個在檢視狀態。\n請把其中一個儲存或取消後再開始。");
        _log.Info("window", $"COPI08 found windows={count} class={NativeMethods.ClassName(root)}");
        return root;
    }

    /// <summary>Minimized ERP: restore its TApplication (then the hidden main form) and wait for the main form to show.</summary>
    private async Task RestoreErpAsync(nint app, nint hidden, CancellationToken cancellationToken)
    {
        _log.Info("window", $"ERP minimized; restoring app_iconic={NativeMethods.IsIconic(app)} main_visible={NativeMethods.IsWindowVisible(hidden)}");
        NativeMethods.ShowWindow(app, NativeMethods.SW_RESTORE);
        if (!await WaitUntilAsync(() => NativeMethods.IsWindowVisible(hidden) && !NativeMethods.IsIconic(hidden), 3000, cancellationToken))
            NativeMethods.ShowWindow(hidden, NativeMethods.SW_RESTORE);
        var shown = await WaitUntilAsync(() => NativeMethods.IsWindowVisible(hidden) && !NativeMethods.IsIconic(hidden), 5000, cancellationToken);
        _log.Info("window", $"ERP main form restored={shown}");
        await Delay(500, cancellationToken); // let the main form finish painting
    }

    /// <summary>
    /// ERP main window → Ribbon 作業速查 (found by OCR) → COPI08 into the search box, read back
    /// from the focused ERP control → Enter; waits up to 30 s for the form (CYERPAutoQuery's
    /// OpenFormAsync).
    /// </summary>
    private async Task OpenCopi08Async(CancellationToken cancellationToken)
    {
        var mains = Win32Automation.FindErpMainWindows();
        if (mains.Count == 0)
            throw new InvalidOperationException("找不到 SMART ERP 主視窗（鼎新電腦Conductor），也沒有開著銷貨單建立作業（COPI08）。\n請先登入 ERP。");
        var main = mains[0];
        _log.Info("window", $"opening COPI08 from main window class={NativeMethods.ClassName(main)} title={_log.Value(NativeMethods.WindowText(main))} count={mains.Count}");
        if (!NativeMethods.IsWindowEnabled(main))
            throw new InvalidOperationException("ERP 主視窗目前有對話框開著，無法開啟銷貨單建立作業。");
        if (!Win32Automation.PrepareForeground(main, _log))
            throw new InvalidOperationException("無法把 ERP 主視窗帶到前景。");
        if (!NativeMethods.GetWindowRect(main, out var window))
            throw new InvalidOperationException("讀不到 ERP 主視窗位置。");

        var ribbon = new Rectangle(window.Left, window.Top, Math.Min(window.Width, 1200), Math.Min(window.Height, 220));
        IReadOnlyList<OcrToken> tokens;
        using (var image = ScreenCapture.Capture(ribbon))
            tokens = await _textVision.RecognizeAsync(image, cancellationToken);
        if (RibbonPrintLocator.FindWord(tokens.Select(t => (t.Text, t.Rect)).ToList(), "作業速查") is not { } label)
        {
            _log.Warn("window", $"OCR did not find 作業速查 tokens={_log.Value(string.Join(" ", tokens.Select(t => t.Text)))}");
            throw new InvalidOperationException("ERP 主視窗找不到「作業速查」。請確認 Ribbon 停在「我的ERP」。");
        }
        var point = new Point(ribbon.Left + label.Left + label.Width / 2, ribbon.Top + label.Top + label.Height / 2);
        if (!Win32Automation.IsAreaShownBy(main, new Rectangle(point.X - 8, point.Y - 8, 16, 16)))
            throw new InvalidOperationException("ERP 主視窗的「作業速查」被其他視窗遮住，已停止。");
        InputSender.Click(point);
        await Delay(600, cancellationToken);

        var focus = NativeMethods.FocusedControlOfForeground();
        if (focus == 0 || Win32Automation.ProcessId(focus) != Win32Automation.ProcessId(main))
            throw new InvalidOperationException("按「作業速查」後游標不在 ERP 的搜尋框，已停止。");
        InputSender.UnicodeText(Win32Automation.Copi08Code);
        await Delay(800, cancellationToken); // let ERP list the matching programs
        var typed = (NativeMethods.ControlText(focus) ?? string.Empty).Trim();
        if (!typed.Equals(Win32Automation.Copi08Code, StringComparison.OrdinalIgnoreCase))
        {
            _log.Warn("window", $"作業速查 read-back len={typed.Length} class={NativeMethods.ClassName(focus)}");
            throw new InvalidOperationException("作業速查的搜尋框讀回內容不是 COPI08，已停止。");
        }
        InputSender.Press(NativeMethods.VK_RETURN);
        _log.Info("window", "作業速查 COPI08 + Enter sent");

        // Opening a program form can take a while on the first run.
        var started = Environment.TickCount64;
        if (!await WaitUntilAsync(() => Win32Automation.FindCopi08Windows().Count > 0, 30000, cancellationToken))
        {
            foreach (var h in Win32Automation.VisibleProcessWindows(main))
            {
                NativeMethods.GetWindowRect(h, out var r);
                _log.Warn("window", $"after-作業速查 window class={NativeMethods.ClassName(h)} title={_log.Value(NativeMethods.WindowText(h))} size={r.Width}x{r.Height}");
            }
            throw new InvalidOperationException("從作業速查開啟銷貨單建立作業逾時（30 秒）。");
        }
        _log.Info("window", $"COPI08 opened after_ms={Environment.TickCount64 - started}");
        await Delay(1500, cancellationToken); // let the new form load its first record
    }

    private async Task<bool> WaitUntilAsync(Func<bool> condition, int timeoutMs, CancellationToken cancellationToken)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            await Delay(60, cancellationToken);
            if (condition()) return true;
        }
        return false;
    }

    /// <summary>Read-only status probe for the UI status tag; never writes to ERP and never logs.</summary>
    public ErpStatusProbe ProbeStatus()
    {
        var root = SelectCopi08(out var count);
        if (root == 0) return new ErpStatusProbe(count, 0, ErpMode.Unknown, ErpOwnerFields.Unreadable);

        var (mode, _, _, _) = ReadModeSignal(root);
        var owner = mode == ErpMode.Input ? ReadOwnerFields(root) : ErpOwnerFields.Unreadable;
        return new ErpStatusProbe(1, root, mode, owner);
    }

    /// <summary>
    /// Reads 交易資料 部門代號 / 業務人員 by WM_GETTEXT (works while the tab is hidden).
    /// A fresh 新增 leaves both empty; an existing document always has both.
    /// </summary>
    private static ErpOwnerFields ReadOwnerFields(nint root)
    {
        try
        {
            var sheet = ErpLayoutResolver.FindTabSheet(root, "交易資料");
            if (sheet == 0) return ErpOwnerFields.Unreadable;
            var department = NativeMethods.ControlText(ErpLayoutResolver.ResolveTabField(sheet, "交易資料", "dept_code").Handle);
            var salesperson = NativeMethods.ControlText(ErpLayoutResolver.ResolveTabField(sheet, "交易資料", "salesperson").Handle);
            return ErpDocumentStateTracker.Combine(department, salesperson);
        }
        catch (InvalidOperationException)
        {
            return ErpOwnerFields.Unreadable;
        }
    }

    private static (ErpMode Mode, int Total, int ReadOnly, int Writable) ReadModeSignal(nint root)
    {
        if (root == 0 || !NativeMethods.GetWindowRect(root, out var rr)) return (ErpMode.Unknown, 0, 0, 0);
        var edits = Win32Automation.EnumerateChildren(root)
            .Where(c => c.Visible && c.ClassName.Equals("TDBEdit", StringComparison.OrdinalIgnoreCase))
            .Where(c => c.Rect.Top - rr.Top is >= 140 and <= 245)
            .ToArray();
        if (edits.Length < 6) return (ErpMode.Unknown, 0, 0, 0);

        var readOnly = 0;
        var writable = 0;
        foreach (var edit in edits)
        {
            var style = NativeMethods.GetWindowLongPtr(edit.Handle, NativeMethods.GWL_STYLE).ToInt64();
            if ((style & NativeMethods.ES_READONLY) != 0) readOnly++; else writable++;
        }

        var mode = writable == 0 && readOnly >= 6 ? ErpMode.Browse
            : writable >= 4 && readOnly >= 2 ? ErpMode.Input
            : ErpMode.Unknown;
        return (mode, edits.Length, readOnly, writable);
    }

    /// <param name="exportSheet">
    /// 全熱感紙列印 (user, 2026-10-26): instead of 直接列印, the saved document is exported with
    /// 轉出至 Excel for the thermal 出貨明細 (<see cref="AutomationRunResult.ExportFile"/>).
    /// </param>
    public async Task<AutomationRunResult> RunAsync(FormSnapshot snapshot, bool autoSave, IProgress<string> progress, CancellationToken cancellationToken,
        bool autoPrint = false, bool exportSheet = false)
    {
        try
        {
            return await FillDocumentAsync(snapshot, autoSave, progress, cancellationToken, autoPrint, exportSheet);
        }
        finally
        {
            RestoreClipboard();
        }
    }

    private async Task<AutomationRunResult> FillDocumentAsync(FormSnapshot snapshot, bool autoSave, IProgress<string> progress, CancellationToken cancellationToken,
        bool autoPrint, bool exportSheet)
    {
        var result = new AutomationRunResult();
        LastSalesOrderNumber = string.Empty;
        _noBatchItemCodes = snapshot.NoBatchItemCodes;
        if (snapshot.Values.TryGetValue("order_type", out var orderType))
            result.SalesOrderType = orderType.Trim();
        var root = await ResolveCopi08Async(progress, cancellationToken);
        _lastRoot = root;

        // Automation uses one stable ERP geometry: always maximize COPI08 instead of
        // maintaining a second windowed coordinate/viewport path.
        NativeMethods.ShowWindow(root, NativeMethods.SW_MAXIMIZE);
        await Delay(220, cancellationToken);
        _log.Info("window", "COPI08 maximize requested before automation");

        if (!Win32Automation.PrepareForeground(root, _log)) throw new InvalidOperationException("無法把 COPI08 帶到前景。");

        progress.Report("ERP：確認新增狀態…");
        var isNewDocument = await EnsureInputModeAsync(root, cancellationToken);

        progress.Report("ERP：輸入表頭…");
        await FillHeaderAsync(root, snapshot, result, cancellationToken);
        await FillTabGroupAsync(root, snapshot, "交易資料", cancellationToken);
        await FillTabGroupAsync(root, snapshot, "送貨資料", cancellationToken);
        await FillTabGroupAsync(root, snapshot, "發票資料(一)", cancellationToken);

        if (snapshot.HandoffBeforeDetails.Length > 0)
        {
            result.HandedOff = snapshot.HandoffBeforeDetails;
            progress.Report("ERP：單頭已輸入，依訂單備註轉人工處理（未輸入明細、未儲存）。");
            _log.Info("automation", $"handed off before details document={_log.Value(result.DocumentKey)} reason={_log.Value(snapshot.HandoffBeforeDetails)}");
            return result;
        }

        if (snapshot.Details.Count > 0)
        {
            progress.Report("ERP：啟用並光學定位商品明細…");
            await FillDetailsAsync(root, snapshot.Details, result, progress, cancellationToken);
        }

        Win32Automation.PrepareForeground(root, _log);
        if (!autoSave)
        {
            progress.Report("ERP：輸入完成；自動儲存未開啟，請人工確認後儲存。");
            _log.Info("automation", $"input completed document={result.DocumentKey} warnings={result.Warnings.Count}; auto-save disabled");
            return result;
        }

        // Warnings (e.g. ERP's stock warning acknowledged with OK) do not block saving; the
        // user decided 2026-10-09 that such documents are saved and listed for review.
        if (!isNewDocument)
            result.SaveSkippedReason = "開始時 ERP 已在輸入狀態且部門代號／業務人員已有資料（可能是修改中的單據），不自動儲存";

        if (result.SaveSkippedReason.Length > 0)
        {
            progress.Report($"ERP：輸入完成；{result.SaveSkippedReason}。");
            _log.Warn("automation", $"auto-save skipped document={result.DocumentKey} new={isNewDocument} warnings={result.Warnings.Count}");
            return result;
        }

        progress.Report("ERP：輸入完成，儲存單據…");
        await SaveDocumentAsync(root, result, snapshot.Details, cancellationToken);
        progress.Report("ERP：已儲存。");
        if (exportSheet)
        {
            progress.Report("ERP：已儲存，轉出 Excel 供出貨明細…");
            await ExportDocumentAsync(root, result, cancellationToken);
            progress.Report(result.ExportFile.Length > 0 ? "ERP：已儲存並轉出 Excel。" : $"ERP：已儲存；{result.PrintProblem}");
        }
        else if (autoPrint)
        {
            progress.Report("ERP：已儲存，直接列印…");
            await PrintDocumentAsync(root, result, cancellationToken);
            progress.Report(result.Printed ? "ERP：已儲存並送出直接列印。" : $"ERP：已儲存；{result.PrintProblem}");
        }
        return result;
    }

    /// <summary>
    /// ERP 列印 → 直接列印 on the saved document (user, 2026-10-11). The Ribbon has no
    /// shortcut for it, so both targets are found by OCR: the 「列印」 label opens the drop-down
    /// and only an item reading 「直接列印」 is clicked. Anything unexpected closes the menu
    /// with Esc and is reported; the saved document is never touched.
    /// </summary>
    private async Task PrintDocumentAsync(nint root, AutomationRunResult result, CancellationToken cancellationToken)
    {
        void Skip(string reason)
        {
            result.PrintProblem = reason;
            _log.Warn("print", $"direct print skipped document={_log.Value(result.DocumentKey)} reason={reason}");
        }

        // ERP keeps working for a while after the save shows browse mode, and how long varies
        // (user, 2026-10-11): 列印 may be greyed out or ignore a click. So the whole
        // 列印 → 直接列印 step is retried until it lands or the time is up.
        var deadline = Environment.TickCount64 + 20000;
        var attempt = 0;
        var lastReason = string.Empty;
        await Delay(500, cancellationToken);
        while (Environment.TickCount64 < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (attempt++ > 0) await Delay(800, cancellationToken);
            var outcome = await TryDirectPrintOnceAsync(root, attempt, cancellationToken);
            if (outcome.Length == 0)
            {
                result.Printed = true;
                _log.Info("print", $"direct print requested document={_log.Value(result.DocumentKey)} attempt={attempt}");
                break;
            }
            lastReason = outcome;
            _log.Info("print", $"direct print attempt={attempt} not yet: {outcome}");
        }
        if (!result.Printed) { Skip($"{lastReason}（重試 {attempt} 次、20 秒），未列印"); return; }

        // Let ERP hand the job to the printer; a window it opens is logged and left alone.
        await Delay(1500, cancellationToken);
        var peers = Win32Automation.FindVisibleProcessPeerWindows(root);
        if (peers.Count > 0) LogPeerWindows("after-direct-print", peers);
    }

    /// <summary>
    /// 全熱感紙列印 (user, 2026-10-26): the saved 銷貨單 exported the way CYERPAutoQuery exports
    /// INVI02 — Ribbon 「Excel」 → 「轉出至 Excel」 (both found by OCR) → 匯出設定: xlsx, 單頭／單身
    /// 全選, 儲存檔案 pasted with a path in Temp → 確定 → 「檢視Excel資料?」 No. Anything unexpected
    /// is reported in <see cref="AutomationRunResult.PrintProblem"/>; the saved document is never touched.
    /// </summary>
    private async Task ExportDocumentAsync(nint root, AutomationRunResult result, CancellationToken cancellationToken)
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "Temp", "erp-export");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"COPI08-{DateTime.Now:yyyyMMdd-HHmmss-fff}.xlsx");
        try
        {
            // ERP keeps working for a while after the save shows browse mode (as for 直接列印).
            await Delay(800, cancellationToken);
            await ExportOnceAsync(root, path, cancellationToken);
            result.ExportFile = path;
            _log.Info("export", $"document exported document={_log.Value(result.DocumentKey)}");
        }
        catch (InvalidOperationException ex)
        {
            result.PrintProblem = $"轉出 Excel 失敗（{ex.Message}），出貨明細未列印";
            _log.Warn("export", $"export failed document={_log.Value(result.DocumentKey)}: {ex.Message}");
            if (Win32Automation.FindVisibleProcessPeerWindows(root).Count > 0) LogPeerWindows("after-export-failure", Win32Automation.FindVisibleProcessPeerWindows(root));
        }
    }

    /// <summary>
    /// 出貨明細 of the 銷貨單 a person has open in COPI08 (user, 2026-10-26): exported the same way;
    /// the workbook's path. Throws <see cref="InvalidOperationException"/> with the reason.
    /// </summary>
    public async Task<string> ExportShownDocumentAsync(IProgress<string> progress, CancellationToken cancellationToken)
    {
        try
        {
            var root = await ResolveCopi08Async(progress, cancellationToken);
            var folder = Path.Combine(AppContext.BaseDirectory, "Temp", "erp-export");
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, $"COPI08-{DateTime.Now:yyyyMMdd-HHmmss-fff}.xlsx");
            progress.Report("ERP：轉出這張銷貨單…");
            await ExportOnceAsync(root, path, cancellationToken);
            return path;
        }
        finally
        {
            RestoreClipboard();
        }
    }

    private async Task ExportOnceAsync(nint root, string path, CancellationToken cancellationToken)
    {
        if (!Win32Automation.PrepareForeground(root, _log)) throw new InvalidOperationException("無法把 COPI08 帶到前景");
        if (Win32Automation.FindVisibleProcessPeerWindows(root).Count > 0) throw new InvalidOperationException("ERP 有其他視窗開著");
        if (DetectMode(root) != ErpMode.Browse) throw new InvalidOperationException("ERP 不在檢視狀態");
        if (!NativeMethods.GetWindowRect(root, out var window)) throw new InvalidOperationException("讀不到 COPI08 視窗位置");
        var before = Win32Automation.VisibleProcessWindows(root).ToHashSet();

        // Ribbon 「Excel」, as CYERPAutoQuery finds it (stable there): each Ribbon group window is
        // read on its own first — the group Excel was found in last time, then the rest — so
        // the label is not mixed with its neighbours' words (user's LOG, 2026-10-26: the whole
        // Ribbon read 「E x 串 查 c e l」); the whole Ribbon (1200 px, as there) is the fallback.
        var labelScreen = await FindRibbonWordAsync(root, window.ToRectangle(), cancellationToken, "Excel", "EXCEL", "Ex")
            ?? throw new InvalidOperationException("Ribbon 上找不到「Excel」（請確認 Ribbon 停在「常用」）");
        InputSender.Click(new Point(labelScreen.Left + labelScreen.Width / 2, labelScreen.Top + labelScreen.Height / 2));
        _log.Info("export", $"ribbon Excel clicked label=({labelScreen.Left},{labelScreen.Top},{labelScreen.Width},{labelScreen.Height})");

        // The drop-down: 「轉出至 Excel」.
        var near = new Rectangle(labelScreen.Left - 120, labelScreen.Top - 60, 480, 260);
        Point? item = null;
        var menuDeadline = Environment.TickCount64 + 3000;
        while (item is null && Environment.TickCount64 < menuDeadline)
        {
            await Delay(250, cancellationToken);
            var areas = Win32Automation.FindProcessPopupsNear(root, near).ToList();
            areas.Add(near);
            foreach (var area in areas)
            {
                using var image = ScreenCapture.CaptureWithLayers(area);
                var tokens = await _textVision.RecognizeAsync(image, cancellationToken);
                if (RibbonPrintLocator.FindWord(tokens.Select(t => (t.Text, t.Rect)).ToList(), "轉出至") is { } r)
                {
                    item = new Point(area.Left + r.Left + r.Width / 2, area.Top + r.Top + r.Height / 2);
                    break;
                }
            }
        }
        if (item is not { } menuItem)
        {
            if (Win32Automation.FindProcessPopupsNear(root, near).Count > 0) InputSender.Press(NativeMethods.VK_ESCAPE);
            throw new InvalidOperationException("Excel 選單沒有出現「轉出至 Excel」");
        }
        InputSender.Click(menuItem);
        _log.Info("export", "轉出至 Excel clicked");

        // 匯出設定: the new window with a file path box.
        nint dialog = 0;
        var dialogDeadline = Environment.TickCount64 + 10000;
        while (dialog == 0 && Environment.TickCount64 < dialogDeadline)
        {
            await Delay(250, cancellationToken);
            dialog = Win32Automation.VisibleProcessWindows(root).FirstOrDefault(h => !before.Contains(h) && PathBox(h) is not null);
        }
        if (dialog == 0) throw new InvalidOperationException("按下轉出至 Excel 後沒有出現匯出設定視窗");
        await Delay(500, cancellationToken);
        LogExportDialog(dialog);
        Win32Automation.PrepareForeground(dialog, _log);
        var children = Win32Automation.EnumerateChildren(dialog).Where(c => c.Visible).ToList();
        var formats = children.Where(c => c.ClassName.Contains("Combo", StringComparison.OrdinalIgnoreCase) &&
            ControlText(c).Contains(".xls", StringComparison.OrdinalIgnoreCase)).ToList();
        if (formats.Count > 0 && !formats.Any(c => ControlText(c).Contains(".xlsx", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("匯出設定的檔案格式不是「Excel 2007以上 (*.xlsx)」，請在 ERP 改好");

        // 全選 on 單頭 and 單身: 儲存設定 keeps whatever someone ticked last.
        if (!NativeMethods.GetWindowRect(dialog, out var d)) throw new InvalidOperationException("讀不到匯出設定視窗位置");
        foreach (var tab in new[] { "單頭", "單身" })
        {
            var tabArea = new Rectangle(d.Left, d.Top, Math.Min(d.Width, 360), d.Height);
            IReadOnlyList<OcrToken> tokens;
            using (var image = ScreenCapture.Capture(tabArea))
                tokens = await _textVision.RecognizeAsync(image, cancellationToken);
            if (RibbonPrintLocator.FindWord(tokens.Select(t => (t.Text, t.Rect)).ToList(), tab) is { } t)
            {
                InputSender.Click(new Point(tabArea.Left + t.Left + t.Width / 2, tabArea.Top + t.Top + t.Height / 2));
                await Delay(400, cancellationToken);
            }
            else _log.Warn("export", $"export tab {tab} not found by OCR");
            if (Button(dialog, c => c.StartsWith("全選", StringComparison.Ordinal)) is { } all)
            {
                InputSender.Click(Center(all));
                _log.Info("export", $"全選 clicked tab={tab}");
                await Delay(300, cancellationToken);
            }
            else _log.Warn("export", $"全選 not found tab={tab}");
        }

        var box = PathBox(dialog) ?? throw new InvalidOperationException("匯出設定視窗找不到「儲存檔案」欄位");
        InputSender.Click(Center(box));
        await PasteTextAsync(box.Handle, path, "export_path", cancellationToken);
        if (!string.Equals((NativeMethods.ControlText(box.Handle) ?? string.Empty).Trim(), path, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("匯出路徑貼上後讀回的內容不一致");
        var ok = Button(dialog, c => c.StartsWith("確定", StringComparison.Ordinal)) ?? throw new InvalidOperationException("匯出設定視窗找不到「確定」");
        InputSender.Click(Center(ok));
        _log.Info("export", "匯出設定 確定 clicked");

        // ERP writes the file and asks 「檢視Excel資料?」: No (Yes opens the file).
        var deadline = Environment.TickCount64 + 60000;
        while (true)
        {
            if (Environment.TickCount64 > deadline) throw new InvalidOperationException("轉出 Excel 後沒有出現「檢視Excel資料?」");
            await Delay(250, cancellationToken);
            if (NativeMethods.IsWindow(dialog) && NativeMethods.IsWindowVisible(dialog)) continue;
            var question = Win32Automation.VisibleProcessWindows(root)
                .Where(h => !before.Contains(h))
                .Select(h => Button(h, c => c is "No" or "否" or "&No" or "否(&N)" || c.StartsWith("否", StringComparison.Ordinal)))
                .FirstOrDefault(b => b is not null);
            if (question is null) continue;
            await Delay(200, cancellationToken);
            InputSender.Click(Center(question));
            _log.Info("export", "檢視Excel資料? answered No");
            break;
        }
        var fileDeadline = Environment.TickCount64 + 15000;
        while (!File.Exists(path) || !NativeMethods.IsWindowEnabled(root))
        {
            if (Environment.TickCount64 > fileDeadline) throw new InvalidOperationException("轉出 Excel 後找不到匯出的檔案");
            await Delay(200, cancellationToken);
        }
    }

    /// <summary>The Ribbon group window Excel was last found in (CYERPAutoQuery learns it the same way).</summary>
    private static string? _excelGroup;

    /// <summary>
    /// Screen box of the first of <paramref name="words"/> on COPI08's Ribbon: group windows
    /// (RibbonGroupBar) one by one, the remembered one first; then the whole Ribbon. Null when none.
    /// </summary>
    private async Task<Rectangle?> FindRibbonWordAsync(nint root, Rectangle window, CancellationToken cancellationToken, params string[] words)
    {
        Rectangle? Find(IReadOnlyList<OcrToken> tokens)
        {
            var list = tokens.Select(t => (t.Text, t.Rect)).ToList();
            foreach (var word in words)
                if (RibbonPrintLocator.FindWord(list, word) is { } r) return r;
            return null;
        }
        var bars = Win32Automation.EnumerateChildren(root)
            .Where(c => c.Visible && c.ClassName.Contains("RibbonGroupBar", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(c => _excelGroup is not null && c.Text.Trim() == _excelGroup)
            .ToList();
        foreach (var bar in bars)
        {
            var area = Rectangle.FromLTRB(bar.Rect.Left, bar.Rect.Top, bar.Rect.Right, bar.Rect.Bottom);
            if (area.Width < 10 || area.Height < 10) continue;
            IReadOnlyList<OcrToken> tokens;
            using (var image = ScreenCapture.Capture(area))
                tokens = await _textVision.RecognizeAsync(image, cancellationToken);
            if (Find(tokens) is not { } g) continue;
            _excelGroup = bar.Text.Trim();
            _log.Info("export", $"ribbon {words[0]} found in group={_log.Value(_excelGroup)}");
            return new Rectangle(area.Left + g.Left, area.Top + g.Top, g.Width, g.Height);
        }
        _log.Info("export", $"ribbon groups={bars.Count} without {words[0]}; reading the whole Ribbon");
        var ribbon = new Rectangle(window.Left, window.Top, Math.Min(window.Width, 1200), Math.Min(window.Height, 220));
        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (attempt > 0) await Delay(800, cancellationToken);
            IReadOnlyList<OcrToken> tokens;
            using (var image = ScreenCapture.Capture(ribbon))
                tokens = await _textVision.RecognizeAsync(image, cancellationToken);
            if (Find(tokens) is { } r) return new Rectangle(ribbon.Left + r.Left, ribbon.Top + r.Top, r.Width, r.Height);
            if (attempt == 2)
                foreach (var t in tokens)
                    _log.Info("export", $"  ribbon token x={t.Rect.Left} y={t.Rect.Top} w={t.Rect.Width} h={t.Rect.Height} text={_log.Value(t.Text)}");
        }
        return null;
    }

    private static string ControlText(WindowControl c) => (NativeMethods.ControlText(c.Handle) ?? c.Text ?? string.Empty).Trim();

    private static Point Center(WindowControl c) => new((c.Rect.Left + c.Rect.Right) / 2, (c.Rect.Top + c.Rect.Bottom) / 2);

    /// <summary>The 儲存檔案 box: an edit showing a file path (C:\… or \\server\…).</summary>
    private static WindowControl? PathBox(nint dialog) =>
        Win32Automation.EnumerateChildren(dialog).Where(c => c.Visible && Win32Automation.IsInputLike(c.ClassName))
            .FirstOrDefault(c => ControlText(c) is var t && t.Length > 3 && (t[1] == ':' || t.StartsWith(@"\\", StringComparison.Ordinal)) &&
                !c.ClassName.Contains("Combo", StringComparison.OrdinalIgnoreCase));

    /// <summary>A button window (Win32, Delphi TButton／TBitBtn, DevExpress) whose caption matches.</summary>
    private static WindowControl? Button(nint window, Func<string, bool> caption) =>
        Win32Automation.EnumerateChildren(window).FirstOrDefault(c => c.Visible &&
            (c.ClassName.Contains("Button", StringComparison.OrdinalIgnoreCase) || c.ClassName.Contains("Btn", StringComparison.OrdinalIgnoreCase)) &&
            caption(c.Text.Replace("&", string.Empty, StringComparison.Ordinal).Trim()));

    /// <summary>For on-site diagnosis: the 匯出設定 window's controls.</summary>
    private void LogExportDialog(nint dialog)
    {
        NativeMethods.GetWindowRect(dialog, out var w);
        _log.Info("export", $"export dialog title={_log.Value(NativeMethods.WindowText(dialog))} class={NativeMethods.ClassName(dialog)}");
        foreach (var c in Win32Automation.EnumerateChildren(dialog).Where(c => c.Visible))
            _log.Info("export", $"  child class={c.ClassName} text={_log.Value(c.Text)} rect={c.Rect.Left - w.Left},{c.Rect.Top - w.Top},{c.Rect.Right - c.Rect.Left}x{c.Rect.Bottom - c.Rect.Top}");
    }

    /// <summary>
    /// One 列印 → 直接列印 try. Returns empty when 直接列印 was clicked, else why not. A drop-down
    /// left open without 直接列印 is closed with Esc only when ERP shows a popup window, so a
    /// stray Esc never reaches COPI08 itself.
    /// </summary>
    private async Task<string> TryDirectPrintOnceAsync(nint root, int attempt, CancellationToken cancellationToken)
    {
        if (!Win32Automation.PrepareForeground(root, _log)) return "無法把 COPI08 帶到前景";
        if (Win32Automation.FindVisibleProcessPeerWindows(root).Count > 0) return "ERP 有其他視窗開著";
        if (DetectMode(root) != ErpMode.Browse) return "ERP 不在檢視狀態";
        if (!NativeMethods.GetWindowRect(root, out var window)) return "讀不到 COPI08 視窗位置";

        // The Ribbon is the top strip of COPI08; 列印 sits in its left part.
        var ribbon = new Rectangle(window.Left, window.Top, Math.Min(window.Width, 1200), Math.Min(window.Height, 220));
        IReadOnlyList<OcrToken> tokens;
        using (var image = ScreenCapture.Capture(ribbon))
            tokens = await _textVision.RecognizeAsync(image, cancellationToken);
        // Logged on the first try: Build 31 found no exact 「列印」 token on the user's PC
        // (2026-10-11) and the log could not show what the OCR read instead.
        if (attempt == 1)
            foreach (var t in tokens)
                _log.Info("print", $"  ribbon token x={t.Rect.Left} y={t.Rect.Top} w={t.Rect.Width} h={t.Rect.Height} text={_log.Value(t.Text)}");
        if (RibbonPrintLocator.FindWord(tokens.Select(t => (t.Text, t.Rect)).ToList(), "列印") is not { } labelRect)
            return "Ribbon 上找不到「列印」";
        var labelScreen = new Rectangle(ribbon.Left + labelRect.Left, ribbon.Top + labelRect.Top, labelRect.Width, labelRect.Height);
        // The drop-down (直接列印 / 預覽列印) opens over the button itself, not only below it:
        // Build 1 on the user's PC read only COPI08's form below 「列印」 and the retry's click
        // on 「列印」 landed on 預覽列印 (2026-10-12). So the area around the label is read, and
        // when ERP shows a popup window there, that window is read.
        var near = new Rectangle(labelScreen.Left - 120, labelScreen.Top - 180, 420, 400);

        // A menu left open by an earlier try is used as it is; clicking 列印 again would close it.
        if (await FindDirectPrintAsync(root, near, attempt, logTokens: false, cancellationToken) is { } already)
        {
            InputSender.Click(already);
            _log.Info("print", $"direct print clicked in a menu already open attempt={attempt}");
            return string.Empty;
        }
        cancellationToken.ThrowIfCancellationRequested();
        InputSender.Click(new Point(labelScreen.Left + labelScreen.Width / 2, labelScreen.Top + labelScreen.Height / 2));
        _log.Info("print", $"ribbon 列印 clicked attempt={attempt} label=({labelScreen.Left},{labelScreen.Top},{labelScreen.Width},{labelScreen.Height})");

        var menuDeadline = Environment.TickCount64 + 3000;
        while (Environment.TickCount64 < menuDeadline)
        {
            await Delay(250, cancellationToken);
            if (await FindDirectPrintAsync(root, near, attempt, logTokens: Environment.TickCount64 >= menuDeadline, cancellationToken) is { } item)
            {
                cancellationToken.ThrowIfCancellationRequested();
                InputSender.Click(item);
                _log.Info("print", $"direct print clicked at ({item.X},{item.Y}) attempt={attempt}");
                return string.Empty;
            }
        }
        if (Win32Automation.FindVisibleProcessPeerWindows(root).Count > 0)
        {
            InputSender.Press(NativeMethods.VK_ESCAPE);
            await Delay(200, cancellationToken);
        }
        return "列印選單沒有出現「直接列印」";
    }

    /// <summary>Screen point of 「直接列印」 in an open 列印 drop-down, or null.</summary>
    private async Task<Point?> FindDirectPrintAsync(nint root, Rectangle near, int attempt, bool logTokens, CancellationToken cancellationToken)
    {
        var popups = Win32Automation.FindProcessPopupsNear(root, near);
        var areas = popups.Count > 0 ? popups.ToList() : [];
        areas.Add(near);
        foreach (var area in areas)
        {
            using var image = ScreenCapture.CaptureWithLayers(area);
            var tokens = await _textVision.RecognizeAsync(image, cancellationToken);
            // 「直接」 alone is enough: the other item is 「預覽列印」.
            if (RibbonPrintLocator.FindWord(tokens.Select(t => (t.Text, t.Rect)).ToList(), "直接") is { } item)
                return new Point(area.Left + item.Left + item.Width / 2, area.Top + item.Top + item.Height / 2);
            if (!logTokens) continue;
            _log.Info("print", $"  menu area=({area.Left},{area.Top},{area.Width},{area.Height}) popup={popups.Contains(area)} attempt={attempt}");
            foreach (var t in tokens)
                _log.Info("print", $"  menu token x={t.Rect.Left} y={t.Rect.Top} w={t.Rect.Width} h={t.Rect.Height} text={_log.Value(t.Text)}");
        }
        return null;
    }

    private static string Compact(string text) => new(text.Where(c => !char.IsWhiteSpace(c)).ToArray());


    /// <summary>
    /// Saves with ERP's F12 shortcut (the Ribbon 儲存 group also holds 取消, so a click is
    /// not used) and confirms ERP returned to browse mode with the same sales order
    /// number. Any ERP message window stops the run untouched.
    /// </summary>
    private async Task SaveDocumentAsync(nint root, AutomationRunResult result, IReadOnlyList<DetailRow> details, CancellationToken cancellationToken)
    {
        if (!Win32Automation.PrepareForeground(root, _log))
            throw new InvalidOperationException("儲存前無法把 COPI08 帶到前景；未儲存。");
        if (Win32Automation.FindVisibleProcessPeerWindows(root).Count > 0)
            throw new InvalidOperationException("儲存前 ERP 已有其他視窗開啟；未按 F12，單據尚未儲存。");
        if (DetectMode(root) != ErpMode.Input)
            throw new InvalidOperationException("儲存前 ERP 不在輸入狀態；未按 F12，請人工確認。");

        cancellationToken.ThrowIfCancellationRequested();
        InputSender.Press(NativeMethods.VK_F12);
        _log.Info("document", $"save requested by F12 document={_log.Value(result.DocumentKey)}");

        // The last detail row is validated on save, so ERP's stock warning can appear here
        // (real test, Build 8: a one-row document). As on a row change it is acknowledged
        // with OK and recorded; if ERP then stays in input mode, F12 is pressed once more.
        var lastRow = details.Count > 0 ? details[^1] : new DetailRow();
        var warningsAcknowledged = 0;
        var deadline = Environment.TickCount64 + 6000;
        while (Environment.TickCount64 < deadline)
        {
            await Delay(150, cancellationToken);
            if (Win32Automation.FindVisibleProcessPeerWindows(root).Count > 0)
            {
                if (warningsAcknowledged >= 2)
                    throw new InvalidOperationException("按 F12 儲存後 ERP 連續出現庫存不足提示；請人工確認單據是否已儲存。");
                await AcknowledgeStockWarningIfShownAsync(root, details.Count, lastRow,
                    "按 F12 儲存後 ERP 出現訊息視窗；程式未代為處理，請人工確認單據是否已儲存。", cancellationToken);
                warningsAcknowledged++;
                AddStockWarning(result, details.Count, lastRow);
                Win32Automation.PrepareForeground(root, _log);
                await Delay(600, cancellationToken);
                if (Win32Automation.FindVisibleProcessPeerWindows(root).Count == 0 && DetectMode(root) == ErpMode.Input)
                {
                    InputSender.Press(NativeMethods.VK_F12);
                    _log.Info("document", $"save requested again by F12 after stock warning document={_log.Value(result.DocumentKey)}");
                }
                deadline = Environment.TickCount64 + 6000;
                continue;
            }

            if (DetectMode(root) != ErpMode.Browse) continue;
            var number = (NativeMethods.ControlText(ErpLayoutResolver.ResolveSalesOrderNumber(root).Handle) ?? string.Empty).Trim();
            if (result.SalesOrderNumber.Length > 0 && number != result.SalesOrderNumber)
                throw new InvalidOperationException("ERP 已回到檢視狀態，但顯示的單號與輸入時取得的不同；請人工確認。");
            result.Saved = true;
            _log.Info("document", $"saved and verified document={_log.Value(result.DocumentKey)}");
            return;
        }
        throw new InvalidOperationException("按 F12 儲存後 6 秒內未確認 ERP 回到檢視狀態；請人工確認單據是否已儲存。");
    }

    private void LogPeerWindows(string context, IReadOnlyList<nint> peers)
    {
        foreach (var peer in peers)
        {
            NativeMethods.GetWindowRect(peer, out var r);
            _log.Info("window", $"peer window context={context} class={NativeMethods.ClassName(peer)} title={_log.Value(NativeMethods.WindowText(peer))} size={r.Width}x{r.Height}");
        }
    }

    /// <summary>Sales order number of the document the last run worked on (empty before one is read).</summary>
    public string LastSalesOrderNumber { get; private set; } = string.Empty;

    /// <summary>
    /// ERP's own "放棄本次新增／修改": focus a header field (客戶代號; never the detail grid,
    /// where Esc only leaves the cell), press Esc, confirm ERP's 「是否放棄…」 with Enter (OK is
    /// the default) and verify COPI08 is back in browse mode. With
    /// <paramref name="expectedSalesOrderNumber"/> it only abandons that document.
    /// Allowed uses (PROJECT_RULES §1): a user-confirmed restart, and a batch document CY
    /// itself created that failed.
    /// </summary>
    public async Task AbandonInputAsync(string? expectedSalesOrderNumber, CancellationToken cancellationToken)
    {
        // The COPI08 this program just filled; otherwise (a user-confirmed restart) the only one.
        var windows = Win32Automation.FindCopi08Windows();
        var root = windows.Contains(_lastRoot) ? _lastRoot : windows.Count == 1 ? windows[0] : 0;
        if (root == 0) throw new InvalidOperationException("找不到要放棄的那個 COPI08 視窗；未執行放棄，請人工處理。");
        if (!Win32Automation.PrepareForeground(root, _log))
            throw new InvalidOperationException("放棄前無法把 COPI08 帶到前景。");
        if (Win32Automation.FindVisibleProcessPeerWindows(root).Count > 0)
            throw new InvalidOperationException("ERP 有其他視窗開著；未執行放棄，請人工處理。");
        if (DetectMode(root) != ErpMode.Input)
            throw new InvalidOperationException("ERP 不在新增／修改狀態，不需要放棄。");
        if (!string.IsNullOrEmpty(expectedSalesOrderNumber))
        {
            var shown = (NativeMethods.ControlText(ErpLayoutResolver.ResolveSalesOrderNumber(root).Handle) ?? string.Empty).Trim();
            if (shown != expectedSalesOrderNumber)
                throw new InvalidOperationException("ERP 目前的單號與本程式建立的單據不同；未執行放棄，請人工處理。");
        }

        var customer = ErpLayoutResolver.ResolveHeader(root, "customer_code");
        var center = new Point((customer.Rect.Left + customer.Rect.Right) / 2, (customer.Rect.Top + customer.Rect.Bottom) / 2);
        InputSender.Click(center);
        if (await WaitFocusedEditAsync(root, customer.Rect, cancellationToken, 800) == 0)
            throw new InvalidOperationException("無法把焦點放到客戶代號欄；未執行放棄，請人工處理。");
        await Delay(150, cancellationToken);
        InputSender.Press(NativeMethods.VK_ESCAPE);

        nint confirm = 0;
        var deadline = Environment.TickCount64 + 2500;
        while (confirm == 0 && Environment.TickCount64 < deadline)
        {
            await Delay(120, cancellationToken);
            confirm = Win32Automation.FindVisibleProcessPeerWindows(root).FirstOrDefault();
        }
        if (confirm == 0)
            throw new InvalidOperationException("按 Esc 後 ERP 沒有出現「是否放棄」確認；未放棄，請人工處理。");

        var isAbandonPrompt = false;
        for (var attempt = 1; attempt <= 3 && !isAbandonPrompt; attempt++)
        {
            if (attempt > 1) await Delay(300, cancellationToken);
            isAbandonPrompt = Win32Automation.WindowTreeContainsText(confirm, "放棄") ||
                              await _textVision.ContainsAllFragmentsAsync(confirm, ["放棄"], cancellationToken);
        }
        if (!isAbandonPrompt)
        {
            LogPeerWindows("abandon", [confirm]);
            throw new InvalidOperationException("按 Esc 後 ERP 出現的不是「是否放棄」確認；程式未代為處理，請人工確認。");
        }

        if (!Win32Automation.PrepareForeground(confirm, _log))
            throw new InvalidOperationException("無法取得「是否放棄」確認視窗焦點；請人工處理。");
        InputSender.Press(NativeMethods.VK_RETURN);
        if (!await WaitWindowClosedAsync(confirm, 1500, cancellationToken))
            throw new InvalidOperationException("已對「是否放棄」按 Enter，但確認視窗沒有關閉；請人工處理。");

        deadline = Environment.TickCount64 + 3500;
        while (Environment.TickCount64 < deadline)
        {
            await Delay(150, cancellationToken);
            if (DetectMode(root) == ErpMode.Browse)
            {
                _log.Info("document", $"input abandoned expected_sales_no={_log.Value(expectedSalesOrderNumber)}");
                return;
            }
        }
        throw new InvalidOperationException("已確認放棄，但 ERP 沒有回到檢視狀態；請人工確認。");
    }

    private async Task WarmUpOcrAsync(PaddleOcrService ocr)
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

    /// <summary>
    /// Puts COPI08 into input mode. Returns true when the document is known to be a new
    /// one: CY pressed 新增 (F5) itself, or ERP was already in input mode with 部門代號 and
    /// 業務人員 both empty (an existing document always has both).
    /// </summary>
    /// <summary>
    /// This run opened the document itself with F5 (a brand-new one): no field of it can hold a
    /// person's data yet, so text read in a field is replaced instead of stopping. ERP's control
    /// text can be stale: 發票號碼 read as filled while blank on screen, and every MO店+ order
    /// of the run failed on it (user LOG and screenshot 2026-10-19).
    /// </summary>
    private bool _createdByF5;

    /// <summary>Set while 銷貨單別 is entered again when ERP gave no sales order number (it is always typed).</summary>
    private bool _typeOrderType;

    /// <summary>客戶代號 typed instead of pasted (the retry when ERP cleared the pasted one).</summary>
    private bool _typeCustomer;

    private async Task<bool> EnsureInputModeAsync(nint root, CancellationToken cancellationToken)
    {
        _createdByF5 = false;
        var mode = DetectMode(root);
        if (mode == ErpMode.Input)
        {
            var owner = ReadOwnerFields(root);
            var isNew = owner == ErpOwnerFields.BothEmpty;
            _log.Info("state", $"ERP already in input mode at start owner_fields={owner} new_document={isNew}");
            return isNew;
        }
        if (mode != ErpMode.Browse) throw new InvalidOperationException("無法可靠判斷 ERP 目前是檢視或輸入狀態。");
        if (Win32Automation.FindVisibleProcessPeerWindows(root).Count > 0)
            throw new InvalidOperationException("ERP 目前有其他視窗開啟；未按 F5 新增。");

        // ERP's own 新增 shortcut in browse mode; no Ribbon coordinates involved.
        InputSender.Press(NativeMethods.VK_F5);
        _createdByF5 = true;
        _log.Info("state", "new document requested by F5");

        var deadline = Environment.TickCount64 + 3500;
        var checks = 0;
        while (Environment.TickCount64 < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Delay(120, cancellationToken);
            checks++;
            var peers = Win32Automation.FindVisibleProcessPeerWindows(root);
            if (peers.Count > 0)
            {
                LogPeerWindows("new", peers);
                throw new InvalidOperationException("按 F5 新增後 ERP 出現其他視窗；已停止，請人工確認。");
            }
            mode = DetectMode(root);
            if (mode != ErpMode.Input) continue;

            var owner = ReadOwnerFields(root);
            _log.Info("state", $"ERP entered input mode after_checks={checks} elapsed_ms={checks * 120} owner_fields={owner}");
            if (owner != ErpOwnerFields.BothEmpty)
                throw new InvalidOperationException("按 F5 後 ERP 進入輸入狀態，但部門代號／業務人員不是空白，無法確認是新單據；已停止，請人工確認。");
            return true;
        }
        throw new InvalidOperationException("已按 F5 新增，但等待 3.5 秒後仍無法確認 ERP 進入可輸入狀態。");
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
        {
            try
            {
                result.SalesOrderNumber = await CaptureSalesOrderNumberAsync(root, expectedDate, cancellationToken);
            }
            catch (InvalidOperationException) when (snapshot.Values.TryGetValue("order_type", out var orderType) && !string.IsNullOrWhiteSpace(orderType))
            {
                // No number after a pasted 銷貨單別: typed once more, then waited again.
                _log.Warn("document", "no sales order number after the pasted order type; typing it again");
                var field = FieldCatalog.All.First(f => f.Key == "order_type");
                _typeOrderType = true;
                try { await SetFieldAsync(root, ErpLayoutResolver.ResolveHeader(root, field.Key), field, orderType, cancellationToken); }
                finally { _typeOrderType = false; }
                result.SalesOrderNumber = await CaptureSalesOrderNumberAsync(root, expectedDate, cancellationToken);
            }
        }
        LastSalesOrderNumber = result.SalesOrderNumber;
        await EnsureCustomerCodeAsync(root, snapshot, cancellationToken);
    }

    /// <summary>
    /// 客戶代號 read back once the sales order number is there. A pasted one read back right at
    /// first and was blank later: ERP, still taking the 銷貨單別 typed after its paste failed,
    /// cleared it (user's screenshot 2026-10-20, second MO店+ order). Typed once more; still not
    /// there stops before anything else goes in.
    /// </summary>
    private async Task EnsureCustomerCodeAsync(nint root, FormSnapshot snapshot, CancellationToken cancellationToken)
    {
        if (!snapshot.Values.TryGetValue("customer_code", out var code) || string.IsNullOrWhiteSpace(code)) return;
        var field = FieldCatalog.All.First(f => f.Key == "customer_code");
        for (var attempt = 1; ; attempt++)
        {
            await Delay(150, cancellationToken);
            var shown = (NativeMethods.ControlText(ErpLayoutResolver.ResolveHeader(root, field.Key).Handle) ?? string.Empty).Trim();
            if (string.Equals(shown, code.Trim(), StringComparison.OrdinalIgnoreCase)) return;
            _log.Warn("field", $"customer code reads back differently after the order number attempt={attempt} shown_len={shown.Length}");
            if (attempt >= 2) throw new InvalidOperationException("ERP「客戶代號」輸入後被清空或不同，重打一次仍不對；已停止，請確認 ERP。");
            _typeCustomer = true;
            try { await SetFieldAsync(root, ErpLayoutResolver.ResolveHeader(root, field.Key), field, code, cancellationToken); }
            finally { _typeCustomer = false; }
        }
    }

    private async Task<string> CaptureSalesOrderNumberAsync(nint root, string expectedDate, CancellationToken cancellationToken)
    {
        if (expectedDate.Length == 0)
        {
            // No date on the form: ERP fills today's date itself (user-confirmed); read it back.
            var shownDate = NativeMethods.ControlText(ErpLayoutResolver.ResolveHeader(root, "order_date").Handle) ?? string.Empty;
            if (InputRules.TryNormalizeValidDate(shownDate, out var erpDate)) expectedDate = erpDate;
            _log.Info("document", $"order date taken from ERP date_len={expectedDate.Length}");
        }
        if (expectedDate.Length != 8 || !expectedDate.All(char.IsDigit))
            throw new InvalidOperationException("無法取得單據日期（表單未填，ERP 日期欄也讀不到），無法驗證 ERP 銷貨單號。");
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
            lastDirect = (NativeMethods.ControlText(target.Handle) ?? string.Empty).Trim();
            if (InputRules.IsExpectedSalesOrderNumber(lastDirect, expectedDate))
            {
                _log.Info("document", $"sales order number captured sales_no={_log.Value(lastDirect)} source=win32-text attempts={attempt} elapsed_ms={Environment.TickCount64 - started}");
                return lastDirect;
            }

            if (attempt == 1 || attempt % 3 == 0)
            {
                lastCopied = await CopyControlTextExactlyAsync(target.Handle, cancellationToken);
                if (InputRules.IsExpectedSalesOrderNumber(lastCopied, expectedDate))
                {
                    _log.Info("document", $"sales order number captured sales_no={_log.Value(lastCopied)} source=clipboard attempts={attempt} elapsed_ms={Environment.TickCount64 - started}");
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
            snapshot.ComboOptions.TryGetValue(field.Key, out var options);
            await SetFieldAsync(root, target, field, value, cancellationToken, options);
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

    /// <summary>The clipboard's own content, copied before the first pasted field and put back after the document.</summary>
    private DataObject? _clipboardBefore;
    private bool _clipboardSaved;

    /// <summary>
    /// <paramref name="value"/> pasted into the ERP field or detail cell <paramref name="box"/>
    /// (EM_SETSEL + WM_PASTE, as CYERPAutoQuery fills ERP boxes) — faster than typing it and
    /// past any IME (user, 2026-10-16: 銷貨單別 … 金額). The clipboard is put back once the
    /// document is done; an empty value or a busy clipboard is typed as before.
    /// </summary>
    private async Task PasteTextAsync(nint box, string value, string key, CancellationToken cancellationToken)
    {
        // Keys sent just before (Backspace through SendInput) are still queued while WM_PASTE
        // goes straight in, and cleared what was pasted: 銷貨單別 came out empty and ERP gave
        // no sales order number (user LOG 2026-10-17). So the queue drains first.
        await Delay(120, cancellationToken);
        if (value.Length > 0)
        {
            try
            {
                SaveClipboard();
                Clipboard.SetText(value);
                NativeMethods.SendMessage(box, NativeMethods.EM_SETSEL, nint.Zero, new nint(-1));
                NativeMethods.SendMessage(box, NativeMethods.WM_PASTE, nint.Zero, nint.Zero);
                await Delay(80, cancellationToken);
                var shown = (NativeMethods.ControlText(box) ?? string.Empty).Trim();
                if (shown == value.Trim()) return;
                // Not taken as it is (or shown formatted): typed over the selection instead.
                _log.Info("field", $"paste read back differs; typed instead key={key} expected_len={value.Length} shown_len={shown.Length}");
                NativeMethods.SendMessage(box, NativeMethods.EM_SETSEL, nint.Zero, new nint(-1));
            }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException or ThreadStateException)
            {
                _log.Info("field", $"clipboard busy ({ex.GetType().Name}); typed instead key={key}");
            }
        }
        InputSender.UnicodeText(value);
    }

    /// <summary>A copy of every format on the clipboard (the live object would follow later changes).</summary>
    private void SaveClipboard()
    {
        if (_clipboardSaved) return;
        _clipboardSaved = true;
        if (Clipboard.GetDataObject() is not { } live) return;
        var copy = new DataObject();
        foreach (var format in live.GetFormats(autoConvert: false))
        {
            try
            {
                if (live.GetData(format, autoConvert: false) is { } data) copy.SetData(format, data);
            }
            catch (Exception) { } // a format another program renders lazily may refuse; the rest is kept
        }
        _clipboardBefore = copy;
    }

    private void RestoreClipboard()
    {
        if (!_clipboardSaved) return;
        try
        {
            if (_clipboardBefore is { } saved && saved.GetFormats().Length > 0) Clipboard.SetDataObject(saved, copy: true, retryTimes: 5, retryDelay: 50);
            else Clipboard.Clear();
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException or ThreadStateException)
        {
            _log.Info("field", "clipboard not restored");
        }
        _clipboardBefore = null;
        _clipboardSaved = false;
    }

    private async Task SetFieldAsync(nint root, WindowControl target, FieldDefinition field, string value, CancellationToken cancellationToken, IReadOnlyList<string>? options = null)
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
            await SelectComboAsync(root, target, field, value, options, cancellationToken);
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
            await PasteTextAsync(focus, value, field.Key, cancellationToken);
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
            var editorBefore = NativeMethods.WindowText(focus).Trim();
            var targetBefore = NativeMethods.WindowText(target.Handle).Trim();
            var targetIsLookupDbEdit = field.Kind == FieldKind.Lookup &&
                                       target.ClassName.Equals("TDBEdit", StringComparison.OrdinalIgnoreCase);
            var before = targetIsLookupDbEdit ? targetBefore : editorBefore;

            // A field that reads back the form value is still retyped: ERP's control text can
            // be stale (it reported 貨運別 "11" and 送貨地址 text while both were blank on
            // screen), and only tabbing through left the field empty (user, 2026-10-11).
            var sameValue = before == value;
            if (sameValue)
            {
                InputSender.EndBackspace(before.Length + 2);
                await Delay(80, cancellationToken);
                _log.Info("field", $"retyping a field that already reads the form value key={field.Key}");
            }

            if (targetIsLookupDbEdit && targetBefore.Length == 0 && editorBefore.Length > 0)
                _log.Info("field", $"lookup target is blank; ignored retained inner-editor text key={field.Key} editor_class={NativeMethods.ClassName(focus)} editor_len={editorBefore.Length}");

            if (!sameValue && !string.IsNullOrWhiteSpace(before))
            {
                // ERP fills these from the customer master; the form value is meant to replace it.
                if (!CustomerDerivedKeys.Contains(field.Key) && !_createdByF5)
                    throw new InvalidOperationException($"ERP 欄位「{field.Label}」目前已有內容；為避免覆蓋既有值已停止。");
                InputSender.EndBackspace(before.Length + 2);
                await Delay(80, cancellationToken);
                // Code points help explain values that look blank on screen (diagnostic mode only).
                var codes = string.Join(" ", before.Take(12).Select(c => ((int)c).ToString("X4")));
                _log.Info("field", $"replacing ERP value key={field.Key} old_len={before.Length} old={_log.Value(before)} codes={_log.Value(codes)}");
            }
        }

        var imeClosedBefore = InputSender.ImeClosedCount;
        if (field.Key == "order_type" || (field.Key == "customer_code" && _typeCustomer))
        {
            // 銷貨單別 is always typed (user's LOG, 2026-10-28: a paste that read back right left ERP
            // without a sales order number in about one order of five, each waiting 3.5 s and
            // typing it again; typed, the number came at once). Three characters cost little.
            await Delay(120, cancellationToken);
            InputSender.UnicodeText(value);
        }
        else await PasteTextAsync(focus, value, field.Key, cancellationToken);
        if (InputSender.ImeClosedCount != imeClosedBefore) _log.Info("field", $"ERP IME was open; switched off before typing key={field.Key}");
        await Delay(100, cancellationToken);
        InputSender.Press(NativeMethods.VK_TAB);
        await Delay(field.Kind == FieldKind.Lookup ? 420 : 220, cancellationToken);

        if (field.Key == "tax_id") await AcknowledgeTaxIdWarningIfShownAsync(root, cancellationToken);

        // Free-text fields must read back exactly; anything else means characters went
        // astray (IME composition, field length limit) and the next field must not get them.
        if (ReadBackKeys.Contains(field.Key))
        {
            var after = NativeMethods.ControlText(target.Handle)?.Trim();
            if (after is null) _log.Warn("field", $"read-back unavailable key={field.Key}");
            else if (after != value.Trim())
            {
                _log.Warn("field", $"read-back mismatch key={field.Key} expected_len={value.Length} actual_len={after.Length} actual={_log.Value(after)}");
                throw new InvalidOperationException(
                    $"ERP 欄位「{field.Label}」輸入後讀回與表單不同（表單 {value.Length} 字，ERP {after.Length} 字）；可能被輸入法攔住或超過欄位長度，已停止，請確認 ERP 內容。");
            }
        }
    }

    /// <summary>
    /// ERP checks 統一編號 when it is left and answers a valid but unchecked one (e.g. a
    /// non-profit) with 「統一編號輸入錯誤！」, focus on OK; that is expected and only
    /// acknowledged with Enter (user, 2026-10-11). Any other message stops the run.
    /// </summary>
    private async Task AcknowledgeTaxIdWarningIfShownAsync(nint root, CancellationToken cancellationToken)
    {
        nint dialog = 0;
        var deadline = Environment.TickCount64 + 900;
        while (dialog == 0 && Environment.TickCount64 < deadline)
        {
            await Delay(100, cancellationToken);
            dialog = Win32Automation.FindVisibleProcessPeerWindows(root).FirstOrDefault();
        }
        if (dialog == 0) return;

        var isTaxIdWarning = false;
        for (var attempt = 1; attempt <= 3 && !isTaxIdWarning; attempt++)
        {
            if (attempt > 1) await Delay(250, cancellationToken);
            isTaxIdWarning = Win32Automation.WindowTreeContainsText(dialog, "統一編號") ||
                             await _textVision.ContainsAllFragmentsAsync(dialog, ["錯誤"], cancellationToken);
        }
        if (!isTaxIdWarning)
        {
            LogPeerWindows("tax-id", [dialog]);
            throw new InvalidOperationException("輸入統一編號後 ERP 跳出未預期的訊息；程式未代為處理，請人工確認。");
        }
        if (!Win32Automation.PrepareForeground(dialog, _log))
            throw new InvalidOperationException("無法取得「統一編號輸入錯誤」視窗焦點；請人工處理。");
        InputSender.Press(NativeMethods.VK_RETURN);
        if (!await WaitWindowClosedAsync(dialog, 1500, cancellationToken))
            throw new InvalidOperationException("已對「統一編號輸入錯誤」按 Enter，但視窗沒有關閉；請人工處理。");
        _log.Info("field", "ERP tax-id check warning acknowledged with OK (expected for e.g. non-profit buyers)");
        Win32Automation.PrepareForeground(root, _log);
        await Delay(150, cancellationToken);
    }

    /// <summary>Free-text fields that ERP keeps as typed, verified after leaving them.</summary>
    private static readonly HashSet<string> ReadBackKeys =
        new(["trade_note", "ship_addr1", "inv_name", "freight_type"], StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Fields ERP may pre-fill from the customer on a new document (付款條件, 貨運別,
    /// 送貨地址(一), and the invoice 客戶全名／統一編號); a different form value replaces them
    /// instead of stopping. 送貨地址(一) carries the tracking number: Build 15 stopped on it
    /// although it looked blank in ERP (user, 2026-10-11).
    /// </summary>
    private static readonly HashSet<string> CustomerDerivedKeys =
        new(["payment_terms", "freight_type", "ship_addr1", "inv_name", "tax_id"], StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Selects a COPI08 drop-down value. With a known option list (ERP order) the list is
    /// opened, moved to the top with Up and down to the option's position, and confirmed
    /// with Enter; otherwise the value is typed as before. The text ERP then shows is read
    /// back (Win32, then OCR of the field) and a different value stops the run.
    /// </summary>
    private async Task SelectComboAsync(nint root, WindowControl target, FieldDefinition field, string value,
        IReadOnlyList<string>? options, CancellationToken cancellationToken)
    {
        var center = new Point((target.Rect.Left + target.Rect.Right) / 2, (target.Rect.Top + target.Rect.Bottom) / 2);
        var index = options?.ToList().FindIndex(o => InputRules.ComboShowsOption(o, value) || InputRules.ComboShowsOption(value, o)) ?? -1;

        if (!await ComboAlreadyShowsAsync(target, value, cancellationToken))
        {
            InputSender.Click(new Point(Math.Max(target.Rect.Left + 4, target.Rect.Right - 9), center.Y));
            await Delay(220, cancellationToken);
            if (index >= 0)
            {
                for (var i = 0; i < options!.Count; i++) { InputSender.Press(NativeMethods.VK_UP); await Delay(35, cancellationToken); }
                for (var i = 0; i < index; i++) { InputSender.Press(NativeMethods.VK_DOWN); await Delay(35, cancellationToken); }
            }
            else
            {
                InputSender.UnicodeText(value);
            }
            await Delay(80, cancellationToken);
            InputSender.Press(NativeMethods.VK_RETURN);
            await Delay(260, cancellationToken);
        }

        var shown = await ReadComboTextAsync(target, cancellationToken);
        _log.Info("field", $"combo selected key={field.Key} list_index={index} shown={_log.Value(shown)}");
        if (!InputRules.ComboShowsOption(shown, value))
            throw new InvalidOperationException($"ERP 欄位「{field.Label}」選取後顯示「{shown}」，與指定的「{value}」不符；已停止，請人工確認。");
    }

    private async Task<bool> ComboAlreadyShowsAsync(WindowControl target, string value, CancellationToken cancellationToken) =>
        InputRules.ComboShowsOption(await ReadComboTextAsync(target, cancellationToken), value);

    /// <summary>The text a TcxDBImageComboBox shows: its inner edit via WM_GETTEXT, else OCR of the field.</summary>
    private async Task<string> ReadComboTextAsync(WindowControl target, CancellationToken cancellationToken)
    {
        var inner = Win32Automation.EnumerateChildren(target.Handle)
            .FirstOrDefault(c => c.ClassName.Contains("InnerEdit", StringComparison.OrdinalIgnoreCase));
        var text = (NativeMethods.ControlText(inner?.Handle ?? target.Handle) ?? string.Empty).Trim();
        if (text.Length > 0) return text;

        if (!NativeMethods.GetWindowRect(target.Handle, out var rect) || rect.Width <= 0 || rect.Height <= 0) return string.Empty;
        using var image = ScreenCapture.Capture(rect.ToRectangle());
        var tokens = await _textVision.RecognizeAsync(image, cancellationToken);
        return string.Concat(tokens.OrderBy(t => t.Rect.Left).Select(t => t.Text)).Trim();
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
            throw new InvalidOperationException("辨識沒有找到任何可輸入的明細列。");

        // Bundles (user, 2026-10-12): their parts take ERP's own list price, read back here, and
        // the gap to what each bundle sold for goes to the 促銷組合 line after them.
        var bundleList = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        var bundleSold = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        var gapRow = rows.ToList().FindIndex(r => r.TakesBundleGap);
        var lastBundleRow = rows.ToList().FindLastIndex(r => r.BundleKey.Length > 0);
        if (lastBundleRow >= 0 && gapRow < lastBundleRow)
            throw new InvalidOperationException("表單有套裝品項，但促銷組合那一列不在套裝品項之後（或被刪除）；套裝與原價的差額無處可放，未開始輸入，請檢查表單。");

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
                await HandleRowTransitionDialogsAsync(root, grid, geometry, result, rowIndex, rows[rowIndex - 1], cancellationToken);
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
            if (row.BundleKey.Length > 0 && string.IsNullOrWhiteSpace(row.UnitPrice))
            {
                var listPrice = await ReadDetailUnitPriceAsync(root, grid, geometry, visibleRow, row.ItemCode, cancellationToken);
                var quantity = decimal.Parse(row.Quantity, NumberStyles.Number, CultureInfo.InvariantCulture);
                bundleList[row.BundleKey] = bundleList.GetValueOrDefault(row.BundleKey) + listPrice * quantity;
                if (decimal.TryParse(row.BundleTotal, NumberStyles.Number, CultureInfo.InvariantCulture, out var sold))
                    bundleSold[row.BundleKey] = bundleSold.GetValueOrDefault(row.BundleKey) + sold;
                _log.Info("detail", $"bundle part row={visibleRow + 1} item={_log.Value(row.ItemCode)} list_price={listPrice} quantity={quantity} bundle={_log.Value(row.BundleKey)}");
            }
            var unitPrice = row.UnitPrice;
            if (row.TakesBundleGap && bundleList.Count > 0)
            {
                var missing = bundleList.Keys.Where(k => !bundleSold.ContainsKey(k)).ToList();
                if (missing.Count > 0)
                    throw new InvalidOperationException($"套裝 {string.Join("、", missing)} 少了套裝售價；未輸入促銷組合，請人工處理此單。");
                // A bundle sold above its parts' list prices is for a person (user, 2026-10-12).
                var dearer = bundleList.Where(p => p.Value < bundleSold[p.Key]).ToList();
                if (dearer.Count > 0)
                    throw new InvalidOperationException("套裝售價高於 ERP 原價合計，轉人工處理：" +
                        string.Join("、", dearer.Select(p => $"{p.Key}（原價合計 {p.Value:0.##}，售價 {bundleSold[p.Key]:0.##}）")) + "。");
                var gap = bundleList.Sum(p => p.Value - bundleSold[p.Key]);
                var basePrice = decimal.Parse(row.UnitPrice.Length > 0 ? row.UnitPrice : "0", NumberStyles.Number, CultureInfo.InvariantCulture);
                unitPrice = (basePrice - gap).ToString("0.####", CultureInfo.InvariantCulture);
                _log.Info("detail", $"促銷組合 takes bundle gap={gap} base={basePrice} price={unitPrice}");
            }
            if (!string.IsNullOrWhiteSpace(unitPrice))
                geometry = await SetDetailCellAsync(root, grid, geometry, visibleRow, 7, unitPrice, cancellationToken);

            geometry = await SelectBatchIfRequiredAsync(root, grid, geometry, visibleRow, row.ItemCode, cancellationToken);
        }
    }

    private async Task PrimeDetailGridAsync(nint root, WindowControl grid, CancellationToken cancellationToken)
    {
        if (!Win32Automation.PrepareForeground(root, _log))
            throw new InvalidOperationException("啟用商品明細區前無法把 ERP 帶到前景。");
        if (!NativeMethods.GetWindowRect(grid.Handle, out var rect) || rect.Width <= 0 || rect.Height <= 45)
            throw new InvalidOperationException("商品明細區目前沒有有效畫面位置。");

        // The 品號 cell of the first row, found from the header (user, 2026-10-13: a fixed
        // spot missed on another user's layout). The old spot only when the header is not read.
        var cell = await _gridVision.FindFirstItemCellAsync(grid.Handle, cancellationToken);
        var relativeX = cell?.X ?? Math.Clamp((int)Math.Round(rect.Width * 0.052), 28, Math.Max(28, rect.Width - 24));
        var relativeY = Math.Clamp(cell?.Y ?? 33, 24, Math.Max(24, rect.Height - 12));
        var point = new Point(rect.Left + relativeX, rect.Top + relativeY);
        if (!Win32Automation.PrepareForeground(root, _log))
            throw new InvalidOperationException("啟用商品明細區前無法把 ERP 帶到前景。");
        InputSender.Click(point);
        await Delay(320, cancellationToken);

        var focus = NativeMethods.FocusedControlOfForeground(root);
        _log.Info("detail", $"first-row prime click once from_header={cell.HasValue} point={point.X},{point.Y} focus=0x{focus:X}/{NativeMethods.ClassName(focus)}");
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
                _editorRowCenter.Clear();
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

        // A third, later try (user's PC, 2026-10-13: after 品號 + Enter the 數量 cell twice did
        // not open while ERP was still busy with the item; the same order went through when run
        // again), each retry waiting longer first.
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (attempt > 1) await Delay(500 * attempt, cancellationToken);
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
                NativeMethods.GetWindowRect(focus, out var focusRect);
                _log.Warn("detail", $"editor not ready row={visibleRow + 1} col={column} attempt={attempt} point={point.X},{point.Y} focus=0x{focus:X}/{NativeMethods.ClassName(focus)} focus_rect={focusRect.Left},{focusRect.Top},{focusRect.Width}x{focusRect.Height} grid_top={freshRect.Top}");
                continue;
            }
            if (NativeMethods.GetWindowRect(editor, out var editorRect) && editorRect.Height > 0)
                _editorRowCenter[visibleRow] = (editorRect.Top + editorRect.Bottom) / 2 - freshRect.Top;

            _log.Info("detail", $"editor ready row={visibleRow + 1} col={column} attempt={attempt} point={point.X},{point.Y} edit=0x{editor:X}/{NativeMethods.ClassName(editor)}");
            await Delay(90, cancellationToken);
            await PasteTextAsync(editor, value, $"detail col={column}", cancellationToken);
            await Delay(140, cancellationToken);
            // Every detail cell is committed by physical Enter. This is the validated
            // grid behavior and is intentionally different from the upper form fields.
            InputSender.Press(NativeMethods.VK_RETURN);
            await Delay(280, cancellationToken);
            _log.Info("detail", $"cell committed by Enter row={visibleRow + 1} col={column} chars={value.Length}");
            return geometry;
        }

        throw new InvalidOperationException($"ERP 明細 row={visibleRow + 1}, column={column} 連續三次都沒有進入編輯模式。");
    }

    /// <summary>
    /// The 單價 ERP filled in for a bundle part (its list price): the cell's editor is opened and
    /// its text read (WM_GETTEXT, else OCR of the editor), then Enter leaves it unchanged.
    /// Nothing is guessed: a price that cannot be read stops the run.
    /// </summary>
    private async Task<decimal> ReadDetailUnitPriceAsync(nint root, WindowControl grid, GridGeometry geometry, int visibleRow, string itemCode, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Win32Automation.PrepareForeground(root, _log))
                throw new InvalidOperationException("讀取 ERP 單價前無法把 ERP 帶到前景。");
            var (_, point, freshRect) = await ResolveFreshDetailCellPointAsync(grid.Handle, geometry, visibleRow, 7, cancellationToken);
            InputSender.Click(point);
            await Delay(150, cancellationToken);
            var editor = await WaitGridEditorAsync(root, freshRect, point, cancellationToken, 600);
            if (editor == 0)
            {
                InputSender.Press(NativeMethods.VK_RETURN);
                editor = await WaitGridEditorAsync(root, freshRect, point, cancellationToken, 720);
            }
            if (editor == 0) { await Delay(250, cancellationToken); continue; }

            var text = (NativeMethods.ControlText(editor) ?? string.Empty).Trim();
            if (text.Length == 0 && NativeMethods.GetWindowRect(editor, out var rect) && rect.Width > 0 && rect.Height > 0)
            {
                using var image = ScreenCapture.Capture(rect.ToRectangle());
                text = string.Concat((await _textVision.RecognizeAsync(image, cancellationToken)).OrderBy(t => t.Rect.Left).Select(t => t.Text)).Trim();
            }
            InputSender.Press(NativeMethods.VK_RETURN);
            await Delay(250, cancellationToken);
            _log.Info("detail", $"unit price read row={visibleRow + 1} item={_log.Value(itemCode)} attempt={attempt} text={_log.Value(text)}");
            if (decimal.TryParse(text.Replace(",", string.Empty), NumberStyles.Number, CultureInfo.InvariantCulture, out var price) && price > 0)
                return price;
            await Delay(300, cancellationToken);
        }
        throw new InvalidOperationException($"讀不到套裝品項 {itemCode} 在 ERP 帶出的單價；未繼續，請人工處理此單。");
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

        // Prefer the exact OCR match for the 單位 header so the grid-line fallback
        // cannot replace a verified unit X with the preceding 贈/備品量 interval.
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
            _log.Warn("vision", $"F2 whole-window PaddleOCR missed requested unit; trying isolated-cell OCR target=\"{_log.Value(unit)}\"");
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
        // 折價券／運費／促銷組合 lines never have a batch; their F2 check only waited ~2 s each
        // (user, 2026-10-11).
        if (_noBatchItemCodes.Contains(itemCode.Trim()))
        {
            _log.Info("detail", $"batch not required row={visibleRow + 1} item={_log.Value(itemCode)} reason=settings-item-without-batch");
            return geometry;
        }
        geometry = await EnsureDetailColumnVisibleAsync(root, grid, geometry, visibleRow, 5, cancellationToken);
        if (!Win32Automation.PrepareForeground(root, _log))
            throw new InvalidOperationException("檢查批號欄前無法把 ERP 帶到前景。");

        var (activeGeometry, point, freshRect) = await ResolveFreshDetailCellPointAsync(grid.Handle, geometry, visibleRow, 5, cancellationToken);
        geometry = activeGeometry;

        var (markerState, foregroundRatio) = InspectBatchMarkerVisual(freshRect, point);
        _log.Info("detail", $"batch marker visual row={visibleRow + 1} item={_log.Value(itemCode)} state={markerState} foreground_ratio={foregroundRatio:F4}");
        if (markerState == BatchMarkerVisualState.Blank)
        {
            _log.Info("detail", $"batch not required row={visibleRow + 1} item={_log.Value(itemCode)} reason=visually-blank-batch-cell");
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
        // ERP's own F2 response is the final signal: a lot-managed item opens the batch
        // lookup, an item without lots opens nothing. The visual check above only skips F2
        // for clearly blank cells; on the active row every cell shows a "…" editor button,
        // which reads as a marker (real test, V0.2.0 Build 2: an item without lots).
        var lookup = await WaitLookupAsync(cancellationToken, 2000);
        if (lookup == 0)
        {
            var peers = Win32Automation.FindVisibleProcessPeerWindows(root);
            if (peers.Count > 0)
            {
                LogPeerWindows("batch-f2", peers);
                throw new InvalidOperationException($"品號 {itemCode}：按批號 F2 後 ERP 出現非批號查詢的視窗；已停止，請人工確認。");
            }
            _log.Info("detail", $"batch not required row={visibleRow + 1} item={_log.Value(itemCode)} reason=no-f2-lookup marker_state={markerState}");
            return geometry;
        }

        _log.Info("detail", $"batch lookup opened row={visibleRow + 1} item={_log.Value(itemCode)} marker_state={markerState}");
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
        _log.Info("detail", $"batch selected row={visibleRow + 1} item={_log.Value(itemCode)} lookup_row={selection.RowNumber} positive_stock={selection.Stock}");
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

    /// <summary>
    /// Handles ERP messages after Down to the next detail row. 「庫存量或批號量不足」 is
    /// acknowledged with Enter (its default OK) and recorded as a warning, as the user
    /// directed (2026-10-09: just press OK and finish the document). Whether ERP then created
    /// the next row is read from the 序號 column: if it did not, Down is pressed once more,
    /// and a second refusal stops the run. Any other window stops the run.
    /// </summary>
    private async Task HandleRowTransitionDialogsAsync(
        nint root,
        WindowControl grid,
        GridGeometry geometry,
        AutomationRunResult result,
        int completedDetailRow,
        DetailRow completedRow,
        CancellationToken cancellationToken)
    {
        if (!await AcknowledgeStockWarningIfShownAsync(root, completedDetailRow, completedRow, RowChangeUnexpected, cancellationToken)) return;
        AddStockWarning(result, completedDetailRow, completedRow);

        if (!Win32Automation.PrepareForeground(root, _log))
            throw new InvalidOperationException("關閉庫存不足提示後無法回到 COPI08；已停止避免後續錯位。");
        await Delay(200, cancellationToken);

        if (await DetailRowExistsAsync(grid, geometry, completedDetailRow + 1, "after-stock-warning", cancellationToken)) return;

        // ERP stayed in the completed row: ask for the next row once more.
        InputSender.Press(NativeMethods.VK_DOWN);
        await Delay(300, cancellationToken);
        if (await AcknowledgeStockWarningIfShownAsync(root, completedDetailRow, completedRow, RowChangeUnexpected, cancellationToken))
            throw new InvalidOperationException($"第 {completedDetailRow} 列品號 {completedRow.ItemCode}：按確定後 ERP 仍不允許換到下一列（庫存量或批號量不足）；請檢查庫別、數量或批號後人工處理。");
        Win32Automation.PrepareForeground(root, _log);
        if (!await DetailRowExistsAsync(grid, geometry, completedDetailRow + 1, "after-second-down", cancellationToken))
            throw new InvalidOperationException($"第 {completedDetailRow} 列之後 ERP 沒有建立下一列；已停止避免錯位。");
    }

    private const string RowChangeUnexpected = "切換商品明細下一列時 ERP 出現未預期視窗；為避免資料填入錯誤欄位已立即停止目前單據。";

    private void AddStockWarning(AutomationRunResult result, int detailRow, DetailRow row)
    {
        var warning = new AutomationWarning(
            "BATCH_STOCK_INSUFFICIENT",
            result.SalesOrderType,
            result.SalesOrderNumber,
            detailRow,
            row.ItemCode,
            "ERP 顯示庫存量或批號量不足（已按確定繼續），需人工確認");
        result.Warnings.Add(warning);
        _log.Warn("document", $"warning recorded code={warning.Code} document={warning.DocumentKey} detail_row={warning.DetailRow} item={_log.Value(warning.ItemCode)}");
    }

    /// <summary>
    /// Returns false when no ERP window is open; presses Enter on the stock warning and
    /// returns true; throws for any other window. The message text is read from the Win32
    /// tree or, retried while the box is still painting, by OCR.
    /// </summary>
    private async Task<bool> AcknowledgeStockWarningIfShownAsync(
        nint root, int completedDetailRow, DetailRow completedRow, string unexpectedMessage, CancellationToken cancellationToken)
    {
        var peers = Win32Automation.FindVisibleProcessPeerWindows(root);
        if (peers.Count == 0) return false;

        const string knownWarning = "庫存量或批號量不足";
        foreach (var peer in peers)
        {
            var known = false;
            for (var attempt = 1; attempt <= 3 && !known; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (attempt > 1) await Delay(300, cancellationToken);
                known = Win32Automation.WindowTreeContainsText(peer, knownWarning) ||
                        // OCR of ERP's own message box read 「障存量或批就量不足！」 (庫→障, 號→就)
                        // in the real test, so match the stable fragments only.
                        await _textVision.ContainsAllFragmentsAsync(peer, ["存量", "批", "不足"], cancellationToken);
            }
            if (!known) continue;

            if (!Win32Automation.PrepareForeground(peer, _log))
                throw new InvalidOperationException("ERP 庫存不足提示已出現，但無法安全取得提示視窗焦點。");
            InputSender.Press(NativeMethods.VK_RETURN);
            if (!await WaitWindowClosedAsync(peer, 1200, cancellationToken))
                throw new InvalidOperationException("已對庫存不足提示送出 Enter，但提示視窗沒有關閉；已停止避免後續錯位。");
            _log.Info("document", $"stock warning acknowledged detail_row={completedDetailRow} item={_log.Value(completedRow.ItemCode)}");
            return true;
        }

        LogPeerWindows("unexpected", peers);
        throw new InvalidOperationException(unexpectedMessage);
    }

    /// <summary>
    /// Whether ERP has created detail row <paramref name="rowNumber"/>: its 序號 cell shows
    /// glyphs, while the slot of a row that does not exist yet is blank. Reading the number
    /// itself is only logged — the new row is the blue selected row and ERP's slashed zeros
    /// made a whole-column OCR miss it (real test, Build 10).
    /// </summary>
    private async Task<bool> DetailRowExistsAsync(WindowControl grid, GridGeometry geometry, int rowNumber, string context, CancellationToken cancellationToken)
    {
        if (geometry.RowCenterY.Count == 0 || !NativeMethods.GetWindowRect(grid.Handle, out var rect) || rect.Width <= 0 || rect.Height <= 0)
            throw new InvalidOperationException("無法取得商品明細位置，無法確認 ERP 是否已建立下一列；已停止避免錯位。");

        var firstY = _editorRowCenter.TryGetValue(0, out var seenFirst) ? seenFirst : geometry.RowCenterY[0];
        var pitch = DetailRowPitch();
        var maxVisible = Math.Max(0, (int)((rect.Height - firstY - 6) / pitch));
        var slot = Math.Min(rowNumber - 1, maxVisible);
        var centerY = rect.Top + firstY + (int)Math.Round(slot * pitch);
        // 序號 digits sit at x≈13–46; the left edge holds the row indicator (*).
        var cell = Rectangle.FromLTRB(rect.Left + 12, centerY - 10, rect.Left + Math.Min(rect.Width, 48), centerY + 10);

        using var image = ScreenCapture.Capture(cell);
        using var normalized = F2BatchCellLocator.NormalizeCell(image);
        using var glyphs = F2BatchCellLocator.TrimToGlyphs(normalized);
        var exists = glyphs is not null;
        var read = string.Empty;
        if (glyphs is not null)
        {
            var line = await _textVision.RecognizeLineAsync(glyphs, cancellationToken);
            read = InputRules.TryAcceptLowConfidenceStock(line.Chars, out var digits) ? digits : line.Text;
        }
        _log.Info("detail", $"row exists check context={context} row={rowNumber} slot={slot + 1} exists={exists} read={_log.Value(read)}");
        return exists;
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

    /// <summary>
    /// Detail row pitch: measured from the first and the furthest of the first six rows whose
    /// editor has opened in this document, else the long-standing 24 px.
    /// </summary>
    private double DetailRowPitch()
    {
        var known = _editorRowCenter.Where(p => p.Key <= 5).OrderBy(p => p.Key).ToList();
        if (known.Count < 2) return ProvenDetailRowPitch;
        var (first, last) = (known[0], known[^1]);
        var pitch = (last.Value - first.Value) / (double)(last.Key - first.Key);
        // Outside a sane range the measurement is wrong (an editor in another row); keep 22.
        return pitch is >= 16 and <= 36 ? pitch : ProvenDetailRowPitch;
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

            var firstY = _editorRowCenter.TryGetValue(0, out var seenFirst) ? seenFirst : activeGeometry.RowCenterY[0];
            var pitch = DetailRowPitch();
            var maxVisible = Math.Max(0, (int)((activeGeometry.ScreenRect.Height - firstY - 6) / pitch));
            var clickRow = Math.Min(visibleRow, maxVisible);
            var y = firstY + (int)Math.Round(clickRow * pitch);
            capturedPoint = new Point(activeGeometry.ScreenRect.Left + x, activeGeometry.ScreenRect.Top + y);
            _log.Info("detail", $"row point extrapolated logical_row={visibleRow + 1} visible_slot={clickRow + 1} pitch={pitch:0.##}");
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
