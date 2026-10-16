using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CYERPAutoQuery;

/// <summary>
/// Looks up one 品號 in SMART ERP 品號資料建立作業 (INVI02) and takes its data from ERP's own
/// 「轉出至 Excel」 export (user, 2026-10-16: cleaner and safer than reading tab by tab).
/// Read-only towards ERP data: it opens the query dialog (F6), sets 一般選項 品號 = code,
/// confirms, shows 基本資料1 and exports; it never presses 新增／修改／刪除／儲存／取消.
/// </summary>
internal sealed class ItemQueryService
{
    private const int MaximizeSettleMs = 300;
    // The first F6 of a session can take more than 3 s (user's PC, 2026-10-16: the dialog
    // showed about 4 s after F6 and the 3 s wait had already given up).
    private const int QueryDialogWaitMs = 10000;
    private const int QueryRunWaitMs = 15000;
    private const int RecordWaitMs = 5000;
    private const int EmptyRecordStableMs = 800;
    private const int TabWaitMs = 1200;
    private const int QuerySettleWaitMs = 3000;
    private const int MenuWaitMs = 3000;
    private const int ExportDialogWaitMs = 10000;
    private const int ExportRunWaitMs = 60000;

    private readonly AppLogger _log;
    private readonly PaddleOcrService _ocr;

    public ItemQueryService(AppLogger log)
    {
        _log = log;
        _ocr = new PaddleOcrService(log);
    }

    public async Task<ItemQueryResult> QueryAsync(string itemCode, ItemQueryOptions options, IProgress<string> progress, CancellationToken cancellationToken)
    {
        if (!ItemCodeRules.TryNormalize(itemCode, out var code, out var error))
            throw new InvalidOperationException(error);

        using var gate = ErpAutomationGate.Enter();
        ExportFiles.CleanUp(_log);
        var watch = Stopwatch.StartNew();
        _log.Info("query", $"start item={_log.Value(code)}");

        progress.Report("ERP：尋找品號資料建立作業…");
        var root = await ResolveInvi02Async(progress, cancellationToken);

        // Same as CYERPAutoInput with COPI08: always maximize so every geometry is the same.
        NativeMethods.ShowWindow(root, NativeMethods.SW_MAXIMIZE);
        await Pace.Delay(MaximizeSettleMs, cancellationToken);
        if (!Win32Automation.PrepareForeground(root, _log))
            throw new InvalidOperationException("無法把品號資料建立作業帶到前景。");

        var page = FindMainPageControl(root);

        progress.Report("ERP：開啟查詢條件（F6）…");
        var dialog = await OpenQueryDialogAsync(root, page, cancellationToken);

        progress.Report("ERP：輸入一般選項品號…");
        await FillGeneralQueryAsync(dialog, code, cancellationToken);

        progress.Report("ERP：查詢中…");
        await ConfirmQueryAsync(root, dialog, cancellationToken);
        var header = await WaitForRecordAsync(root, page, code, cancellationToken);
        if (header is null)
        {
            _log.Info("query", $"not found item={_log.Value(code)} elapsed_ms={watch.ElapsedMilliseconds}");
            return ItemQueryResult.NotFound(code, watch.Elapsed);
        }

        // Exported from 資料瀏覽 the workbook lacks 單身資料 (庫別); from 基本資料1 it has both
        // sheets (user, 2026-10-16).
        // ERP ends a query on 資料瀏覽. When the record asked for was already shown, the header
        // matches before ERP has finished, and 基本資料1 still looked shown at that moment, so
        // it was not clicked and the export came from 資料瀏覽 (V0.2.0 Build 1, 2026-10-16).
        // So: wait for 資料瀏覽 first, then always click 基本資料1.
        progress.Report("ERP：切換到基本資料1…");
        var sheet = Win32Automation.FindTabSheet(root, Invi02Layout.ExportTab)
            ?? throw new InvalidOperationException($"找不到 ERP 頁籤「{Invi02Layout.ExportTab}」。");
        if (Win32Automation.FindTabSheet(root, Invi02Layout.BrowseTab) is { } browseSheet)
        {
            var settled = await WaitUntilAsync(() => NativeMethods.IsWindowVisible(browseSheet.Handle), QuerySettleWaitMs, cancellationToken);
            _log.Info("tab", $"after query 資料瀏覽 shown={settled}");
        }
        await Pace.Delay(300, cancellationToken);
        await ActivateTabAsync(root, page, Invi02Layout.Invi02Tabs, Invi02Layout.ExportTab, sheet.Handle, cancellationToken, force: true);

        progress.Report("ERP：轉出 Excel…");
        var path = ExportFiles.NewPath();
        ItemQueryResult result;
        try
        {
            result = await ExportAndReadAsync(root, header, code, path, watch, progress, cancellationToken);
        }
        catch (InvalidOperationException ex) when (ex is not QueryFailure)
        {
            // Every failure from here on is an export failure for the Web contract.
            throw new QueryFailure(ErpAgentErrorCode.EXPORT_FAILED, ex.Message, ex);
        }
        finally
        {
            ExportFiles.Delete(path, _log);
        }

        // Batches when the item is batch managed; history only when asked (user, 2026-10-16).
        var batchManaged = Invq01Layout.IsBatchManaged(result.Value("批號管理"));
        if (!batchManaged && options.History is null) return result;
        try
        {
            result = await QueryInvq01Async(root, code, result, batchManaged, options.History, progress, cancellationToken);
        }
        catch (InvalidOperationException ex) when (ex is not QueryFailure)
        {
            throw new QueryFailure(ErpAgentErrorCode.EXPORT_FAILED, ex.Message, ex);
        }
        _log.Info("query", $"done with INVQ01 item={_log.Value(code)} batches={result.Batches.Count} history={result.History.Count} elapsed_ms={watch.ElapsedMilliseconds}");
        return result with { Elapsed = watch.Elapsed };
    }

    private async Task<ItemQueryResult> ExportAndReadAsync(nint root, HeaderControls header, string code, string path,
        Stopwatch watch, IProgress<string> progress, CancellationToken cancellationToken)
    {
        // Exporting from any tab but 基本資料1 loses 單身資料; checked again right before.
        var exportSheet = Win32Automation.FindTabSheet(root, Invi02Layout.ExportTab);
        if (exportSheet is null || !NativeMethods.IsWindowVisible(exportSheet.Handle))
            throw new InvalidOperationException("轉出前 ERP 不在「基本資料1」頁籤，已停止（從資料瀏覽轉出會缺少庫別）。");
        await ExportToExcelAsync(root, path, cancellationToken);

        // The record must still be the one asked for after the export.
        if (!ItemCodeRules.SameCode(NativeMethods.ControlText(header.ItemCode.Handle), code))
            throw new InvalidOperationException("轉出途中 ERP 顯示的品號改變了；結果不採用，請重新查詢。");

        progress.Report("讀取匯出資料…");
        var sheets = await ExportFiles.ReadAsync(path, cancellationToken);
        var (fields, warehouses) = Invi02Export.Parse(sheets, code);
        var result = new ItemQueryResult(code, fields, warehouses, watch.Elapsed);
        _log.Info("query", $"done item={_log.Value(code)} fields={fields.Count} warehouses={warehouses.Count} elapsed_ms={watch.ElapsedMilliseconds}");
        return result;
    }

    /// <summary>Read-only status probe for the UI status badge; never writes to ERP and never logs.</summary>
    public Invi02Status ProbeStatus()
    {
        try
        {
            var windows = Win32Automation.FindInvi02Windows();
            if (windows.Count == 0) return Invi02Status.NotOpen;
            // With several, a query uses one in browse state (user, 2026-10-16).
            if (windows.Count > 1) return windows.Any(IsInBrowseState) ? Invi02Status.Browse : Invi02Status.Multiple;
            var root = windows[0];
            if (!NativeMethods.IsWindowEnabled(root)) return Invi02Status.DialogOpen;
            var header = ResolveHeader(root, FindMainPageControl(root));
            if (!header.ItemCode.ClassName.Equals("TDBEdit", StringComparison.OrdinalIgnoreCase)) return Invi02Status.Unknown;
            return Win32Automation.IsReadOnly(header.ItemCode.Handle) ? Invi02Status.Browse : Invi02Status.Editing;
        }
        catch (InvalidOperationException)
        {
            return Invi02Status.Unknown;
        }
    }

    /// <summary>
    /// The INVI02 window to use (user, 2026-10-16): with several, the first (top of the z-order)
    /// in browse state; with none, it is opened from the ERP main window through 作業速查.
    /// </summary>
    private async Task<nint> ResolveInvi02Async(IProgress<string> progress, CancellationToken cancellationToken)
    {
        var windows = Win32Automation.FindInvi02Windows();
        if (windows.Count == 0)
        {
            progress.Report("ERP：開啟品號資料建立作業（作業速查）…");
            await OpenInvi02Async(cancellationToken);
            windows = Win32Automation.FindInvi02Windows();
            if (windows.Count == 0)
                throw new QueryFailure(ErpAgentErrorCode.ERP_NOT_OPEN, "已從作業速查開啟 INVI02，但找不到品號資料建立作業視窗。");
        }

        nint root;
        if (windows.Count == 1) root = windows[0];
        else
        {
            var browse = windows.Where(IsInBrowseState).ToList();
            _log.Info("window", $"INVI02 windows={windows.Count} browse={browse.Count}");
            if (browse.Count == 0)
                throw new QueryFailure(ErpAgentErrorCode.ERP_STATE, $"開著 {windows.Count} 個品號資料建立作業，但沒有一個在檢視狀態。\n請把其中一個儲存或取消後再查詢。");
            root = browse[0];
        }
        _log.Info("window", $"INVI02 found class={NativeMethods.ClassName(root)} visible={NativeMethods.IsWindowVisible(root)}");
        return root;
    }

    /// <summary>Browse state: no dialog over the form and the header 品號 read-only.</summary>
    private static bool IsInBrowseState(nint root)
    {
        try
        {
            if (!NativeMethods.IsWindowEnabled(root)) return false;
            var header = ResolveHeader(root, FindMainPageControl(root));
            return header.ItemCode.ClassName.Equals("TDBEdit", StringComparison.OrdinalIgnoreCase) &&
                   Win32Automation.IsReadOnly(header.ItemCode.Handle);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>
    /// ERP main window → Ribbon 作業速查 (found by OCR, like 「Excel」) → type INVI02 into the search
    /// box → Enter (user's screenshots 2026-10-16). The typed text is read back from the focused
    /// ERP control before Enter, so nothing is typed into another program.
    /// </summary>
    private async Task OpenInvi02Async(CancellationToken cancellationToken)
    {
        var mains = Win32Automation.FindErpMainWindows();
        if (mains.Count == 0)
            throw new QueryFailure(ErpAgentErrorCode.ERP_NOT_OPEN, "找不到 SMART ERP 主視窗（鼎新電腦Conductor），也沒有開著品號資料建立作業。\n請先登入 ERP。");
        var main = mains[0];
        _log.Info("window", $"opening INVI02 from main window class={NativeMethods.ClassName(main)} count={mains.Count}");
        if (!NativeMethods.IsWindowEnabled(main))
            throw new QueryFailure(ErpAgentErrorCode.ERP_STATE, "ERP 主視窗目前有對話框開著，無法開啟品號資料建立作業。");
        if (!Win32Automation.PrepareForeground(main, _log))
            throw new QueryFailure(ErpAgentErrorCode.ERP_STATE, "無法把 ERP 主視窗帶到前景。");
        if (!NativeMethods.GetWindowRect(main, out var window))
            throw new QueryFailure(ErpAgentErrorCode.ERP_STATE, "讀不到 ERP 主視窗位置。");

        var ribbon = new Rectangle(window.Left, window.Top, Math.Min(window.Width, 1200), Math.Min(window.Height, 220));
        IReadOnlyList<OcrToken> tokens;
        using (var image = ScreenCapture.Capture(ribbon))
            tokens = await _ocr.RecognizeAsync(image, cancellationToken, requireChinese: true);
        if (OcrWordFinder.FindWord(tokens, "作業速查") is not { } label)
        {
            _log.Warn("window", $"OCR did not find 作業速查 tokens=\"{string.Join(" ", tokens.Select(t => t.Text))}\"");
            throw new QueryFailure(ErpAgentErrorCode.ERP_STATE, "ERP 主視窗找不到「作業速查」。請確認 Ribbon 停在「我的ERP」。");
        }
        SafeClick(main, new Point(ribbon.Left + label.Left + label.Width / 2, ribbon.Top + label.Top + label.Height / 2));
        await Pace.Delay(600, cancellationToken);

        var focus = NativeMethods.FocusedControlOfForeground();
        if (focus == 0 || Win32Automation.ProcessId(focus) != Win32Automation.ProcessId(main))
            throw new QueryFailure(ErpAgentErrorCode.ERP_STATE, "按「作業速查」後游標不在 ERP 的搜尋框，已停止。");
        InputSender.UnicodeText(Invi02Layout.FormCode);
        await Pace.Delay(800, cancellationToken); // let ERP list the matching programs
        var typed = (NativeMethods.ControlText(focus) ?? string.Empty).Trim();
        if (!typed.Equals(Invi02Layout.FormCode, StringComparison.OrdinalIgnoreCase))
        {
            _log.Warn("window", $"作業速查 read-back len={typed.Length} class={NativeMethods.ClassName(focus)}");
            throw new QueryFailure(ErpAgentErrorCode.ERP_STATE, "作業速查的搜尋框讀回內容不是 INVI02，已停止。");
        }
        InputSender.Press(NativeMethods.VK_RETURN);
        _log.Info("window", "作業速查 INVI02 + Enter sent");

        // Opening a program form can take a while on the first run.
        var started = Environment.TickCount64;
        if (!await WaitUntilAsync(() => Win32Automation.FindInvi02Windows().Count > 0, 30000, cancellationToken))
        {
            LogProcessWindows(main, "after-作業速查");
            throw new QueryFailure(ErpAgentErrorCode.ERP_NOT_OPEN, "從作業速查開啟品號資料建立作業逾時（30 秒）。");
        }
        _log.Info("window", $"INVI02 opened after_ms={Environment.TickCount64 - started}");
        await Pace.Delay(1500, cancellationToken); // let the new form load its first record
    }

    private static WindowControl FindMainPageControl(nint root)
    {
        var first = Win32Automation.FindTabSheet(root, Invi02Layout.ExportTab)
            ?? throw new InvalidOperationException($"找不到 ERP 頁籤「{Invi02Layout.ExportTab}」。");
        var pageHandle = NativeMethods.GetParent(first.Handle);
        return Win32Automation.EnumerateChildren(root).FirstOrDefault(c => c.Handle == pageHandle)
            ?? throw new InvalidOperationException("找不到 ERP 品號資料頁籤的 TcxPageControl。");
    }

    // ---- header -----------------------------------------------------------------------

    private sealed record HeaderControls(WindowControl ItemCode);

    /// <summary>
    /// The header is every input above the tab page control. Its first row starts with 品號,
    /// then 品名 and 規格 rows (checkboxes such as 定重 are left out of that row structure).
    /// </summary>
    private static HeaderControls ResolveHeader(nint root, WindowControl page)
    {
        var inputs = Win32Automation.InputControls(root, visibleOnly: true)
            .Where(c => c.Rect.Bottom <= page.Rect.Top && !Win32Automation.IsInside(c.Handle, page.Handle))
            .ToList();
        var rows = Invi02Layout.BuildRows(
            inputs.Where(c => !Invi02Layout.IsCheckBox(c.ClassName)),
            c => c.Rect.ToRectangle());
        if (rows.Count < 3)
            throw new InvalidOperationException($"ERP 品號資料表頭結構不符預期（只找到 {rows.Count} 列欄位）。");
        return new HeaderControls(rows[0][0]);
    }

    // ---- query dialog -----------------------------------------------------------------

    private async Task<nint> OpenQueryDialogAsync(nint root, WindowControl page, CancellationToken cancellationToken)
    {
        // A dialog left open (by the user, or by an earlier run that gave up) is reused
        // instead of pressing F6 again.
        var open = FindQueryDialogs(root);
        if (open.Count == 1)
        {
            _log.Info("dialog", "query dialog already open; reused");
            return open[0];
        }
        if (open.Count > 1) throw new QueryFailure(ErpAgentErrorCode.ERP_STATE, "ERP 開著多個「設定查詢條件」視窗，請先關閉再查詢。");
        if (!NativeMethods.IsWindowEnabled(root))
            throw new QueryFailure(ErpAgentErrorCode.ERP_STATE, "ERP 品號資料建立作業目前有其他對話框開著，請先處理後再查詢。");

        var header = ResolveHeader(root, page);
        if (header.ItemCode.ClassName.Equals("TDBEdit", StringComparison.OrdinalIgnoreCase) &&
            !Win32Automation.IsReadOnly(header.ItemCode.Handle))
            throw new QueryFailure(ErpAgentErrorCode.ERP_STATE, "ERP 品號資料建立作業目前在新增或修改狀態。\n請先儲存或取消後再查詢。");

        if (!Win32Automation.PrepareForeground(root, _log))
            throw new InvalidOperationException("按 F6 前無法把品號資料建立作業帶到前景。");
        InputSender.Press(NativeMethods.VK_F6);
        _log.Info("dialog", "F6 sent");

        var started = Environment.TickCount64;
        var deadline = started + QueryDialogWaitMs;
        while (Environment.TickCount64 < deadline)
        {
            await Pace.Delay(100, cancellationToken);
            var found = FindQueryDialogs(root);
            if (found.Count == 1)
            {
                _log.Info("dialog", $"query dialog shown after_ms={Environment.TickCount64 - started} class={NativeMethods.ClassName(found[0])}");
                await Pace.Delay(150, cancellationToken); // let the dialog finish painting
                return found[0];
            }
        }
        LogProcessWindows(root, "after-F6");
        throw new QueryFailure(ErpAgentErrorCode.ERP_STATE, "已按 F6，但 10 秒內沒有找到「設定查詢條件」視窗。\n請確認品號資料建立作業在檢視狀態；若視窗已出現，請把 LOG 傳給開發人員。");
    }

    /// <summary>
    /// 設定查詢條件 windows: recognised by their 一般選項 tab, or by the caption. The tab is the
    /// stronger signal (a skinned caption or a dialog parented inside INVI02 would hide from a
    /// caption-only search).
    /// </summary>
    private static IReadOnlyList<nint> FindQueryDialogs(nint root)
    {
        var found = Win32Automation.VisibleProcessWindows(root)
            .Where(h => Invi02Layout.IsQueryDialogTitle(NativeMethods.WindowText(h)) ||
                        Win32Automation.FindTabSheet(h, Invi02Layout.GeneralQueryTab) is not null)
            .ToList();
        if (found.Count == 0 && Win32Automation.FindTabSheet(root, Invi02Layout.GeneralQueryTab) is { } inner)
        {
            // Parented inside INVI02: the dialog is the sheet's ancestor right below root.
            var h = inner.Handle;
            while (NativeMethods.GetParent(h) is var p && p != 0 && p != root) h = p;
            if (NativeMethods.IsWindowVisible(h)) found.Add(h);
        }
        return found;
    }

    /// <summary>For on-site diagnosis: every visible window of the ERP process (captions are ERP UI text, not business data).</summary>
    private void LogProcessWindows(nint root, string context)
    {
        _log.Warn("dialog", $"{context}: root enabled={NativeMethods.IsWindowEnabled(root)}");
        foreach (var h in Win32Automation.VisibleProcessWindows(root))
        {
            NativeMethods.GetWindowRect(h, out var r);
            _log.Warn("dialog", $"{context}: window class={NativeMethods.ClassName(h)} title=\"{NativeMethods.WindowText(h)}\" rect={r.Left},{r.Top},{r.Width}x{r.Height} enabled={NativeMethods.IsWindowEnabled(h)}");
        }
    }

    private async Task FillGeneralQueryAsync(nint dialog, string code, CancellationToken cancellationToken)
    {
        if (!Win32Automation.PrepareForeground(dialog, _log))
            throw new InvalidOperationException("無法把「設定查詢條件」帶到前景。");

        // The dialog remembers the tab used last time (user, 2026-10-16).
        var sheet = Win32Automation.FindTabSheet(dialog, Invi02Layout.GeneralQueryTab)
            ?? throw new InvalidOperationException("「設定查詢條件」找不到「一般選項」頁籤。");
        var pageHandle = NativeMethods.GetParent(sheet.Handle);
        var page = Win32Automation.EnumerateChildren(dialog).FirstOrDefault(c => c.Handle == pageHandle)
            ?? throw new InvalidOperationException("「設定查詢條件」找不到頁籤控制項。");
        await ActivateTabAsync(dialog, page, Invi02Layout.QueryDialogTabs, Invi02Layout.GeneralQueryTab, sheet.Handle, cancellationToken);

        var rows = Invi02Layout.BuildRows(
            Win32Automation.InputControls(sheet.Handle, visibleOnly: true).Where(c => !Invi02Layout.IsCheckBox(c.ClassName)),
            c => c.Rect.ToRectangle());
        if (rows.Count == 0 || rows[0].Count != 2 ||
            !Invi02Layout.IsCombo(rows[0][0].ClassName) || !Invi02Layout.IsEditBox(rows[0][1].ClassName))
        {
            _log.Warn("dialog", $"general tab rows={rows.Count} first=[{string.Join(",", rows.FirstOrDefault()?.Select(c => c.ClassName) ?? [])}]");
            throw new InvalidOperationException("「一般選項」的品號條件結構不符預期（應為：條件下拉 + 輸入框）。");
        }
        if (rows.Count > 1) _log.Warn("dialog", $"general tab has {rows.Count} condition rows; only the first (品號) is set");
        var op = rows[0][0];
        var value = rows[0][1];

        await SetOperatorEqualsAsync(dialog, op, value, cancellationToken);
        await SetValueAsync(dialog, value, code, cancellationToken);
    }

    /// <summary>
    /// 一般選項 operator list order (user's screenshot 2026-10-16): &lt;, &lt;=, =, &gt;=, &gt;,
    /// %like%, like%, %like. Home then Down twice selects 「=」; the result is read back.
    /// </summary>
    private async Task SetOperatorEqualsAsync(nint dialog, WindowControl op, WindowControl value, CancellationToken cancellationToken)
    {
        if (ComboText(op) == "=") return;

        SafeClick(dialog, Win32Automation.Center(op.Rect));
        await Pace.Delay(200, cancellationToken);
        InputSender.Press(NativeMethods.VK_HOME);
        await Pace.Delay(60, cancellationToken);
        for (var i = 0; i < 2; i++)
        {
            InputSender.Press(NativeMethods.VK_DOWN);
            await Pace.Delay(60, cancellationToken);
        }

        // An open list takes Enter itself (commits and closes); with the list closed Enter
        // would press 確定, so it is sent only when the list is known to be open.
        if (IsDropDownOpen(dialog, op))
        {
            InputSender.Press(0x0D);
            await Pace.Delay(150, cancellationToken);
        }

        var shown = ComboText(op);
        _log.Info("dialog", $"operator set shown=\"{shown}\"");
        if (shown != "=")
            throw new InvalidOperationException($"無法把品號條件設為「=」（目前顯示「{shown}」）。");
    }

    private static string ComboText(WindowControl combo) => (NativeMethods.ControlText(combo.Handle) ?? string.Empty).Trim();

    private static bool IsDropDownOpen(nint dialog, WindowControl combo)
    {
        if (NativeMethods.QueryNumber(combo.Handle, NativeMethods.CB_GETDROPPEDSTATE) is { } dropped && dropped != 0)
            return true;
        var r = combo.Rect.ToRectangle();
        var below = new Rectangle(r.Left, r.Bottom - 2, Math.Max(r.Width, 60), 120);
        return Win32Automation.CountProcessPopupsOver(dialog, below, dialog) > 0;
    }

    private async Task SetValueAsync(nint dialog, WindowControl value, string code, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            SafeClick(dialog, Win32Automation.Center(value.Rect));
            await Pace.Delay(150, cancellationToken);
            var focus = NativeMethods.FocusedControlOfForeground();
            if (focus != value.Handle && !Win32Automation.IsInside(focus, value.Handle))
            {
                _log.Info("dialog", $"value box not focused after click attempt={attempt}");
                continue; // the first click may only have closed the operator list
            }

            var current = NativeMethods.ControlText(value.Handle) ?? string.Empty;
            InputSender.EndBackspace(current.Length + 2);
            await Pace.Delay(60, cancellationToken);
            InputSender.UnicodeText(code);
            await Pace.Delay(120, cancellationToken);

            var shown = (NativeMethods.ControlText(value.Handle) ?? string.Empty).Trim();
            if (shown == code) return;
            _log.Warn("dialog", $"value read-back mismatch shown={_log.Value(shown)}");
            throw new InvalidOperationException("品號輸入後讀回的內容不一致，已停止。");
        }
        throw new InvalidOperationException("無法把游標移到品號輸入框。");
    }

    private async Task ConfirmQueryAsync(nint root, nint dialog, CancellationToken cancellationToken)
    {
        var ok = Win32Automation.EnumerateChildren(dialog)
            .FirstOrDefault(c => c.Visible && c.Enabled && Invi02Layout.NormalizeCaption(c.Text).StartsWith("確定", StringComparison.Ordinal))
            ?? throw new InvalidOperationException("「設定查詢條件」找不到「確定」按鈕。");
        SafeClick(dialog, Win32Automation.Center(ok.Rect));
        _log.Info("dialog", "確定 clicked");

        var deadline = Environment.TickCount64 + QueryRunWaitMs;
        while (NativeMethods.IsWindow(dialog) && NativeMethods.IsWindowVisible(dialog))
        {
            if (Environment.TickCount64 > deadline)
                throw new InvalidOperationException("按「確定」後查詢視窗沒有關閉，請人工確認 ERP 畫面。");
            await Pace.Delay(100, cancellationToken);
        }

        // A message box (for example nothing found) keeps the form disabled.
        deadline = Environment.TickCount64 + 3000;
        while (!NativeMethods.IsWindowEnabled(root))
        {
            if (Environment.TickCount64 > deadline)
                throw new InvalidOperationException("查詢後 ERP 顯示了訊息視窗（可能查無此品號），請人工確認並關閉。");
            await Pace.Delay(100, cancellationToken);
        }
    }

    /// <summary>
    /// The record ERP shows after the query, or null when there is none: for a 品號 that does
    /// not exist ERP goes to 資料瀏覽 with an empty list and an empty header (user, 2026-10-16),
    /// so a 品號 field that stays empty for a moment means not found.
    /// </summary>
    private async Task<HeaderControls?> WaitForRecordAsync(nint root, WindowControl page, string code, CancellationToken cancellationToken)
    {
        var deadline = Environment.TickCount64 + RecordWaitMs;
        long? emptySince = null;
        var shown = string.Empty;
        while (true)
        {
            var header = ResolveHeader(root, page);
            shown = (NativeMethods.ControlText(header.ItemCode.Handle) ?? string.Empty).Trim();
            if (ItemCodeRules.SameCode(shown, code)) return header;
            if (shown.Length == 0)
            {
                emptySince ??= Environment.TickCount64;
                if (Environment.TickCount64 - emptySince >= EmptyRecordStableMs) return null;
            }
            else emptySince = null;
            if (Environment.TickCount64 > deadline) break;
            await Pace.Delay(100, cancellationToken);
        }
        _log.Warn("query", $"record mismatch shown={_log.Value(shown)}");
        throw new InvalidOperationException($"查詢「{code}」後 ERP 顯示的是另一個品號「{shown}」，結果不採用，請人工確認。");
    }

    // ---- tabs -------------------------------------------------------------------------

    /// <summary>
    /// Shows <paramref name="tab"/>: nothing to do when it is already shown; otherwise click
    /// the measured position, and when that does not show it, find the caption by OCR and
    /// click there (user decision 2026-10-16). Success is always the sheet becoming visible.
    /// </summary>
    private async Task ActivateTabAsync(nint root, WindowControl page, TabStrip strip, string tab, nint sheet, CancellationToken cancellationToken, bool force = false)
    {
        // force: click even when the sheet already looks shown (ERP may still be switching).
        if (!force && NativeMethods.IsWindowVisible(sheet)) return;
        if (!Win32Automation.PrepareForeground(root, _log))
            throw new InvalidOperationException($"切換頁籤「{tab}」前無法把 ERP 視窗帶到前景。");

        var pageRect = page.Rect.ToRectangle();
        if (strip.PointFor(pageRect, tab) is { } point)
        {
            SafeClick(root, point);
            if (await WaitVisibleAsync(sheet, cancellationToken))
            {
                _log.Info("tab", $"shown tab={tab} by=geometry");
                return;
            }
            _log.Warn("tab", $"geometry click did not show tab={tab} point={point.X - pageRect.Left},{point.Y - pageRect.Top}");
        }

        var stripArea = new Rectangle(pageRect.Left, pageRect.Top, pageRect.Width, Math.Min(32, pageRect.Height));
        using var image = ScreenCapture.Capture(stripArea);
        var tokens = await _ocr.RecognizeAsync(image, cancellationToken, requireChinese: true);
        var found = OcrWordFinder.FindWord(tokens, tab);
        if (found is null)
        {
            _log.Warn("tab", $"OCR did not find tab={tab} tokens={tokens.Count} read=\"{string.Join(" ", tokens.Select(t => t.Text))}\"");
            throw new InvalidOperationException($"找不到 ERP 頁籤「{tab}」（座標與 OCR 都沒有成功）。");
        }
        var target = new Point(stripArea.Left + found.Value.Left + found.Value.Width / 2, stripArea.Top + found.Value.Top + found.Value.Height / 2);
        SafeClick(root, target);
        if (await WaitVisibleAsync(sheet, cancellationToken))
        {
            _log.Info("tab", $"shown tab={tab} by=ocr point={target.X - pageRect.Left},{target.Y - pageRect.Top}");
            return;
        }
        throw new InvalidOperationException($"已點擊 ERP 頁籤「{tab}」，但頁籤沒有切換。");
    }

    private static async Task<bool> WaitVisibleAsync(nint sheet, CancellationToken cancellationToken)
    {
        var deadline = Environment.TickCount64 + TabWaitMs;
        var stable = 0;
        while (Environment.TickCount64 < deadline)
        {
            await Pace.Delay(40, cancellationToken);
            stable = NativeMethods.IsWindowVisible(sheet) ? stable + 1 : 0;
            if (stable >= 3) return true;
        }
        return false;
    }

    /// <summary>Clicks only when the point really shows <paramref name="root"/> (nothing covers it).</summary>
    private static void SafeClick(nint root, Point point)
    {
        if (!Win32Automation.IsPointShownBy(root, point))
            throw new InvalidOperationException("要點擊的 ERP 位置被其他視窗遮住，已停止。");
        InputSender.Click(point);
    }

    // ---- Excel export ---------------------------------------------------------------

    /// <summary>
    /// Ribbon 「Excel」 → 「轉出至 Excel」 → type the path into 儲存檔案 → 確定 → answer No to
    /// 「檢視Excel資料?」 (Yes would open the file). ERP has no shortcut for it (user,
    /// 2026-10-16). The Ribbon is not made of windows, so its text is found by OCR, the
    /// same as CYERPAutoInput's 列印 → 直接列印; a wrong Ribbon click could start another
    /// function, so no fixed coordinates are used there.
    /// </summary>
    private async Task ExportToExcelAsync(nint root, string path, CancellationToken cancellationToken)
    {
        if (!Win32Automation.PrepareForeground(root, _log))
            throw new InvalidOperationException("轉出 Excel 前無法把品號資料建立作業帶到前景。");
        if (!NativeMethods.GetWindowRect(root, out var window))
            throw new InvalidOperationException("讀不到品號資料建立作業的視窗位置。");

        var ribbon = new Rectangle(window.Left, window.Top, Math.Min(window.Width, 1200), Math.Min(window.Height, 220));
        IReadOnlyList<OcrToken> tokens;
        using (var image = ScreenCapture.Capture(ribbon))
            tokens = await _ocr.RecognizeAsync(image, cancellationToken);
        var label = OcrWordFinder.FindWord(tokens, "Excel") ?? OcrWordFinder.FindWord(tokens, "EXCEL");
        if (label is null)
        {
            foreach (var t in tokens)
                _log.Info("export", $"  ribbon token x={t.Rect.Left} y={t.Rect.Top} w={t.Rect.Width} h={t.Rect.Height} text={_log.Value(t.Text)}");
            throw new InvalidOperationException("ERP 工具列上找不到「Excel」。請確認 Ribbon 停在「常用」。");
        }
        var labelScreen = new Rectangle(ribbon.Left + label.Value.Left, ribbon.Top + label.Value.Top, label.Value.Width, label.Value.Height);
        var before = Win32Automation.VisibleProcessWindows(root);
        SafeClick(root, new Point(labelScreen.Left + labelScreen.Width / 2, labelScreen.Top + labelScreen.Height / 2));
        _log.Info("export", $"ribbon Excel clicked label=({labelScreen.Left},{labelScreen.Top},{labelScreen.Width},{labelScreen.Height})");

        // The drop-down: 轉出至 Excel / 轉出全部符合查詢條件的資料.
        var near = new Rectangle(labelScreen.Left - 120, labelScreen.Top - 60, 480, 220);
        var item = await FindMenuItemAsync(root, near, "轉出至", cancellationToken);
        if (item is null)
        {
            // Close a drop-down left open; Esc only when ERP really shows a popup, so a stray
            // Esc never reaches the INVI02 form itself (CYERPAutoInput rule).
            if (Win32Automation.ProcessPopupsNear(root, near).Count > 0) InputSender.Press(NativeMethods.VK_ESCAPE);
            throw new InvalidOperationException("Excel 選單沒有出現「轉出至 Excel」。");
        }
        InputSender.Click(item.Value);
        _log.Info("export", "轉出至 Excel clicked");

        // 匯出設定 is recognised by its 儲存檔案 path box, or by the INVI02 caption it carries.
        await CompleteExportAsync(root, before, path, Invi02Layout.IsInvi02Title, strict: true, cancellationToken);
    }

    /// <summary>
    /// Everything after the export command: the export dialog (ERP's 匯出設定, or a Windows
    /// 另存新檔 dialog), the path, 確定, and the end of the export. <paramref name="strict"/> is
    /// INVI02's known flow: xlsx format and 單頭／單身 全選 are required, and so is the
    /// 「檢視Excel資料?」 question. INVQ01's 轉出到Excel dialog has not been seen yet, so there
    /// whatever applies is done and the end is the file written with ERP idle again.
    /// </summary>
    private async Task CompleteExportAsync(nint root, IReadOnlySet<nint> before, string path, Func<string, bool> ownTitle, bool strict, CancellationToken cancellationToken)
    {
        var found = await WaitNewWindowAsync(root, before, h => IsExportDialog(h, ownTitle), ExportDialogWaitMs, cancellationToken);
        if (found is not { } dialog)
        {
            LogProcessWindows(root, "after-export-command");
            throw new InvalidOperationException("按下轉出 Excel 後沒有出現匯出設定視窗。");
        }
        await Pace.Delay(500, cancellationToken); // let ERP finish building the dialog
        if (IsSaveDialog(dialog)) await FillSaveDialogAsync(dialog, path, cancellationToken);
        else await FillExportDialogAsync(dialog, path, strict, cancellationToken);
        await FinishExportAsync(root, before, dialog, path, questionRequired: strict, cancellationToken);
    }

    private static bool IsExportDialog(nint h, Func<string, bool> ownTitle)
    {
        if (IsSaveDialog(h)) return true;
        if (Invi02Layout.IsMessageDialogClass(NativeMethods.ClassName(h))) return false;
        return ownTitle(NativeMethods.WindowText(h)) ||
               Win32Automation.InputControls(h, visibleOnly: true).Any(c => Invi02Layout.LooksLikeFilePath((NativeMethods.ControlText(c.Handle) ?? string.Empty).Trim()));
    }

    /// <summary>A Windows 另存新檔 dialog: class #32770 with a file name box and a 存檔 button.</summary>
    private static bool IsSaveDialog(nint h) =>
        NativeMethods.ClassName(h) == "#32770" &&
        Win32Automation.EnumerateChildren(h).Any(c => c.Visible && c.ClassName == "Edit") &&
        FindButton(h, IsSaveCaption) is not null;

    private static bool IsSaveCaption(string caption) =>
        caption.StartsWith("存檔", StringComparison.Ordinal) || caption.StartsWith("儲存", StringComparison.Ordinal) ||
        caption.StartsWith("SAVE", StringComparison.Ordinal);

    private async Task FillSaveDialogAsync(nint dialog, string path, CancellationToken cancellationToken)
    {
        if (!Win32Automation.PrepareForeground(dialog, _log))
            throw new InvalidOperationException("無法把另存新檔視窗帶到前景。");
        var box = Win32Automation.EnumerateChildren(dialog).First(c => c.Visible && c.ClassName == "Edit");
        SafeClick(dialog, Win32Automation.Center(box.Rect));
        await Pace.Delay(150, cancellationToken);
        await PasteIntoAsync(box.Handle, path, cancellationToken);
        var shown = (NativeMethods.ControlText(box.Handle) ?? string.Empty).Trim();
        if (!string.Equals(shown, path, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("另存新檔的檔名貼上後讀回的內容不一致，已停止。");
        var save = FindButton(dialog, IsSaveCaption) ?? throw new InvalidOperationException("另存新檔視窗找不到「存檔」按鈕。");
        SafeClick(dialog, Win32Automation.Center(save.Rect));
        _log.Info("export", "save dialog path pasted and 存檔 clicked");
    }

    /// <summary>
    /// ERP writes the file (showing a 處理進度 window meanwhile), closes the dialog and may ask
    /// 「檢視Excel資料?」, answered No. Only a window with a No button counts: the progress window
    /// is small too and was mistaken for the question (V0.2.0 LOG, 2026-10-16). OCR is only a
    /// late fallback and must read 「檢視」 before it clicks. Without a required question the
    /// export is done once the file exists and ERP has been idle for a moment.
    /// </summary>
    private async Task FinishExportAsync(nint root, IReadOnlySet<nint> before, nint dialog, string path, bool questionRequired, CancellationToken cancellationToken)
    {
        var deadline = Environment.TickCount64 + ExportRunWaitMs;
        long? dialogClosedAt = null;
        long? quietSince = null;
        long nextOcr = 0;
        nint question = 0;
        while (question == 0)
        {
            if (Environment.TickCount64 > deadline)
            {
                LogProcessWindows(root, "after-export");
                foreach (var h in Win32Automation.VisibleProcessWindows(root).Where(h => !before.Contains(h)))
                    LogChildren(h, "after-export-window");
                throw new InvalidOperationException(questionRequired
                    ? "轉出 Excel 後沒有出現「檢視Excel資料?」，請人工確認 ERP 畫面。"
                    : "轉出 Excel 後 ERP 沒有完成（仍有視窗開著或沒有產生檔案），請人工確認 ERP 畫面。");
            }
            await Pace.Delay(250, cancellationToken);
            if (NativeMethods.IsWindow(dialog) && NativeMethods.IsWindowVisible(dialog)) continue;
            dialogClosedAt ??= Environment.TickCount64;

            var fresh = Win32Automation.VisibleProcessWindows(root).Where(h => !before.Contains(h) && IsSmallDialog(h)).ToList();
            foreach (var h in fresh)
            {
                if (FindButton(h, IsNoCaption) is not { } no) continue;
                await Pace.Delay(200, cancellationToken);
                SafeClick(h, Win32Automation.Center(no.Rect));
                question = h;
                break;
            }
            if (question != 0) break;

            if (!questionRequired)
            {
                var quiet = NativeMethods.IsWindowEnabled(root) && File.Exists(path) &&
                            !Win32Automation.VisibleProcessWindows(root).Any(h => !before.Contains(h));
                if (!quiet) quietSince = null;
                else if ((quietSince ??= Environment.TickCount64) + 1500 <= Environment.TickCount64)
                {
                    _log.Info("export", "export finished without a question");
                    return;
                }
            }
            if (Environment.TickCount64 - dialogClosedAt < 8000 || Environment.TickCount64 < nextOcr) continue;

            nextOcr = Environment.TickCount64 + 2000;
            foreach (var h in fresh)
            {
                if (!NativeMethods.GetWindowRect(h, out var q)) continue;
                IReadOnlyList<OcrToken> questionText;
                using (var image = ScreenCapture.Capture(q.ToRectangle()))
                    questionText = await _ocr.RecognizeAsync(image, cancellationToken, requireChinese: true);
                if (OcrWordFinder.FindWord(questionText, "檢視") is null) continue;
                LogChildren(h, "export-question");
                await OcrClickAsync(h, q.ToRectangle(), "No", "「檢視Excel資料?」的 No", cancellationToken, alternative: "否");
                question = h;
                break;
            }
        }
        _log.Info("export", "檢視Excel資料? answered No");

        deadline = Environment.TickCount64 + 15000;
        while (NativeMethods.IsWindow(question) && NativeMethods.IsWindowVisible(question) || !NativeMethods.IsWindowEnabled(root))
        {
            if (Environment.TickCount64 > deadline)
                throw new InvalidOperationException("回答「檢視Excel資料?」後 ERP 仍有視窗開著，請人工確認。");
            await Pace.Delay(100, cancellationToken);
        }
    }

    /// <summary>Screen point of the drop-down item containing <paramref name="word"/>, or null after a bounded wait.</summary>
    private async Task<Point?> FindMenuItemAsync(nint root, Rectangle near, string word, CancellationToken cancellationToken, bool topmost = false)
    {
        var deadline = Environment.TickCount64 + MenuWaitMs;
        while (Environment.TickCount64 < deadline)
        {
            await Pace.Delay(250, cancellationToken);
            var areas = Win32Automation.ProcessPopupsNear(root, near).ToList();
            areas.Add(near);
            foreach (var area in areas)
            {
                // The Ribbon drop-down is a layered window; a plain screen copy misses it.
                using var image = ScreenCapture.CaptureWithLayers(area);
                var tokens = await _ocr.RecognizeAsync(image, cancellationToken);
                var matches = OcrWordFinder.FindWords(tokens, word);
                if (matches.Count > 0)
                {
                    // 串查 lists 庫存狀況查詢作業 twice; the first (top) one is used (user, 2026-10-16).
                    var r = topmost ? matches.MinBy(m => m.Top) : matches[0];
                    if (matches.Count > 1) _log.Info("export", $"menu word={word} matches={matches.Count} topmost={topmost}");
                    return new Point(area.Left + r.Left + r.Width / 2, area.Top + r.Top + r.Height / 2);
                }
                if (Environment.TickCount64 >= deadline)
                    _log.Info("export", $"  menu area=({area.Left},{area.Top},{area.Width},{area.Height}) tokens=\"{string.Join(" ", tokens.Select(t => _log.Value(t.Text)))}\"");
            }
        }
        return null;
    }

    /// <summary>
    /// 匯出設定 (user's screenshot 2026-10-16): 檔案格式 must already be the xlsx one; every
    /// field is ticked with 全選 on both 單頭 and 單身, because 儲存設定 remembers whatever
    /// someone ticked last (user, 2026-10-16); 儲存檔案 gets CYERPAutoQuery's own TEMP path,
    /// read back; 確定 is clicked with the mouse.
    /// </summary>
    private async Task FillExportDialogAsync(nint dialog, string path, bool strict, CancellationToken cancellationToken)
    {
        if (!Win32Automation.PrepareForeground(dialog, _log))
            throw new InvalidOperationException("無法把匯出設定視窗帶到前景。");
        var inputs = Win32Automation.InputControls(dialog, visibleOnly: true);
        var texts = inputs.Select(c => (Control: c, Text: (NativeMethods.ControlText(c.Handle) ?? string.Empty).Trim())).ToList();
        if (!strict) LogChildren(dialog, "export-dialog");
        var formats = texts.Where(t => Invi02Layout.IsCombo(t.Control.ClassName) && t.Text.Contains(".xls", StringComparison.OrdinalIgnoreCase)).ToList();
        if ((strict || formats.Count > 0) && !formats.Any(t => t.Text.Contains(".xlsx", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("匯出設定的檔案格式不是「Excel 2007以上 (*.xlsx)」，請在 ERP 改好後再查詢。");
        var pathBoxes = texts
            .Where(t => Invi02Layout.IsEditBox(t.Control.ClassName) && Invi02Layout.LooksLikeFilePath(t.Text))
            .ToList();
        if (pathBoxes.Count != 1)
        {
            _log.Warn("export", $"path box candidates={pathBoxes.Count} inputs=[{string.Join(",", inputs.Select(c => c.ClassName))}]");
            throw new InvalidOperationException("匯出設定視窗找不到唯一的「儲存檔案」欄位。");
        }
        var box = pathBoxes[0].Control;

        if (strict || Win32Automation.FindTabSheet(dialog, Invi02Layout.ExportFieldTabs.Order[0]) is not null)
            await SelectAllExportFieldsAsync(dialog, cancellationToken);
        else if (FindButton(dialog, c => c.StartsWith("全選", StringComparison.Ordinal)) is not null)
            await PressSelectAllAsync(dialog, "匯出欄位", cancellationToken);

        // The path is pasted, not typed (user, 2026-10-16: faster): select the box's whole
        // text with EM_SETSEL (never Ctrl+A in ERP) and send WM_PASTE; the clipboard is
        // restored afterwards.
        for (var attempt = 1; ; attempt++)
        {
            SafeClick(dialog, Win32Automation.Center(box.Rect));
            await Pace.Delay(150, cancellationToken);
            await PasteIntoAsync(box.Handle, path, cancellationToken);
            var shown = (NativeMethods.ControlText(box.Handle) ?? string.Empty).Trim();
            if (string.Equals(shown, path, StringComparison.OrdinalIgnoreCase)) break;
            _log.Warn("export", $"path read-back mismatch attempt={attempt} len={shown.Length}");
            if (attempt >= 2) throw new InvalidOperationException("匯出路徑貼上後讀回的內容不一致，已停止。");
        }
        _log.Info("export", "path pasted");

        await ClickButtonAsync(dialog, "確定", "匯出設定視窗的「確定」", cancellationToken);
        _log.Info("export", "匯出設定 確定 clicked");
    }

    /// <summary>
    /// 全選 on each 匯出欄位 tab (單頭, 單身). On the user's PC (V0.2.0 LOG, 2026-10-16) the tabs
    /// live on a TcxPageControl whose 單身 sheet only exists after its tab is clicked, and
    /// 全選／清除／確定 are TBitBtn windows. The tab is clicked at its measured position and
    /// counts as shown once a visible sheet with that caption exists; OCR is the fallback.
    /// </summary>
    private async Task SelectAllExportFieldsAsync(nint dialog, CancellationToken cancellationToken)
    {
        var page = Win32Automation.EnumerateChildren(dialog)
            .Where(c => c.Visible && c.ClassName.Equals("TcxPageControl", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault(c => Win32Automation.EnumerateChildren(c.Handle).Any(s => s.ClassName.Contains("TabSheet", StringComparison.OrdinalIgnoreCase)));
        if (page is null) LogChildren(dialog, "export-dialog");
        if (!NativeMethods.GetWindowRect(dialog, out var d))
            throw new InvalidOperationException("讀不到匯出設定視窗位置。");
        foreach (var tab in Invi02Layout.ExportFieldTabs.Order)
        {
            await ShowExportTabAsync(dialog, page, d.ToRectangle(), tab, cancellationToken);
            await PressSelectAllAsync(dialog, tab, cancellationToken);
        }
    }

    private async Task ShowExportTabAsync(nint dialog, WindowControl? page, Rectangle dialogRect, string tab, CancellationToken cancellationToken)
    {
        bool Shown() => Win32Automation.FindTabSheet(dialog, tab) is { } sheet && NativeMethods.IsWindowVisible(sheet.Handle);
        if (Shown()) return;
        if (page is not null && Invi02Layout.ExportFieldTabs.PointFor(page.Rect.ToRectangle(), tab) is { } point)
        {
            SafeClick(dialog, point);
            if (await WaitUntilAsync(Shown, 1500, cancellationToken))
            {
                _log.Info("export", $"export tab shown tab={tab} by=geometry");
                return;
            }
            _log.Warn("export", $"geometry click did not show export tab={tab}");
        }
        // 「匯出子單頭」 sits right of the tabs; only the dialog's left part is read.
        var tabArea = new Rectangle(dialogRect.Left, dialogRect.Top, Math.Min(dialogRect.Width, 360), dialogRect.Height);
        await OcrClickAsync(dialog, tabArea, tab, $"匯出設定頁籤「{tab}」", cancellationToken);
        if (page is not null && !await WaitUntilAsync(Shown, 1500, cancellationToken))
            throw new InvalidOperationException($"已點擊匯出設定頁籤「{tab}」，但頁籤沒有切換。");
        await Pace.Delay(300, cancellationToken);
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> condition, int timeoutMs, CancellationToken cancellationToken)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            await Pace.Delay(60, cancellationToken);
            if (condition()) return true;
        }
        return false;
    }

    /// <summary>Replaces an edit box's text by a clipboard paste (EM_SETSEL + WM_PASTE), keeping the user's clipboard.</summary>
    private async Task PasteIntoAsync(nint box, string text, CancellationToken cancellationToken)
    {
        IDataObject? saved = null;
        try { saved = Clipboard.GetDataObject(); } catch (ExternalException) { }
        try
        {
            Clipboard.SetText(text);
            NativeMethods.SendMessage(box, NativeMethods.EM_SETSEL, 0, -1);
            NativeMethods.SendMessage(box, NativeMethods.WM_PASTE, 0, 0);
            await Pace.Delay(150, cancellationToken);
        }
        finally
        {
            try
            {
                if (saved is not null) Clipboard.SetDataObject(saved, copy: true);
                else Clipboard.Clear();
            }
            catch (ExternalException ex)
            {
                _log.Warn("export", $"clipboard not restored: {ex.GetType().Name}");
            }
        }
    }

    private async Task PressSelectAllAsync(nint dialog, string what, CancellationToken cancellationToken)
    {
        await ClickButtonAsync(dialog, "全選", $"匯出設定「{what}」的「全選」", cancellationToken);
        _log.Info("export", $"全選 clicked tab={what}");
        await Pace.Delay(300, cancellationToken);
    }

    /// <summary>
    /// Clicks the button captioned <paramref name="caption"/>: the button window when ERP has
    /// one, otherwise the caption found by OCR in the dialog (drawn DevExpress buttons).
    /// </summary>
    private async Task ClickButtonAsync(nint dialog, string caption, string what, CancellationToken cancellationToken)
    {
        if (FindButton(dialog, c => c.StartsWith(caption, StringComparison.Ordinal)) is { } button)
        {
            SafeClick(dialog, Win32Automation.Center(button.Rect));
            return;
        }
        if (!NativeMethods.GetWindowRect(dialog, out var d))
            throw new InvalidOperationException($"讀不到{what}所在視窗的位置。");
        await OcrClickAsync(dialog, d.ToRectangle(), caption, what, cancellationToken);
    }

    /// <summary>Finds <paramref name="word"/> (or <paramref name="alternative"/>) by OCR inside <paramref name="area"/> of <paramref name="window"/> and clicks it.</summary>
    private async Task OcrClickAsync(nint window, Rectangle area, string word, string what, CancellationToken cancellationToken, string? alternative = null)
    {
        if (!Win32Automation.PrepareForeground(window, _log))
            throw new InvalidOperationException($"點擊{what}前無法把視窗帶到前景。");
        IReadOnlyList<OcrToken> tokens;
        using (var image = ScreenCapture.Capture(area))
            tokens = await _ocr.RecognizeAsync(image, cancellationToken, requireChinese: true);
        if ((OcrWordFinder.FindWord(tokens, word) ?? (alternative is null ? null : OcrWordFinder.FindWord(tokens, alternative))) is not { } r)
        {
            _log.Warn("export", $"OCR did not find {what} tokens=\"{string.Join(" ", tokens.Select(t => t.Text))}\"");
            throw new InvalidOperationException($"找不到{what}。");
        }
        var point = new Point(area.Left + r.Left + r.Width / 2, area.Top + r.Top + r.Height / 2);
        SafeClick(window, point);
        _log.Info("export", $"clicked by OCR what={what} point={point.X - area.Left},{point.Y - area.Top}");
    }

    /// <summary>For on-site diagnosis: the child windows of an ERP dialog (classes, captions, positions).</summary>
    private void LogChildren(nint window, string context)
    {
        NativeMethods.GetWindowRect(window, out var w);
        foreach (var c in Win32Automation.EnumerateChildren(window).Where(c => c.Visible))
            _log.Info("dialog", $"{context}: child class={c.ClassName} text=\"{c.Text}\" rect={c.Rect.Left - w.Left},{c.Rect.Top - w.Top},{c.Rect.Width}x{c.Rect.Height}");
    }

    /// <summary>Win32 Button, Delphi TButton／TBitBtn (ERP's 全選／確定) and DevExpress TcxButton.</summary>
    private static bool IsButtonClass(string className) =>
        className.Contains("Button", StringComparison.OrdinalIgnoreCase) || className.Contains("Btn", StringComparison.OrdinalIgnoreCase);

    private static bool IsSmallDialog(nint hwnd) =>
        NativeMethods.GetWindowRect(hwnd, out var r) && r.Width is > 120 and < 700 && r.Height is > 80 and < 450;

    private static bool IsNoCaption(string caption) =>
        caption is "NO" or "否" || caption.StartsWith("否(", StringComparison.Ordinal);

    private static WindowControl? FindButton(nint window, Func<string, bool> caption) =>
        Win32Automation.EnumerateChildren(window)
            .FirstOrDefault(c => c.Visible && c.Enabled && IsButtonClass(c.ClassName) &&
                                 caption(Invi02Layout.NormalizeCaption(c.Text)));

    private static async Task<nint?> WaitNewWindowAsync(nint root, IReadOnlySet<nint> before, Func<nint, bool> match, int timeoutMs, CancellationToken cancellationToken)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            await Pace.Delay(120, cancellationToken);
            var found = Win32Automation.VisibleProcessWindows(root).Where(h => !before.Contains(h) && match(h)).ToList();
            if (found.Count == 1) return found[0];
        }
        return null;
    }

    // ---- INVQ01 庫存狀況查詢作業 ---------------------------------------------------------

    private const int Invq01OpenWaitMs = 30000;
    private const int Invq01QueryWaitMs = 60000;
    private const int Invq01CloseWaitMs = 10000;

    /// <summary>
    /// INVI02 串查 → 庫存狀況查詢作業 opens INVQ01 on the item shown (user, 2026-10-16). For a
    /// batch-managed item 查各批庫存量 is queried and exported; when asked, 查歷史異動 for the date
    /// range as well. Read-only: only query conditions are set. INVQ01 is closed afterwards
    /// (user, 2026-10-16), also when something failed.
    /// </summary>
    private async Task<ItemQueryResult> QueryInvq01Async(nint invi02, string code, ItemQueryResult result,
        bool batches, HistoryRange? history, IProgress<string> progress, CancellationToken cancellationToken)
    {
        progress.Report("ERP：開啟庫存狀況查詢作業（串查）…");
        var invq = await OpenInvq01Async(invi02, cancellationToken);
        var closed = false;
        try
        {
            NativeMethods.ShowWindow(invq, NativeMethods.SW_MAXIMIZE);
            await Pace.Delay(MaximizeSettleMs, cancellationToken);
            if (!Win32Automation.PrepareForeground(invq, _log))
                throw new InvalidOperationException("無法把庫存狀況查詢作業帶到前景。");
            await WaitInvq01ItemAsync(invq, code, cancellationToken);
            var page = FindInvq01PageControl(invq);

            if (batches)
            {
                progress.Report("ERP：查各批庫存量…");
                var sheets = await RunInvq01QueryAsync(invq, page, Invq01Layout.BatchTab, null, cancellationToken);
                result = result with { Batches = Invq01Export.ParseBatches(sheets, code), BatchManaged = true };
            }
            if (history is not null)
            {
                progress.Report($"ERP：查歷史異動（{Invq01Layout.ErpDate(history.From)}～{Invq01Layout.ErpDate(history.To)}）…");
                var sheets = await RunInvq01QueryAsync(invq, page, Invq01Layout.HistoryTab, history, cancellationToken);
                result = result with { History = Invq01Export.ParseHistory(sheets, code), HistoryRange = history };
            }

            progress.Report("ERP：關閉庫存狀況查詢作業…");
            closed = await CloseInvq01Async(invq);
            if (!closed) throw new InvalidOperationException("庫存狀況查詢作業沒有關閉，請人工確認 ERP 畫面。");
            Win32Automation.PrepareForeground(invi02, _log);
            return result;
        }
        finally
        {
            if (!closed && !await CloseInvq01Async(invq))
                _log.Warn("invq01", "INVQ01 left open after a failure");
        }
    }

    private async Task<nint> OpenInvq01Async(nint invi02, CancellationToken cancellationToken)
    {
        if (!Win32Automation.PrepareForeground(invi02, _log))
            throw new InvalidOperationException("串查前無法把品號資料建立作業帶到前景。");
        var before = Win32Automation.VisibleProcessWindows(invi02);
        var label = await RibbonClickAsync(invi02, Invq01Layout.LinkMenu, "工具列的「串查」", cancellationToken);

        var near = new Rectangle(label.Left - 150, label.Top - 40, 640, 560);
        var item = await FindMenuItemAsync(invi02, near, Invq01Layout.FormTitle, cancellationToken, topmost: true);
        if (item is null)
        {
            if (Win32Automation.ProcessPopupsNear(invi02, near).Count > 0) InputSender.Press(NativeMethods.VK_ESCAPE);
            throw new InvalidOperationException("串查選單沒有出現「庫存狀況查詢作業」。");
        }
        InputSender.Click(item.Value);
        _log.Info("invq01", "串查 庫存狀況查詢作業 clicked");

        var started = Environment.TickCount64;
        var found = await WaitNewWindowAsync(invi02, before,
            h => Invq01Layout.IsInvq01Title(NativeMethods.WindowText(h)) && !Invi02Layout.IsMessageDialogClass(NativeMethods.ClassName(h)),
            Invq01OpenWaitMs, cancellationToken);
        if (found is not { } invq)
        {
            LogProcessWindows(invi02, "after-串查");
            throw new InvalidOperationException("從串查開啟庫存狀況查詢作業逾時。");
        }
        _log.Info("invq01", $"INVQ01 opened after_ms={Environment.TickCount64 - started} class={NativeMethods.ClassName(invq)}");
        return invq;
    }

    /// <summary>INVQ01 opened from INVI02 shows the same 品號; anything else stops the query.</summary>
    private async Task WaitInvq01ItemAsync(nint invq, string code, CancellationToken cancellationToken)
    {
        bool Shows() => Win32Automation.InputControls(invq, visibleOnly: true)
            .Any(c => ItemCodeRules.SameCode(NativeMethods.ControlText(c.Handle), code));
        if (await WaitUntilAsync(Shows, 10000, cancellationToken)) return;
        LogChildren(invq, "invq01");
        throw new InvalidOperationException($"庫存狀況查詢作業沒有帶出品號「{code}」，已停止。");
    }

    /// <summary>The lower TcxPageControl (查各庫庫存量 … 查詢結果), or null when its sheets are not windows.</summary>
    private WindowControl? FindInvq01PageControl(nint invq)
    {
        var sheet = Win32Automation.FindTabSheet(invq, Invq01Layout.BatchTab) ?? Win32Automation.FindTabSheet(invq, Invq01Layout.HistoryTab);
        if (sheet is null)
        {
            _log.Warn("invq01", "INVQ01 tab sheets not found as windows; tabs are found by OCR");
            LogChildren(invq, "invq01");
            return null;
        }
        var pageHandle = NativeMethods.GetParent(sheet.Handle);
        return Win32Automation.EnumerateChildren(invq).FirstOrDefault(c => c.Handle == pageHandle);
    }

    /// <summary>One INVQ01 query: tab → (dates) → 開始查詢 → 查詢結果 → 轉出到Excel → read.</summary>
    private async Task<IReadOnlyList<(string Name, List<string[]> Rows)>> RunInvq01QueryAsync(
        nint invq, WindowControl? page, string tab, HistoryRange? range, CancellationToken cancellationToken)
    {
        await ShowInvq01TabAsync(invq, page, tab, cancellationToken);
        if (range is not null) await FillHistoryRangeAsync(invq, range, cancellationToken);

        await RibbonClickAsync(invq, Invq01Layout.StartQuery, "工具列的「開始查詢」", cancellationToken);
        _log.Info("invq01", $"開始查詢 clicked tab={tab}");
        await WaitInvq01QueryAsync(invq, tab, cancellationToken);

        var path = ExportFiles.NewPath(Invq01Layout.FormCode);
        try
        {
            var before = Win32Automation.VisibleProcessWindows(invq);
            await RibbonClickAsync(invq, Invq01Layout.ExportButton, "工具列的「轉出到Excel」", cancellationToken);
            _log.Info("invq01", $"轉出到Excel clicked tab={tab}");
            await CompleteExportAsync(invq, before, path, Invq01Layout.IsInvq01Title, strict: false, cancellationToken);
            return await ExportFiles.ReadAsync(path, cancellationToken);
        }
        finally
        {
            ExportFiles.Delete(path, _log);
        }
    }

    private async Task ShowInvq01TabAsync(nint invq, WindowControl? page, string tab, CancellationToken cancellationToken)
    {
        if (page is not null && Win32Automation.FindTabSheet(invq, tab) is { } sheet)
        {
            await ActivateTabAsync(invq, page, Invq01Layout.Tabs, tab, sheet.Handle, cancellationToken);
            return;
        }
        // Without sheet windows the caption is found by OCR; that the right query ran is checked
        // afterwards through the caption ERP gives the window (…-查各批庫存量).
        if (!NativeMethods.GetWindowRect(invq, out var w))
            throw new InvalidOperationException("讀不到庫存狀況查詢作業的視窗位置。");
        var area = new Rectangle(w.Left, w.Top + 150, w.Width, Math.Max(100, w.Height - 150));
        await OcrClickAsync(invq, area, tab, $"庫存狀況查詢作業的頁籤「{tab}」", cancellationToken);
        await Pace.Delay(500, cancellationToken);
    }

    /// <summary>查歷史異動 選擇日期 起／迄: the two boxes of that group, left to right, each read back.</summary>
    private async Task FillHistoryRangeAsync(nint invq, HistoryRange range, CancellationToken cancellationToken)
    {
        var group = Win32Automation.EnumerateChildren(invq)
            .FirstOrDefault(c => c.Visible && Invi02Layout.NormalizeCaption(c.Text) == Invq01Layout.DateGroup);
        var boxes = group is null ? [] : Win32Automation.InputControls(group.Handle, visibleOnly: true)
            .Where(c => !Invi02Layout.IsCheckBox(c.ClassName)).OrderBy(c => c.Rect.Left).ToList();
        if (boxes.Count != 2)
            boxes = Win32Automation.InputControls(invq, visibleOnly: true)
                .Where(c => c.ClassName.Contains("Date", StringComparison.OrdinalIgnoreCase)).OrderBy(c => c.Rect.Left).ToList();
        if (boxes.Count != 2)
        {
            LogChildren(invq, "invq01-history");
            throw new InvalidOperationException("查歷史異動找不到「選擇日期」的起、迄欄位。");
        }

        // Each segmented date edit must receive eight physical digit keys, followed by Enter.
        // Read back *after* Enter: never start the ERP query with a partial date.
        await SetDateAsync(invq, boxes[0], range.From, "起", cancellationToken);
        await SetDateAsync(invq, boxes[1], range.To, "迄", cancellationToken);
        var from = NativeMethods.ControlText(boxes[0].Handle);
        var to = NativeMethods.ControlText(boxes[1].Handle);
        _log.Info("invq01", $"history range shown from={_log.Value(from ?? string.Empty)} to={_log.Value(to ?? string.Empty)}");
        if (!Invq01Layout.ShowsDate(from, range.From) || !Invq01Layout.ShowsDate(to, range.To))
            throw new InvalidOperationException("查歷史異動的日期輸入後讀回不一致，已停止。");
    }

    private async Task SetDateAsync(nint invq, WindowControl box, DateOnly date, string what, CancellationToken cancellationToken)
    {
        // Clicking the center of a date mask selects 月 or 日, not 年. Click near its
        // left edge to select 年 and overwrite the three sections in one keyboard pass.
        // Do not use End/Backspace or Ctrl+A on this segmented ERP control.
        var width = box.Rect.Right - box.Rect.Left;
        if (width < 48)
            throw new InvalidOperationException($"歷史異動的{what}日欄位寬度不正確，已停止。");
        var yearPoint = new Point(box.Rect.Left + Math.Min(12, width / 10),
            (box.Rect.Top + box.Rect.Bottom) / 2);
        SafeClick(invq, yearPoint);
        await Pace.Delay(150, cancellationToken);
        var focus = NativeMethods.FocusedControlOfForeground();
        if (focus != box.Handle && !Win32Automation.IsInside(focus, box.Handle))
            throw new InvalidOperationException($"無法把游標移到歷史異動的{what}日欄位。");

        // Keep a Chinese IME from intercepting the physical digit keystrokes.
        if (NativeMethods.CloseImeFor(focus))
            await Pace.Delay(60, cancellationToken);

        // Use VK_0..VK_9 (keydown/keyup), not Unicode WM_CHAR: the ERP mask reacts to
        // actual number keys and advances 年 → 月 → 日 as the digits are entered.
        foreach (var digit in Invq01Layout.ErpDateKeys(date))
        {
            cancellationToken.ThrowIfCancellationRequested();
            InputSender.Press((ushort)digit);
            await Pace.Delay(70, cancellationToken);
        }
        InputSender.Press(NativeMethods.VK_RETURN);
        await Pace.Delay(200, cancellationToken);
        var shown = NativeMethods.ControlText(box.Handle);
        if (!Invq01Layout.ShowsDate(shown, date))
        {
            _log.Warn("invq01", $"history {what} date readback mismatch length={shown?.Length ?? 0}");
            throw new InvalidOperationException($"查歷史異動的{what}日輸入後讀回不一致，已停止。");
        }
    }

    /// <summary>
    /// After 開始查詢 ERP names the query in the caption (…-查各批庫存量) and shows 查詢結果. A
    /// message box raised by the query (one with a button that stays) stops the wait.
    /// </summary>
    private async Task WaitInvq01QueryAsync(nint invq, string tab, CancellationToken cancellationToken)
    {
        var before = Win32Automation.VisibleProcessWindows(invq);
        var result = Win32Automation.FindTabSheet(invq, Invq01Layout.ResultTab);
        var started = Environment.TickCount64;
        var deadline = started + Invq01QueryWaitMs;
        var messageSince = new Dictionary<nint, long>();
        while (true)
        {
            await Pace.Delay(150, cancellationToken);
            if (Invq01Layout.TitleShowsQuery(NativeMethods.WindowText(invq), tab) && NativeMethods.IsWindowEnabled(invq) &&
                (result is null || NativeMethods.IsWindowVisible(result.Handle)))
                break;
            foreach (var h in Win32Automation.VisibleProcessWindows(invq).Where(h => !before.Contains(h) && IsSmallDialog(h)))
            {
                // A 處理進度 window may carry 取消; only a message box (確定／OK) counts.
                if (!Invi02Layout.IsMessageDialogClass(NativeMethods.ClassName(h)) &&
                    FindButton(h, c => c.StartsWith("確定", StringComparison.Ordinal) || c == "OK") is null) continue;
                var since = messageSince.TryGetValue(h, out var t) ? t : messageSince[h] = Environment.TickCount64;
                if (Environment.TickCount64 - since < 1500) continue;
                LogChildren(h, "invq01-message");
                throw new InvalidOperationException($"「{tab}」開始查詢後 ERP 顯示了訊息視窗，請人工確認並關閉。");
            }
            if (Environment.TickCount64 > deadline)
            {
                LogProcessWindows(invq, "after-開始查詢");
                throw new InvalidOperationException($"「{tab}」開始查詢後 {Invq01QueryWaitMs / 1000} 秒內沒有出現查詢結果。");
            }
        }
        _log.Info("invq01", $"query shown tab={tab} after_ms={Environment.TickCount64 - started}");
        await Pace.Delay(600, cancellationToken); // let the result grid and the Ribbon settle
    }

    /// <summary>Closes INVQ01 with WM_CLOSE (no keys, no clicks); not cancellable so it also runs after Esc.</summary>
    private async Task<bool> CloseInvq01Async(nint invq)
    {
        if (!NativeMethods.IsWindow(invq)) return true;
        NativeMethods.PostMessage(invq, NativeMethods.WM_CLOSE, 0, 0);
        var deadline = Environment.TickCount64 + Invq01CloseWaitMs;
        while (Environment.TickCount64 < deadline)
        {
            await Task.Delay(100);
            if (!NativeMethods.IsWindow(invq) || !NativeMethods.IsWindowVisible(invq))
            {
                _log.Info("invq01", "INVQ01 closed");
                return true;
            }
        }
        LogProcessWindows(invq, "after-close-INVQ01");
        return false;
    }

    /// <summary>Finds <paramref name="word"/> on the Ribbon of <paramref name="window"/> by OCR and clicks it; returns the label's screen rectangle.</summary>
    private async Task<Rectangle> RibbonClickAsync(nint window, string word, string what, CancellationToken cancellationToken)
    {
        if (!Win32Automation.PrepareForeground(window, _log))
            throw new InvalidOperationException($"點擊{what}前無法把 ERP 視窗帶到前景。");
        if (!NativeMethods.GetWindowRect(window, out var w))
            throw new InvalidOperationException($"讀不到{what}所在視窗的位置。");
        var ribbon = new Rectangle(w.Left, w.Top, Math.Min(w.Width, 1200), Math.Min(w.Height, 220));
        IReadOnlyList<OcrToken> tokens;
        using (var image = ScreenCapture.Capture(ribbon))
            tokens = await _ocr.RecognizeAsync(image, cancellationToken, requireChinese: true);
        if (OcrWordFinder.FindWord(tokens, word) is not { } r)
        {
            _log.Warn("ribbon", $"OCR did not find {word} tokens=\"{string.Join(" ", tokens.Select(t => t.Text))}\"");
            throw new InvalidOperationException($"ERP 找不到{what}。");
        }
        var label = new Rectangle(ribbon.Left + r.Left, ribbon.Top + r.Top, r.Width, r.Height);
        SafeClick(window, new Point(label.Left + label.Width / 2, label.Top + label.Height / 2));
        return label;
    }
}

/// <summary>Delays that also stop the automation when the user presses Esc.</summary>
internal static class Pace
{
    public static async Task Delay(int milliseconds, CancellationToken cancellationToken)
    {
        var end = Environment.TickCount64 + milliseconds;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if ((NativeMethods.GetAsyncKeyState(NativeMethods.VK_ESCAPE) & 0x8000) != 0)
                throw new OperationCanceledException("Esc");
            var left = end - Environment.TickCount64;
            if (left <= 0) return;
            await Task.Delay((int)Math.Min(left, 25), cancellationToken);
        }
    }
}

/// <summary>
/// Machine-wide lock so two CY tools never drive SMART ERP's keyboard and mouse at the same
/// time. CYERPAutoInput does not take it yet (follow-up in WORK_HANDOFF.md).
/// </summary>
internal sealed class ErpAutomationGate : IDisposable
{
    public const string Name = @"Local\Chihyuan.SmartErp.UiAutomation";
    private readonly Mutex _mutex;

    private ErpAutomationGate(Mutex mutex) => _mutex = mutex;

    public static ErpAutomationGate Enter()
    {
        var mutex = new Mutex(false, Name);
        bool acquired;
        try
        {
            acquired = mutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            acquired = true; // the previous holder exited without releasing
        }
        if (!acquired)
        {
            mutex.Dispose();
            throw new QueryFailure(ErpAgentErrorCode.ERP_BUSY, "另一支 CY 程式正在操作 SMART ERP，請等它完成後再查詢。");
        }
        return new ErpAutomationGate(mutex);
    }

    public void Dispose()
    {
        _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
