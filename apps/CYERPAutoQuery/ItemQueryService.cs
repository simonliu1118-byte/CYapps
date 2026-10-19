using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CYERPAutoQuery;

/// <summary>
/// Looks up one 品號 in SMART ERP 品號資料建立作業 (INVI02) and takes its data from ERP's own
/// 「轉出至 Excel」 export (user, 2026-10-16: cleaner and safer than reading tab by tab).
/// Read-only towards ERP data: it opens the query dialog (F6), sets 一般選項 品號 = code,
/// confirms, shows 基本資料1 and exports; it never presses 新增／修改／刪除／儲存／取消.
/// </summary>
internal sealed partial class ItemQueryService
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
    // After any ERP action (確定, a tab, opening a form) ERP rebuilds its windows for a while;
    // every lookup made right after one polls up to this long instead of looking once
    // (V0.4.0 LOG, 2026-10-16: 資料瀏覽 looked up 0.1 s after 確定 did not exist yet).
    private const int AfterActionWaitMs = 10000;

    private readonly AppLogger _log;
    private readonly PaddleOcrService _ocr;
    /// <summary>
    /// The ERP form the running query drives (INVI02, COPI01 or PURI01). Set by each public query under the
    /// automation gate, so only one query uses it at a time.
    /// </summary>
    private ErpForm _form = ErpForm.Invi02;

    public ItemQueryService(AppLogger log)
    {
        _log = log;
        _ocr = new PaddleOcrService(log);
    }

    public Task<ItemQueryResult> QueryAsync(string itemCode, ItemQueryOptions options, IProgress<string> progress, CancellationToken cancellationToken) =>
        GuardErpMessagesAsync(() => QueryCoreAsync(itemCode, options, progress, cancellationToken));

    private async Task<ItemQueryResult> QueryCoreAsync(string itemCode, ItemQueryOptions options, IProgress<string> progress, CancellationToken cancellationToken)
    {
        if (!ItemCodeRules.TryNormalize(itemCode, out var code, out var error))
            throw new InvalidOperationException(error);

        using var gate = ErpAutomationGate.Enter();
        _form = ErpForm.Invi02;
        ExportFiles.CleanUp(_log);
        var watch = Stopwatch.StartNew();
        _log.Info("query", $"start item={_log.Value(code)}");

        if (await FindRecordAsync(code, watch, progress, cancellationToken) is not { } found)
            return ItemQueryResult.NotFound(code, watch.Elapsed);
        var (root, header) = found;

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

    /// <summary>
    /// The query part every form shares (INVI02, COPI01, PURI01): find the form (opening it through 作業速查
    /// when needed), maximize, F6 → 一般選項 code = value → 確定, wait for the record, then show
    /// the export tab. Null when ERP has no such code.
    /// </summary>
    private async Task<(nint Root, HeaderControls Header)?> FindRecordAsync(string code, Stopwatch watch,
        IProgress<string> progress, CancellationToken cancellationToken)
    {
        progress.Report($"ERP：尋找{_form.Title}…");
        var root = await ResolveFormAsync(progress, cancellationToken);

        // Same as CYERPAutoInput with COPI08: always maximize so every geometry is the same.
        NativeMethods.ShowWindow(root, NativeMethods.SW_MAXIMIZE);
        await Pace.Delay(MaximizeSettleMs, cancellationToken);
        if (!Win32Automation.PrepareForeground(root, _log))
            throw new InvalidOperationException($"無法把{_form.Title}帶到前景。");

        var page = FindMainPageControl(root, _form);

        progress.Report("ERP：開啟查詢條件（F6）…");
        var dialog = await OpenQueryDialogAsync(root, page, cancellationToken);

        // A 品名 search leaves its 進階設定 condition in the dialog; it must not narrow this query.
        await ClearAdvancedConditionAsync(dialog, cancellationToken);
        progress.Report($"ERP：輸入一般選項{_form.CodeName}…");
        await FillGeneralQueryAsync(dialog, code, cancellationToken);

        progress.Report("ERP：查詢中…");
        await ConfirmQueryAsync(root, dialog, cancellationToken);
        var header = await WaitForRecordAsync(root, page, code, cancellationToken);
        if (header is null)
        {
            _log.Info("query", $"not found {_form.Code} code={_log.Value(code)} elapsed_ms={watch.ElapsedMilliseconds}");
            return null;
        }

        // Exported from 資料瀏覽 the workbook lacks 單身資料 (庫別); from 基本資料1 it has both
        // sheets (user, 2026-10-16).
        // ERP ends a query on 資料瀏覽. When the record asked for was already shown, the header
        // matches before ERP has finished, and 基本資料1 still looked shown at that moment, so
        // it was not clicked and the export came from 資料瀏覽 (V0.2.0 Build 1, 2026-10-16).
        // So: wait for 資料瀏覽 first, then always click 基本資料1.
        progress.Report($"ERP：切換到{_form.ExportTab}…");
        var browseSheet = await WaitForTabSheetAsync(root, ErpForm.BrowseTab, QuerySettleWaitMs, cancellationToken, visible: true);
        _log.Info("tab", $"after query 資料瀏覽 shown={browseSheet is { } b && NativeMethods.IsWindowVisible(b.Handle)}");
        var sheet = await WaitForTabSheetAsync(root, _form.ExportTab, AfterActionWaitMs, cancellationToken)
            ?? throw new InvalidOperationException($"找不到 ERP 頁籤「{_form.ExportTab}」。");
        await Pace.Delay(300, cancellationToken);
        await ActivateTabAsync(root, page, _form.Tabs, _form.ExportTab, sheet.Handle, cancellationToken, force: true);

        return (root, header);
    }

    private async Task<ItemQueryResult> ExportAndReadAsync(nint root, HeaderControls header, string code, string path,
        Stopwatch watch, IProgress<string> progress, CancellationToken cancellationToken)
    {
        // Exporting from any tab but 基本資料1 loses 單身資料; checked again right before.
        var exportSheet = Win32Automation.FindTabSheet(root, _form.ExportTab);
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
            var form = ErpForm.Invi02; // the status badge is about INVI02
            var windows = Win32Automation.FindFormWindows(form);
            if (windows.Count == 0) return Invi02Status.NotOpen;
            // With several, a query uses one in browse state (user, 2026-10-16).
            if (windows.Count > 1) return windows.Any(w => IsInBrowseState(w, form)) ? Invi02Status.Browse : Invi02Status.Multiple;
            var root = windows[0];
            if (!NativeMethods.IsWindowEnabled(root)) return Invi02Status.DialogOpen;
            var header = ResolveHeader(root, FindMainPageControl(root, form), form);
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
    private async Task<nint> ResolveFormAsync(IProgress<string> progress, CancellationToken cancellationToken)
    {
        await CloseLeftoverInvq01Async(progress, cancellationToken);
        var windows = Win32Automation.FindFormWindows(_form);
        if (windows.Count == 0)
        {
            progress.Report($"ERP：開啟{_form.Title}（作業速查）…");
            await OpenFormAsync(cancellationToken);
            windows = Win32Automation.FindFormWindows(_form);
            if (windows.Count == 0)
                throw new QueryFailure(ErpAgentErrorCode.ERP_NOT_OPEN, $"已從作業速查開啟 {_form.Code}，但找不到{_form.Title}視窗。");
        }

        nint root;
        if (windows.Count == 1) root = windows[0];
        else
        {
            var browse = windows.Where(w => IsInBrowseState(w, _form)).ToList();
            _log.Info("window", $"{_form.Code} windows={windows.Count} browse={browse.Count}");
            if (browse.Count == 0)
                throw new QueryFailure(ErpAgentErrorCode.ERP_STATE, $"開著 {windows.Count} 個{_form.Title}，但沒有一個在檢視狀態。\n請把其中一個儲存或取消後再查詢。");
            root = browse[0];
        }
        _log.Info("window", $"{_form.Code} found class={NativeMethods.ClassName(root)} visible={NativeMethods.IsWindowVisible(root)}");
        return root;
    }

    /// <summary>Browse state: no dialog over the form and the header 品號 read-only.</summary>
    private static bool IsInBrowseState(nint root, ErpForm form)
    {
        try
        {
            if (!NativeMethods.IsWindowEnabled(root)) return false;
            var header = ResolveHeader(root, FindMainPageControl(root, form), form);
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
    private Task OpenFormAsync(CancellationToken cancellationToken) =>
        OpenFormAsync(_form.Code, _form.Title, () => Win32Automation.FindFormWindows(_form).Count > 0, cancellationToken);

    private async Task OpenFormAsync(string code, string title, Func<bool> opened, CancellationToken cancellationToken)
    {
        var mains = Win32Automation.FindErpMainWindows();
        if (mains.Count == 0 && Win32Automation.FindMinimizedErp() is var (app, hidden))
        {
            // Minimized ERP: its main form is hidden (user, 2026-10-16: only found when the
            // main window was open). Restore ERP and wait for the main form to show.
            _log.Info("window", $"ERP minimized; restoring app_iconic={NativeMethods.IsIconic(app)} main_visible={NativeMethods.IsWindowVisible(hidden)}");
            NativeMethods.ShowWindow(app, NativeMethods.SW_RESTORE);
            if (!await WaitUntilAsync(() => NativeMethods.IsWindowVisible(hidden) && !NativeMethods.IsIconic(hidden), 3000, cancellationToken))
                NativeMethods.ShowWindow(hidden, NativeMethods.SW_RESTORE);
            var shown = await WaitUntilAsync(() => NativeMethods.IsWindowVisible(hidden) && !NativeMethods.IsIconic(hidden), AfterActionWaitMs, cancellationToken);
            _log.Info("window", $"ERP main form restored={shown}");
            await Pace.Delay(500, cancellationToken); // let the main form finish painting its Ribbon
            mains = Win32Automation.FindErpMainWindows();
        }
        if (mains.Count == 0)
        {
            NativeMethods.EnumWindows((hwnd, _) =>
            {
                if (Invi02Layout.IsErpMainTitle(NativeMethods.WindowText(hwnd)))
                {
                    NativeMethods.GetWindowRect(hwnd, out var r);
                    _log.Warn("window", $"main window candidate skipped class={NativeMethods.ClassName(hwnd)} visible={NativeMethods.IsWindowVisible(hwnd)} rect={r.Left},{r.Top},{r.Width}x{r.Height}");
                }
                return true;
            }, 0);
            throw new QueryFailure(ErpAgentErrorCode.ERP_NOT_OPEN, $"找不到 SMART ERP 主視窗（鼎新電腦Conductor），也沒有開著{title}。\n請先登入 ERP。");
        }
        var main = mains[0];
        _log.Info("window", $"opening {code} from main window class={NativeMethods.ClassName(main)} title=\"{NativeMethods.WindowText(main)}\" count={mains.Count}");
        if (!NativeMethods.IsWindowEnabled(main))
            throw new QueryFailure(ErpAgentErrorCode.ERP_STATE, $"ERP 主視窗目前有對話框開著，無法開啟{title}。");
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
        InputSender.UnicodeText(code);
        await Pace.Delay(800, cancellationToken); // let ERP list the matching programs
        var typed = (NativeMethods.ControlText(focus) ?? string.Empty).Trim();
        if (!typed.Equals(code, StringComparison.OrdinalIgnoreCase))
        {
            _log.Warn("window", $"作業速查 read-back len={typed.Length} class={NativeMethods.ClassName(focus)}");
            throw new QueryFailure(ErpAgentErrorCode.ERP_STATE, $"作業速查的搜尋框讀回內容不是 {code}，已停止。");
        }
        InputSender.Press(NativeMethods.VK_RETURN);
        _log.Info("window", $"作業速查 {code} + Enter sent");

        // Opening a program form can take a while on the first run.
        var started = Environment.TickCount64;
        if (!await WaitUntilAsync(() => opened(), 30000, cancellationToken))
        {
            LogProcessWindows(main, "after-作業速查");
            throw new QueryFailure(ErpAgentErrorCode.ERP_NOT_OPEN, $"從作業速查開啟{title}逾時（30 秒）。");
        }
        _log.Info("window", $"{code} opened after_ms={Environment.TickCount64 - started}");
        await Pace.Delay(1500, cancellationToken); // let the new form load its first record
    }

    private static WindowControl FindMainPageControl(nint root, ErpForm form)
    {
        var first = Win32Automation.FindTabSheet(root, form.ExportTab)
            ?? throw new InvalidOperationException($"找不到 ERP 頁籤「{form.ExportTab}」。");
        var pageHandle = NativeMethods.GetParent(first.Handle);
        return Win32Automation.EnumerateChildren(root).FirstOrDefault(c => c.Handle == pageHandle)
            ?? throw new InvalidOperationException($"找不到 ERP {form.Title}頁籤的 TcxPageControl。");
    }

    // ---- header -----------------------------------------------------------------------

    private sealed record HeaderControls(WindowControl ItemCode);

    /// <summary>
    /// The header is every input above the tab page control. Its first row starts with the code
    /// (INVI02 品號, then 品名 and 規格 rows; COPI01 客戶代號＋簡稱, then 全名). Checkboxes such
    /// as 定重 are left out of that row structure.
    /// </summary>
    private static HeaderControls ResolveHeader(nint root, WindowControl page, ErpForm form)
    {
        var inputs = Win32Automation.InputControls(root, visibleOnly: true)
            .Where(c => c.Rect.Bottom <= page.Rect.Top && !Win32Automation.IsInside(c.Handle, page.Handle))
            .ToList();
        var rows = Invi02Layout.BuildRows(
            inputs.Where(c => !Invi02Layout.IsCheckBox(c.ClassName)),
            c => c.Rect.ToRectangle());
        if (rows.Count < form.HeaderRows)
            throw new InvalidOperationException($"ERP {form.Title}表頭結構不符預期（只找到 {rows.Count} 列欄位）。");
        return new HeaderControls(rows[0][0]);
    }

    /// <summary>The header, polled while ERP is still building the form (just opened, maximized or re-queried).</summary>
    private async Task<HeaderControls> ResolveHeaderAsync(nint root, WindowControl page, CancellationToken cancellationToken)
    {
        var deadline = Environment.TickCount64 + AfterActionWaitMs;
        while (true)
        {
            try
            {
                return ResolveHeader(root, page, _form);
            }
            catch (InvalidOperationException) when (Environment.TickCount64 < deadline)
            {
                await Pace.Delay(150, cancellationToken);
            }
        }
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
            throw new QueryFailure(ErpAgentErrorCode.ERP_STATE, $"ERP {_form.Title}目前有其他對話框開著，請先處理後再查詢。");

        var header = await ResolveHeaderAsync(root, page, cancellationToken);
        if (header.ItemCode.ClassName.Equals("TDBEdit", StringComparison.OrdinalIgnoreCase) &&
            !Win32Automation.IsReadOnly(header.ItemCode.Handle))
            throw new QueryFailure(ErpAgentErrorCode.ERP_STATE, $"ERP {_form.Title}目前在新增或修改狀態。\n請先儲存或取消後再查詢。");

        if (!Win32Automation.PrepareForeground(root, _log))
            throw new InvalidOperationException($"按 F6 前無法把{_form.Title}帶到前景。");
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
        throw new QueryFailure(ErpAgentErrorCode.ERP_STATE, $"已按 F6，但 10 秒內沒有找到「設定查詢條件」視窗。\n請確認{_form.Title}在檢視狀態；若視窗已出現，請把 LOG 傳給開發人員。");
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
        var sheet = await WaitForTabSheetAsync(dialog, Invi02Layout.GeneralQueryTab, AfterActionWaitMs, cancellationToken)
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

            // Pasted, not typed (user, 2026-10-16): the box's whole text is replaced.
            await PasteIntoAsync(focus, code, cancellationToken);

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
            await ThrowIfErpMessageAsync(root);
            if (Environment.TickCount64 > deadline)
                throw new InvalidOperationException("按「確定」後查詢視窗沒有關閉，請人工確認 ERP 畫面。");
            await Pace.Delay(100, cancellationToken);
        }

        // A message box (for example nothing found) keeps the form disabled.
        deadline = Environment.TickCount64 + 3000;
        while (!NativeMethods.IsWindowEnabled(root))
        {
            await ThrowIfErpMessageAsync(root);
            if (Environment.TickCount64 > deadline)
                throw new InvalidOperationException("查詢後 ERP 被其他視窗擋住，請人工確認 ERP 畫面。");
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
            await ThrowIfErpMessageAsync(root);
            HeaderControls header;
            try
            {
                header = ResolveHeader(root, page, _form);
            }
            catch (InvalidOperationException) when (Environment.TickCount64 < deadline)
            {
                // ERP is rebuilding the form after 確定 (V0.4.0 LOG: 0 rows 0.2 s after 確定).
                emptySince = null;
                await Pace.Delay(100, cancellationToken);
                continue;
            }
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
    /// <summary>
    /// Clicks <paramref name="point"/> until <paramref name="box"/> has the focus. ERP checks the
    /// field being left before focus moves (V0.9.0 Build 3／4 LOG: right after 客戶代號起, the
    /// 客戶代號迄 and 單據日期 clicks found focus still on the previous TYenMaskEdit), so each try
    /// waits for the focus, longer every time; up to three clicks, each miss logged.
    /// </summary>
    private async Task<nint> ClickUntilFocusedAsync(nint root, WindowControl box, Point point, string what, CancellationToken cancellationToken)
    {
        bool Focused(nint f) => f == box.Handle || Win32Automation.IsInside(f, box.Handle);
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            if (attempt > 1) Win32Automation.PrepareForeground(root, _log);
            SafeClick(root, point);
            nint focus = 0;
            if (await WaitUntilAsync(() => Focused(focus = NativeMethods.FocusedControlOfForeground()), 400 * attempt, cancellationToken))
                return focus;
            _log.Warn("focus", $"{what} attempt={attempt} focus class={(focus != 0 ? NativeMethods.ClassName(focus) : "-")} same_root={(focus != 0 && Win32Automation.IsInside(focus, root))} foreground=\"{NativeMethods.WindowText(NativeMethods.GetForegroundWindow())}\"");
        }
        throw new InvalidOperationException($"{what}點了三次都沒有取得焦點，已停止，避免輸入到錯誤欄位。");
    }

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
    /// <param name="allMatches">
    /// 「轉出全部符合查詢條件的資料」 instead of 「轉出至 Excel」: 資料瀏覽 loads records in pages and
    /// 轉出至 only exports the loaded ones (user, 2026-10-16: 針灸針 gave 12, more appeared after
    /// scrolling past the last row), so a 品名 search needs the export of everything matching.
    /// </param>
    private async Task ExportToExcelAsync(nint root, string path, CancellationToken cancellationToken, bool headerOnly = false, bool allMatches = false)
    {
        var before = Win32Automation.VisibleProcessWindows(root);
        var labelScreen = await RibbonClickAsync(root, "Excel", "工具列的「Excel」（請確認 Ribbon 停在「常用」）", cancellationToken,
            alternative: "EXCEL", requireChinese: false);
        _log.Info("export", $"ribbon Excel clicked label=({labelScreen.Left},{labelScreen.Top},{labelScreen.Width},{labelScreen.Height})");

        // The drop-down: 轉出至 Excel / 轉出全部符合查詢條件的資料.
        var near = new Rectangle(labelScreen.Left - 120, labelScreen.Top - 60, 480, 220);
        var (menuWord, menuName) = allMatches ? ("轉出全部", "轉出全部符合查詢條件的資料") : ("轉出至", "轉出至 Excel");
        var item = await FindMenuItemAsync(root, near, menuWord, cancellationToken);
        if (item is null)
        {
            // Close a drop-down left open; Esc only when ERP really shows a popup, so a stray
            // Esc never reaches the INVI02 form itself (CYERPAutoInput rule).
            if (Win32Automation.ProcessPopupsNear(root, near).Count > 0) InputSender.Press(NativeMethods.VK_ESCAPE);
            throw new InvalidOperationException($"Excel 選單沒有出現「{menuName}」。");
        }
        InputSender.Click(item.Value);
        _log.Info("export", $"{menuName} clicked");

        // 匯出設定 is recognised by its 儲存檔案 path box, or by the INVI02 caption it carries.
        await CompleteExportAsync(root, before, path, _form.IsTitle, strict: true, cancellationToken, headerOnly);
    }

    /// <summary>
    /// Everything after the export command: the export dialog (ERP's 匯出設定, or a Windows
    /// 另存新檔 dialog), the path, 確定, and the end of the export. <paramref name="strict"/> is
    /// INVI02's known flow: xlsx format and 單頭／單身 全選 are required, and so is the
    /// 「檢視Excel資料?」 question. INVQ01's 轉出到Excel dialog has not been seen yet, so there
    /// whatever applies is done and the end is the file written with ERP idle again.
    /// </summary>
    private async Task CompleteExportAsync(nint root, IReadOnlySet<nint> before, string path, Func<string, bool> ownTitle, bool strict, CancellationToken cancellationToken, bool headerOnly = false)
    {
        var found = await WaitNewWindowAsync(root, before, h => IsExportDialog(h, ownTitle), ExportDialogWaitMs, cancellationToken);
        if (found is not { } dialog)
        {
            LogProcessWindows(root, "after-export-command");
            throw new InvalidOperationException("按下轉出 Excel 後沒有出現匯出設定視窗。");
        }
        await Pace.Delay(500, cancellationToken); // let ERP finish building the dialog
        if (IsSaveDialog(dialog)) await FillSaveDialogAsync(dialog, path, cancellationToken);
        else await FillExportDialogAsync(dialog, path, strict, cancellationToken, headerOnly);
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
    private async Task FillExportDialogAsync(nint dialog, string path, bool strict, CancellationToken cancellationToken, bool headerOnly = false)
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

        // A 品名 search (headerOnly, 轉出全部) has not been seen on site: its dialog may have no
        // 單頭／單身 tabs, so there they are used only when present.
        if ((strict && !headerOnly) || Win32Automation.FindTabSheet(dialog, Invi02Layout.ExportFieldTabs.Order[0]) is not null)
            await SelectAllExportFieldsAsync(dialog, cancellationToken, headerOnly);
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
    /// <param name="headerOnly">Only 單頭 (a 品名 search exports the 資料瀏覽 list, which has no 單身).</param>
    private async Task SelectAllExportFieldsAsync(nint dialog, CancellationToken cancellationToken, bool headerOnly = false)
    {
        var page = Win32Automation.EnumerateChildren(dialog)
            .Where(c => c.Visible && c.ClassName.Equals("TcxPageControl", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault(c => Win32Automation.EnumerateChildren(c.Handle).Any(s => s.ClassName.Contains("TabSheet", StringComparison.OrdinalIgnoreCase)));
        if (page is null) LogChildren(dialog, "export-dialog");
        if (!NativeMethods.GetWindowRect(dialog, out var d))
            throw new InvalidOperationException("讀不到匯出設定視窗位置。");
        foreach (var tab in headerOnly ? Invi02Layout.ExportFieldTabs.Order.Take(1) : Invi02Layout.ExportFieldTabs.Order)
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

    /// <summary>
    /// The tab sheet <paramref name="caption"/> of <paramref name="window"/>, polled until it
    /// exists (and, with <paramref name="visible"/>, is shown) or the wait ends. Returns the
    /// sheet if it exists at the end even when not shown, else null.
    /// </summary>
    private static async Task<WindowControl?> WaitForTabSheetAsync(nint window, string caption, int timeoutMs, CancellationToken cancellationToken, bool visible = false)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (true)
        {
            var sheet = Win32Automation.FindTabSheet(window, caption);
            if (sheet is not null && (!visible || NativeMethods.IsWindowVisible(sheet.Handle))) return sheet;
            if (Environment.TickCount64 > deadline) return sheet;
            await Pace.Delay(100, cancellationToken);
        }
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
    private async Task PasteIntoAsync(nint box, string text, CancellationToken cancellationToken, bool selectAll = true)
    {
        IDataObject? saved = null;
        try { saved = Clipboard.GetDataObject(); } catch (ExternalException) { }
        try
        {
            Clipboard.SetText(text);
            // A masked box is pasted at the caret (after Home) instead of over a selection.
            if (selectAll) NativeMethods.SendMessage(box, NativeMethods.EM_SETSEL, 0, -1);
            NativeMethods.SendMessage(box, NativeMethods.WM_PASTE, 0, 0);
            await Pace.Delay(150, cancellationToken);
        }
        finally
        {
            // Another program may hold the clipboard for a moment (V0.4.0 LOG): try a few times.
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    if (saved is not null) Clipboard.SetDataObject(saved, copy: true, retryTimes: 5, retryDelay: 50);
                    else Clipboard.Clear();
                    break;
                }
                catch (ExternalException ex)
                {
                    if (attempt >= 3)
                    {
                        _log.Warn("export", $"clipboard not restored: {ex.GetType().Name}");
                        break;
                    }
                    Thread.Sleep(100);
                }
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

    // ---- ERP's own messages ----------------------------------------------------------

    /// <summary>
    /// A failure while ERP shows its own message box is reported with that message (user,
    /// 2026-10-16: when ERP itself reports an error the program must say so clearly), not as
    /// whatever step happened to fail next. The box is left for the user; nothing is clicked.
    /// </summary>
    private async Task<T> GuardErpMessagesAsync<T>(Func<Task<T>> run)
    {
        try
        {
            return await run();
        }
        catch (InvalidOperationException ex) when (ex is not QueryFailure { FromErpMessage: true })
        {
            var anyErpWindow = Win32Automation.FindFormWindows(_form).Concat(Win32Automation.FindErpMainWindows()).FirstOrDefault();
            if (anyErpWindow != 0 && FindErpMessageBox(anyErpWindow) is var box && box != 0)
                await ThrowErpMessageAsync(box, null, ex);
            throw;
        }
    }

    /// <summary>A visible ERP message box (Delphi TMessageForm or Windows #32770 with a button) of the ERP process, or 0.</summary>
    private static nint FindErpMessageBox(nint anyErpWindow)
    {
        foreach (var h in Win32Automation.VisibleProcessWindows(anyErpWindow))
            if (Invi02Layout.IsMessageDialogClass(NativeMethods.ClassName(h)) && FindButton(h, _ => true) is not null)
                return h;
        return 0;
    }

    /// <summary>Stops with ERP's message when ERP is showing one.</summary>
    private async Task ThrowIfErpMessageAsync(nint anyErpWindow)
    {
        if (FindErpMessageBox(anyErpWindow) is var box && box != 0)
            await ThrowErpMessageAsync(box, null);
    }

    private async Task ThrowErpMessageAsync(nint box, string? when, Exception? inner = null)
    {
        var text = await ReadErpMessageAsync(box);
        _log.Warn("erp-message", $"title=\"{NativeMethods.WindowText(box)}\" text={_log.Value(text)}");
        LogChildren(box, "erp-message");
        var shown = text.Length > 0 ? $"「{text}」" : "（讀不到內容，請看 ERP 畫面）";
        throw new QueryFailure(ErpAgentErrorCode.ERP_STATE,
            $"{when ?? string.Empty}ERP 顯示了訊息：{shown}\n查詢已停止，請先在 ERP 處理這個訊息後再查詢。", inner)
        { FromErpMessage = true };
    }

    /// <summary>
    /// The message text: the box's own text windows when ERP made them windows, otherwise OCR of
    /// the box (Delphi's TMessageForm draws its text with a TLabel, which is not a window).
    /// Button captions are left out.
    /// </summary>
    private async Task<string> ReadErpMessageAsync(nint box)
    {
        static bool IsButtonWord(string t) => Invi02Layout.NormalizeCaption(t) is var c &&
            (c.StartsWith("確定", StringComparison.Ordinal) || c.StartsWith("取消", StringComparison.Ordinal) ||
             c.StartsWith("是", StringComparison.Ordinal) || c.StartsWith("否", StringComparison.Ordinal) ||
             c is "OK" or "YES" or "NO" or "CANCEL");
        var texts = Win32Automation.EnumerateChildren(box)
            .Where(c => c.Visible && !IsButtonClass(c.ClassName) && c.Text.Trim().Length > 0)
            .Select(c => c.Text.Trim())
            .ToList();
        // A box just shown may not have painted its text yet (V0.9.0 Build 2 LOG: 0 tokens), so
        // OCR is tried again after a pause, the second time without requiring Chinese.
        for (var attempt = 0; texts.Count == 0 && attempt < 3 && NativeMethods.GetWindowRect(box, out var r); attempt++)
        {
            try
            {
                if (attempt > 0) await Task.Delay(350);
                IReadOnlyList<OcrToken> tokens;
                using (var image = ScreenCapture.Capture(r.ToRectangle()))
                    tokens = await _ocr.RecognizeAsync(image, CancellationToken.None, requireChinese: attempt < 2);
                texts = tokens.Where(t => !IsButtonWord(t.Text))
                    .OrderBy(t => t.Rect.Top / 12).ThenBy(t => t.Rect.Left)
                    .Select(t => t.Text.Trim()).Where(t => t.Length > 0).ToList();
            }
            catch (Exception ex) when (ex is InvalidOperationException or ExternalException)
            {
                _log.Warn("erp-message", $"OCR failed: {ex.GetType().Name}");
                break;
            }
        }
        var text = string.Join(" ", texts);
        return text.Length > 0 ? text : NativeMethods.WindowText(box).Trim();
    }

    /// <summary>
    /// The text in a message box's body: its client area above the buttons, so the caption (ERP puts
    /// the report's name there) is left out. Read as windows first, then by OCR twice (the second
    /// time without requiring Chinese). Empty when the body shows nothing.
    /// </summary>
    private async Task<string> ReadMessageBodyAsync(nint box)
    {
        var statics = Win32Automation.EnumerateChildren(box)
            .Where(c => c.Visible && !IsButtonClass(c.ClassName) && c.Text.Trim().Length > 0).Select(c => c.Text.Trim()).ToList();
        if (statics.Count > 0) return string.Join(" ", statics);
        if (!NativeMethods.GetClientRect(box, out var client)) return "?";
        var origin = new NativeMethods.POINT();
        if (!NativeMethods.ClientToScreen(box, ref origin)) return "?";
        var bottom = origin.Y + client.Height;
        var buttonTops = Win32Automation.EnumerateChildren(box).Where(c => c.Visible && IsButtonClass(c.ClassName)).Select(c => c.Rect.Top).ToList();
        if (buttonTops.Count > 0) bottom = Math.Min(bottom, buttonTops.Min() - 2);
        var region = new Rectangle(origin.X, origin.Y, client.Width, bottom - origin.Y);
        if (region.Width < 8 || region.Height < 8) return string.Empty;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                if (attempt > 0) await Task.Delay(300);
                IReadOnlyList<OcrToken> tokens;
                using (var image = ScreenCapture.Capture(region))
                    tokens = await _ocr.RecognizeAsync(image, CancellationToken.None, requireChinese: attempt == 0);
                var text = string.Join(" ", tokens.Select(t => t.Text.Trim()).Where(t => t.Length > 0));
                if (text.Length > 0) return text;
            }
            catch (Exception ex) when (ex is InvalidOperationException or ExternalException)
            {
                _log.Warn("erp-message", $"body OCR failed: {ex.GetType().Name}");
                return "?";
            }
        }
        return string.Empty;
    }

    // ---- 客戶 (COPI01)／廠商 (PURI01) ----------------------------------------------------

    /// <summary>
    /// One customer from COPI01 or supplier from PURI01 (user, 2026-10-16／17): the same flow as a
    /// 品號 — F6 → 一般選項 代號 = code → 確定 → 基本資料 → 轉出至 Excel. Both export one sheet
    /// (單頭資料) only.
    /// </summary>
    public Task<PartyQueryResult> QueryPartyAsync(ErpParty party, string input, IProgress<string> progress, CancellationToken cancellationToken) =>
        GuardErpMessagesAsync(() => QueryPartyCoreAsync(party, input, progress, cancellationToken));

    private async Task<PartyQueryResult> QueryPartyCoreAsync(ErpParty party, string input, IProgress<string> progress, CancellationToken cancellationToken)
    {
        if (!ItemCodeRules.TryNormalize(input, out var code, out var error, party.CodeName))
            throw new InvalidOperationException(error);

        using var gate = ErpAutomationGate.Enter();
        _form = party.Form;
        ExportFiles.CleanUp(_log);
        var watch = Stopwatch.StartNew();
        _log.Info("party", $"start form={_form.Code} code={_log.Value(code)}");

        if (await FindRecordAsync(code, watch, progress, cancellationToken) is not { } found)
            return PartyQueryResult.NotFound(party, code, watch.Elapsed);
        var (root, header) = found;

        progress.Report("ERP：轉出 Excel…");
        var path = ExportFiles.NewPath(_form.Code);
        try
        {
            var exportSheet = Win32Automation.FindTabSheet(root, _form.ExportTab);
            if (exportSheet is null || !NativeMethods.IsWindowVisible(exportSheet.Handle))
                throw new InvalidOperationException($"轉出前 ERP 不在「{_form.ExportTab}」頁籤，已停止。");
            await ExportToExcelAsync(root, path, cancellationToken, headerOnly: true);
            if (!ItemCodeRules.SameCode(NativeMethods.ControlText(header.ItemCode.Handle), code))
                throw new InvalidOperationException($"轉出途中 ERP 顯示的{party.CodeName}改變了；結果不採用，請重新查詢。");
            progress.Report("讀取匯出資料…");
            var sheets = await ExportFiles.ReadAsync(path, cancellationToken);
            var fields = PartyExport.Parse(sheets, party, code);
            _log.Info("party", $"done form={_form.Code} code={_log.Value(code)} fields={fields.Count} elapsed_ms={watch.ElapsedMilliseconds}");
            return new PartyQueryResult(party, code, fields, watch.Elapsed);
        }
        catch (InvalidOperationException ex) when (ex is not QueryFailure)
        {
            throw new QueryFailure(ErpAgentErrorCode.EXPORT_FAILED, ex.Message, ex);
        }
        finally
        {
            ExportFiles.Delete(path, _log);
        }
    }

    // ---- 品名 search ---------------------------------------------------------------------

    /// <summary>
    /// Lists the items whose 品名 contains <paramref name="keywordInput"/> (user, 2026-10-16):
    /// F6 → 進階設定 → paste 「(INVMB.MB002 like N'%關鍵字%')」 into the condition box → 確定 →
    /// ERP lists the matches on 資料瀏覽 (empty when none) → 轉出至 Excel from 資料瀏覽. Read-only
    /// like the 品號 query. Every listed 品名 must contain the keyword, or the result is refused.
    /// </summary>
    public Task<ItemSearchResult> SearchByNameAsync(string keywordInput, IProgress<string> progress, CancellationToken cancellationToken) =>
        SearchAsync(ErpForm.Invi02, keywordInput, ItemNameRules.Condition, ItemSearchExport.Parse, progress, cancellationToken);

    /// <summary>
    /// Customers or suppliers whose 簡稱 or 全名 contains the keyword (user, 2026-10-16: always
    /// both, so a name written in either is found), from COPI01／PURI01 the same way as the 品名 search.
    /// </summary>
    public Task<ItemSearchResult> SearchPartiesAsync(ErpParty party, string keywordInput, IProgress<string> progress, CancellationToken cancellationToken) =>
        SearchAsync(party.Form, keywordInput, party.NameCondition, (sheets, keyword) => PartyExport.ParseList(sheets, party, keyword),
            progress, cancellationToken);

    private Task<ItemSearchResult> SearchAsync(ErpForm form, string keywordInput, Func<string, string> condition,
        Func<IReadOnlyList<(string Name, List<string[]> Rows)>, string, IReadOnlyList<IReadOnlyList<ErpField>>> parse,
        IProgress<string> progress, CancellationToken cancellationToken) =>
        GuardErpMessagesAsync(() => SearchCoreAsync(form, keywordInput, condition, parse, progress, cancellationToken));

    private async Task<ItemSearchResult> SearchCoreAsync(ErpForm form, string keywordInput, Func<string, string> condition,
        Func<IReadOnlyList<(string Name, List<string[]> Rows)>, string, IReadOnlyList<IReadOnlyList<ErpField>>> parse,
        IProgress<string> progress, CancellationToken cancellationToken)
    {
        // The keyword rules (no quote, no LIKE wildcard) hold for every form's condition text.
        if (!ItemNameRules.TryNormalize(keywordInput, out var keyword, out var error))
            throw new InvalidOperationException(error);

        using var gate = ErpAutomationGate.Enter();
        _form = form;
        ExportFiles.CleanUp(_log);
        var watch = Stopwatch.StartNew();
        _log.Info("search", $"start {_form.Code} keyword={_log.Value(keyword)}");

        progress.Report($"ERP：尋找{_form.Title}…");
        var root = await ResolveFormAsync(progress, cancellationToken);
        NativeMethods.ShowWindow(root, NativeMethods.SW_MAXIMIZE);
        await Pace.Delay(MaximizeSettleMs, cancellationToken);
        if (!Win32Automation.PrepareForeground(root, _log))
            throw new InvalidOperationException($"無法把{_form.Title}帶到前景。");
        var page = FindMainPageControl(root, _form);

        progress.Report("ERP：開啟查詢條件（F6）…");
        var dialog = await OpenQueryDialogAsync(root, page, cancellationToken);
        var limit = ReadRecordLimit(dialog);
        // An old code left in 一般選項 must not narrow the search.
        await ClearGeneralValueAsync(dialog, cancellationToken);
        progress.Report("ERP：輸入進階設定名稱條件…");
        await FillAdvancedConditionAsync(dialog, condition(keyword), cancellationToken);

        progress.Report("ERP：查詢中…");
        await ConfirmQueryAsync(root, dialog, cancellationToken);
        // ERP rebuilds the form after 確定; 資料瀏覽 appears (and shows) only after a while.
        var browse = await WaitForTabSheetAsync(root, ErpForm.BrowseTab, AfterActionWaitMs, cancellationToken, visible: true)
            ?? throw new InvalidOperationException($"查詢後 {AfterActionWaitMs / 1000} 秒內找不到 ERP 頁籤「{ErpForm.BrowseTab}」。");
        _log.Info("search", $"after query 資料瀏覽 shown={NativeMethods.IsWindowVisible(browse.Handle)}");
        if (!await HasRecordAsync(root, page, cancellationToken))
        {
            _log.Info("search", $"no match elapsed_ms={watch.ElapsedMilliseconds}");
            return new ItemSearchResult(keyword, [], limit, watch.Elapsed);
        }

        // The list is exported from 資料瀏覽 (one 單頭 row per item; user, 2026-10-16).
        await ActivateTabAsync(root, page, _form.Tabs, ErpForm.BrowseTab, browse.Handle, cancellationToken);
        progress.Report("ERP：轉出 Excel…");
        var path = ExportFiles.NewPath(_form.Code);
        try
        {
            await ExportToExcelAsync(root, path, cancellationToken, headerOnly: true, allMatches: true);
            progress.Report("讀取匯出資料…");
            var sheets = await ExportFiles.ReadAsync(path, cancellationToken);
            var items = parse(sheets, keyword);
            _log.Info("search", $"done items={items.Count} limit={limit?.ToString() ?? "?"} elapsed_ms={watch.ElapsedMilliseconds}");
            return new ItemSearchResult(keyword, items, limit, watch.Elapsed);
        }
        catch (InvalidOperationException ex) when (ex is not QueryFailure)
        {
            throw new QueryFailure(ErpAgentErrorCode.EXPORT_FAILED, ex.Message, ex);
        }
        finally
        {
            ExportFiles.Delete(path, _log);
        }
    }

    /// <summary>After a query: true when the header shows a record, false when it stays empty (nothing found).</summary>
    private async Task<bool> HasRecordAsync(nint root, WindowControl page, CancellationToken cancellationToken)
    {
        var deadline = Environment.TickCount64 + RecordWaitMs;
        bool? last = null;
        long since = Environment.TickCount64;
        while (true)
        {
            await ThrowIfErpMessageAsync(root);
            bool? has;
            try
            {
                has = (NativeMethods.ControlText(ResolveHeader(root, page, _form).ItemCode.Handle) ?? string.Empty).Trim().Length > 0;
            }
            catch (InvalidOperationException)
            {
                has = null; // the header is being rebuilt; look again
            }
            if (has != last)
            {
                last = has;
                since = Environment.TickCount64;
            }
            else if (has is { } stable && Environment.TickCount64 - since >= EmptyRecordStableMs) return stable;
            if (Environment.TickCount64 > deadline) return has ?? throw new InvalidOperationException("查詢後讀不到 ERP 表頭，請人工確認 ERP 畫面。");
            await Pace.Delay(100, cancellationToken);
        }
    }

    /// <summary>設定查詢條件's 總資料筆數限制 (a combo holding a number, e.g. 500), or null when not readable.</summary>
    private static int? ReadRecordLimit(nint dialog)
    {
        foreach (var c in Win32Automation.InputControls(dialog, visibleOnly: true).Where(c => Invi02Layout.IsCombo(c.ClassName)))
        {
            var text = (NativeMethods.ControlText(c.Handle) ?? string.Empty).Trim();
            if (text.Length is > 0 and <= 6 && text.All(char.IsAsciiDigit) && int.TryParse(text, out var limit)) return limit;
        }
        return null;
    }

    /// <summary>The dialog's TcxPageControl (一般選項／進階設定).</summary>
    private static WindowControl DialogPage(nint dialog)
    {
        var sheet = Win32Automation.FindTabSheet(dialog, Invi02Layout.GeneralQueryTab)
            ?? Win32Automation.FindTabSheet(dialog, Invi02Layout.AdvancedQueryTab)
            ?? throw new InvalidOperationException("「設定查詢條件」找不到頁籤。");
        var pageHandle = NativeMethods.GetParent(sheet.Handle);
        return Win32Automation.EnumerateChildren(dialog).FirstOrDefault(c => c.Handle == pageHandle)
            ?? throw new InvalidOperationException("「設定查詢條件」找不到頁籤控制項。");
    }

    /// <summary>The 進階設定 condition box: a memo, else the tallest input of that sheet.</summary>
    private static WindowControl? ConditionBox(nint advancedSheet)
    {
        var inputs = Win32Automation.InputControls(advancedSheet, visibleOnly: false);
        return inputs.FirstOrDefault(c => c.ClassName.Contains("Memo", StringComparison.OrdinalIgnoreCase) ||
                                          c.ClassName.Contains("RichEdit", StringComparison.OrdinalIgnoreCase))
            ?? inputs.Where(c => c.Rect.Height >= 40).OrderByDescending(c => c.Rect.Height).FirstOrDefault();
    }

    private static string ConditionText(WindowControl box) => (NativeMethods.ControlText(box.Handle) ?? string.Empty).Trim();

    /// <summary>進階設定 → 清除 (when a condition is there) → paste the condition → read back.</summary>
    private async Task FillAdvancedConditionAsync(nint dialog, string condition, CancellationToken cancellationToken)
    {
        if (!Win32Automation.PrepareForeground(dialog, _log))
            throw new InvalidOperationException("無法把「設定查詢條件」帶到前景。");
        await ShowLazyTabAsync(dialog, DialogPage(dialog), Invi02Layout.QueryDialogTabs, Invi02Layout.AdvancedQueryTab, "設定查詢條件", cancellationToken);
        var sheet = Win32Automation.FindTabSheet(dialog, Invi02Layout.AdvancedQueryTab)
            ?? throw new InvalidOperationException("「設定查詢條件」找不到「進階設定」頁籤。");
        if (ConditionBox(sheet.Handle) is not { } box)
        {
            LogChildren(sheet.Handle, "advanced");
            throw new InvalidOperationException("「進階設定」找不到條件框。");
        }
        await PressClearAsync(dialog, sheet.Handle, box, cancellationToken);

        SafeClick(dialog, Win32Automation.Center(box.Rect));
        await Pace.Delay(150, cancellationToken);
        var focus = NativeMethods.FocusedControlOfForeground();
        if (focus != box.Handle && !Win32Automation.IsInside(focus, box.Handle))
            throw new InvalidOperationException("無法把游標移到「進階設定」的條件框。");
        if (Win32Automation.IsReadOnly(focus))
        {
            LogChildren(sheet.Handle, "advanced");
            throw new InvalidOperationException("「進階設定」的條件框不能直接輸入，已停止。");
        }
        // Pasted, not typed (user, 2026-10-16).
        await PasteIntoAsync(focus, condition, cancellationToken);
        var shown = ConditionText(box);
        if (!ItemNameRules.SameCondition(shown, condition))
        {
            _log.Warn("search", $"condition read-back mismatch shown={_log.Value(shown)}");
            throw new InvalidOperationException("品名條件貼上後讀回的內容不一致，已停止。");
        }
        _log.Info("search", "advanced condition pasted");
    }

    /// <summary>「清除」 on 進階設定 when its condition box is not empty; confirmed by the box becoming empty.</summary>
    private async Task PressClearAsync(nint dialog, nint advancedSheet, WindowControl box, CancellationToken cancellationToken)
    {
        if (ConditionText(box).Length == 0) return;
        var clear = FindButton(advancedSheet, c => c.StartsWith("清除", StringComparison.Ordinal))
            ?? FindButton(dialog, c => c.StartsWith("清除", StringComparison.Ordinal))
            ?? throw new InvalidOperationException("「進階設定」找不到「清除」按鈕。");
        SafeClick(dialog, Win32Automation.Center(clear.Rect));
        if (!await WaitUntilAsync(() => ConditionText(box).Length == 0, 1500, cancellationToken))
            throw new InvalidOperationException("按「清除」後進階設定的條件沒有清空，已停止。");
        _log.Info("dialog", "advanced condition cleared");
    }

    /// <summary>Before a 品號 query: a 進階設定 condition left by a 品名 search is cleared.</summary>
    private async Task ClearAdvancedConditionAsync(nint dialog, CancellationToken cancellationToken)
    {
        if (Win32Automation.FindTabSheet(dialog, Invi02Layout.AdvancedQueryTab) is not { } sheet ||
            ConditionBox(sheet.Handle) is not { } box || ConditionText(box).Length == 0)
            return;
        if (!Win32Automation.PrepareForeground(dialog, _log))
            throw new InvalidOperationException("無法把「設定查詢條件」帶到前景。");
        await ShowLazyTabAsync(dialog, DialogPage(dialog), Invi02Layout.QueryDialogTabs, Invi02Layout.AdvancedQueryTab, "設定查詢條件", cancellationToken);
        await PressClearAsync(dialog, sheet.Handle, box, cancellationToken);
    }

    /// <summary>Before a 品名 search: an old 品號 in 一般選項 is erased (End + Backspace, read back empty).</summary>
    private async Task ClearGeneralValueAsync(nint dialog, CancellationToken cancellationToken)
    {
        if (Win32Automation.FindTabSheet(dialog, Invi02Layout.GeneralQueryTab) is not { } sheet) return;
        var rows = Invi02Layout.BuildRows(
            Win32Automation.InputControls(sheet.Handle, visibleOnly: false).Where(c => !Invi02Layout.IsCheckBox(c.ClassName)),
            c => c.Rect.ToRectangle());
        if (rows.Count == 0 || rows[0].Count != 2 || !Invi02Layout.IsEditBox(rows[0][1].ClassName)) return;
        var value = rows[0][1];
        var current = NativeMethods.ControlText(value.Handle) ?? string.Empty;
        if (current.Trim().Length == 0) return;

        if (!Win32Automation.PrepareForeground(dialog, _log))
            throw new InvalidOperationException("無法把「設定查詢條件」帶到前景。");
        await ActivateTabAsync(dialog, DialogPage(dialog), Invi02Layout.QueryDialogTabs, Invi02Layout.GeneralQueryTab, sheet.Handle, cancellationToken);
        SafeClick(dialog, Win32Automation.Center(value.Rect));
        await Pace.Delay(150, cancellationToken);
        var focus = NativeMethods.FocusedControlOfForeground();
        if (focus != value.Handle && !Win32Automation.IsInside(focus, value.Handle))
            throw new InvalidOperationException("無法把游標移到一般選項的品號欄。");
        InputSender.EndBackspace(current.Length + 2);
        await Pace.Delay(100, cancellationToken);
        if ((NativeMethods.ControlText(value.Handle) ?? string.Empty).Trim().Length != 0)
            throw new InvalidOperationException("一般選項的品號沒有清空，已停止。");
        _log.Info("dialog", "general 品號 cleared before 品名 search");
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
            var page = await FindInvq01PageControlAsync(invq, cancellationToken);

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
        var label = await RibbonClickAsync(invi02, Invq01Layout.LinkMenu, "工具列的「串查」", cancellationToken, group: Invq01Layout.LinkMenuGroup);

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

    /// <summary>
    /// The lower TcxPageControl (查各庫庫存量 … 查詢結果), or null when not found. ERP creates a
    /// sheet only when its tab is first shown (V0.3.0 LOG, 2026-10-16: only 查各庫庫存量 existed),
    /// so the page is found through any sheet that does exist.
    /// </summary>
    private async Task<WindowControl?> FindInvq01PageControlAsync(nint invq, CancellationToken cancellationToken)
    {
        WindowControl? sheet = null;
        await WaitUntilAsync(() => (sheet = Invq01Layout.Tabs.Order.Select(t => Win32Automation.FindTabSheet(invq, t)).FirstOrDefault(t => t is not null)) is not null,
            AfterActionWaitMs, cancellationToken);
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

        await RibbonClickAsync(invq, Invq01Layout.StartQuery, "工具列的「開始查詢」", cancellationToken, group: Invq01Layout.RunGroup);
        _log.Info("invq01", $"開始查詢 clicked tab={tab}");
        await WaitInvq01QueryAsync(invq, tab, cancellationToken);

        var path = ExportFiles.NewPath(Invq01Layout.FormCode);
        try
        {
            var before = Win32Automation.VisibleProcessWindows(invq);
            await RibbonClickAsync(invq, Invq01Layout.ExportButton, "工具列的「轉出到Excel」", cancellationToken, group: Invq01Layout.RunGroup);
            _log.Info("invq01", $"轉出到Excel clicked tab={tab}");
            await CompleteExportAsync(invq, before, path, Invq01Layout.IsInvq01Title, strict: false, cancellationToken);
            return await ExportFiles.ReadAsync(path, cancellationToken);
        }
        finally
        {
            ExportFiles.Delete(path, _log);
        }
    }

    /// <summary>
    /// Shows an INVQ01 tab: the measured position first, then OCR of the tab strip only (a whole
    /// window OCR took 2.6 s, V0.3.0 LOG). Shown means a visible sheet with that caption, which
    /// ERP creates on the first click.
    /// </summary>
    private async Task ShowInvq01TabAsync(nint invq, WindowControl? page, string tab, CancellationToken cancellationToken)
    {
        if (page is not null)
        {
            await ShowLazyTabAsync(invq, page, Invq01Layout.Tabs, tab, "庫存狀況查詢作業", cancellationToken);
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

    /// <summary>
    /// Shows a tab whose sheet window ERP may create only on the first click: the measured
    /// position first, then OCR of the tab strip only. Shown means a visible sheet with that
    /// caption.
    /// </summary>
    private async Task ShowLazyTabAsync(nint window, WindowControl page, TabStrip strip, string tab, string what, CancellationToken cancellationToken)
    {
        bool Shown() => Win32Automation.FindTabSheet(window, tab) is { } sheet && NativeMethods.IsWindowVisible(sheet.Handle);
        if (Shown()) return;
        if (!Win32Automation.PrepareForeground(window, _log))
            throw new InvalidOperationException($"切換頁籤「{tab}」前無法把{what}帶到前景。");
        var pageRect = page.Rect.ToRectangle();
        if (strip.PointFor(pageRect, tab) is { } point)
        {
            SafeClick(window, point);
            if (await WaitUntilAsync(Shown, TabWaitMs, cancellationToken))
            {
                _log.Info("tab", $"shown tab={tab} by=geometry");
                return;
            }
            _log.Warn("tab", $"geometry click did not show tab={tab} point={point.X - pageRect.Left},{point.Y - pageRect.Top}");
        }
        var area = new Rectangle(pageRect.Left, pageRect.Top, pageRect.Width, Math.Min(32, pageRect.Height));
        await OcrClickAsync(window, area, tab, $"{what}的頁籤「{tab}」", cancellationToken);
        if (!await WaitUntilAsync(Shown, TabWaitMs, cancellationToken))
            throw new InvalidOperationException($"已點擊{what}的頁籤「{tab}」，但頁籤沒有切換。");
        _log.Info("tab", $"shown tab={tab} by=ocr");
    }

    /// <summary>查歷史異動 選擇日期 起／迄: the two boxes of that group, left to right, each read back.</summary>
    private async Task FillHistoryRangeAsync(nint invq, HistoryRange range, CancellationToken cancellationToken)
    {
        if (range.From is null && range.To is null)
        {
            _log.Info("invq01", "history date fields not specified; leave both unchanged in ERP");
            return;
        }
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

        // ERP keyboard sequence (user, 2026-10-16), see Invq01Layout.DateActions:
        // both: click 起 -> yyyyMMdd -> Enter -> yyyyMMdd; 起 only: click 起 -> yyyyMMdd;
        // 迄 only: click 迄 -> yyyyMMdd. Never type in a field with unexpected focus.
        foreach (var action in Invq01Layout.DateActions(range))
        {
            var name = action.BoxIndex == 0 ? "起" : "迄";
            await SetDateAsync(invq, boxes[action.BoxIndex], action.Date, name,
                click: action.Click, enterAfter: action.EnterAfter, cancellationToken: cancellationToken);
        }
        if (range.From is { } expectedFrom &&
            !Invq01Layout.ShowsDate(NativeMethods.ControlText(boxes[0].Handle), expectedFrom))
            throw new InvalidOperationException("查歷史異動的起日讀回不一致，已停止。");
        if (range.To is { } expectedTo &&
            !Invq01Layout.ShowsDate(NativeMethods.ControlText(boxes[1].Handle), expectedTo))
            throw new InvalidOperationException("查歷史異動的迄日讀回不一致，已停止。");
    }

    private async Task SetDateAsync(nint invq, WindowControl targetBox, DateOnly date, string what,
        bool click, bool enterAfter, CancellationToken cancellationToken, string field = "歷史異動")
    {
        if (click)
        {
            // Click the 年 part (left end) so the digits start at the year.
            var width = targetBox.Rect.Right - targetBox.Rect.Left;
            if (width < 48)
                throw new InvalidOperationException($"{field}的{what}日欄位寬度不正確，已停止。");
            var yearPoint = new Point(targetBox.Rect.Left + Math.Min(12, width / 10),
                (targetBox.Rect.Top + targetBox.Rect.Bottom) / 2);
            await ClickUntilFocusedAsync(invq, targetBox, yearPoint, $"{field}的{what}日", cancellationToken);
        }
        // Without a click (Enter moved here from 起) ERP may still be checking the field it left.
        var focus = NativeMethods.FocusedControlOfForeground();
        if (focus != targetBox.Handle && !Win32Automation.IsInside(focus, targetBox.Handle) &&
            !await WaitUntilAsync(() => (focus = NativeMethods.FocusedControlOfForeground()) == targetBox.Handle ||
                Win32Automation.IsInside(focus, targetBox.Handle), 1500, cancellationToken))
            throw new InvalidOperationException($"{field}的{what}日沒有取得焦點，已停止，避免日期打到錯誤欄位。");

        if (NativeMethods.CloseImeFor(focus))
            await Pace.Delay(60, cancellationToken);
        // Once a date box has been entered it shows the 「    /  /  」 mask and a click can put
        // the caret on any digit; typing from the second one corrupts the date (user,
        // 2026-10-16). Home moves the caret to the first digit, whether reached by click or Enter.
        InputSender.Press(NativeMethods.VK_HOME);
        await Pace.Delay(60, cancellationToken);
        foreach (var digit in Invq01Layout.ErpDateKeys(date))
        {
            cancellationToken.ThrowIfCancellationRequested();
            InputSender.Press((ushort)digit);
            await Pace.Delay(70, cancellationToken);
        }

        if (enterAfter)
            InputSender.Press(NativeMethods.VK_RETURN);
        await Pace.Delay(200, cancellationToken);
        var shown = NativeMethods.ControlText(targetBox.Handle);
        if (!Invq01Layout.ShowsDate(shown, date))
        {
            _log.Warn("invq01", $"history {what} date readback mismatch length={shown?.Length ?? 0}");
            throw new InvalidOperationException($"{field}的{what}日輸入後讀回不一致，已停止。");
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
                await ThrowErpMessageAsync(h, $"「{tab}」開始查詢後");
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

    /// <summary>
    /// An INVQ01 left open (opened from INVI02 by hand, or by a run that was stopped) locks INVI02:
    /// the query then stops with 「有其他對話框開著」 (V0.5.0 LOG, 2026-10-16). INVQ01 is a query
    /// form only, so it is closed (user, 2026-10-16), and INVI02 is waited for until it is usable.
    /// </summary>
    private async Task CloseLeftoverInvq01Async(IProgress<string> progress, CancellationToken cancellationToken)
    {
        var open = new List<nint>();
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (NativeMethods.IsWindowVisible(hwnd) && Invq01Layout.IsInvq01Title(NativeMethods.WindowText(hwnd)) &&
                !Invi02Layout.IsMessageDialogClass(NativeMethods.ClassName(hwnd)))
                open.Add(hwnd);
            return true;
        }, 0);
        if (open.Count == 0) return;
        progress.Report("ERP：關閉開著的庫存狀況查詢作業…");
        _log.Info("invq01", $"closing {open.Count} INVQ01 left open");
        foreach (var hwnd in open)
            if (!await CloseInvq01Async(hwnd))
                throw new QueryFailure(ErpAgentErrorCode.ERP_STATE, "開著的庫存狀況查詢作業無法自動關閉，請先手動關閉後再查詢。");
        // INVI02 becomes usable again once INVQ01 is gone.
        await WaitUntilAsync(() => Win32Automation.FindInvi02Windows().All(NativeMethods.IsWindowEnabled), AfterActionWaitMs, cancellationToken);
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

    /// <summary>Ribbon group learned per window class and word during this run (see <see cref="RibbonClickAsync"/>).</summary>
    private readonly Dictionary<string, string> _ribbonGroups = new(StringComparer.Ordinal);

    /// <summary>
    /// Finds <paramref name="word"/> on the Ribbon of <paramref name="window"/> by OCR and clicks
    /// it; returns the label's screen rectangle. Each Ribbon group is a TdxRibbonGroupBarControl
    /// window captioned with the group name (V0.3.0 LOG). Reading the whole Ribbon takes about
    /// 2 s; reading one group a fraction of that. So the group is read first: the one given, or
    /// the one the word was found in earlier this run (learned after a whole-Ribbon read). The
    /// click is still where OCR read the word: Ribbon clicks are never blind coordinates.
    /// </summary>
    private async Task<Rectangle> RibbonClickAsync(nint window, string word, string what, CancellationToken cancellationToken,
        string? group = null, string? alternative = null, bool requireChinese = true)
    {
        if (!Win32Automation.PrepareForeground(window, _log))
            throw new InvalidOperationException($"點擊{what}前無法把 ERP 視窗帶到前景。");
        if (!NativeMethods.GetWindowRect(window, out var w))
            throw new InvalidOperationException($"讀不到{what}所在視窗的位置。");

        Rectangle? Find(IReadOnlyList<OcrToken> tokens) =>
            OcrWordFinder.FindWord(tokens, word) ?? (alternative is null ? null : OcrWordFinder.FindWord(tokens, alternative));

        var bars = Win32Automation.EnumerateChildren(window)
            .Where(c => c.Visible && c.ClassName.Contains("RibbonGroupBar", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var key = NativeMethods.ClassName(window) + "|" + word;
        var preferred = group ?? (_ribbonGroups.TryGetValue(key, out var learned) ? learned : null);
        if (preferred is not null)
        {
            if (bars.FirstOrDefault(c => Invi02Layout.NormalizeCaption(c.Text) == Invi02Layout.NormalizeCaption(preferred)) is { } bar)
            {
                var area = bar.Rect.ToRectangle();
                IReadOnlyList<OcrToken> groupTokens;
                using (var image = ScreenCapture.Capture(area))
                    groupTokens = await _ocr.RecognizeAsync(image, cancellationToken, requireChinese: requireChinese);
                if (Find(groupTokens) is { } g)
                {
                    var found = new Rectangle(area.Left + g.Left, area.Top + g.Top, g.Width, g.Height);
                    SafeClick(window, new Point(found.Left + found.Width / 2, found.Top + found.Height / 2));
                    _log.Info("ribbon", $"clicked {word} in group={preferred}");
                    return found;
                }
                _log.Warn("ribbon", $"group={preferred} has no {word} tokens=\"{string.Join(" ", groupTokens.Select(t => t.Text))}\"");
            }
            else _log.Warn("ribbon", $"ribbon group {preferred} not found; reading the whole Ribbon");
        }

        var ribbon = new Rectangle(w.Left, w.Top, Math.Min(w.Width, 1200), Math.Min(w.Height, 220));
        IReadOnlyList<OcrToken> tokens;
        using (var image = ScreenCapture.Capture(ribbon))
            tokens = await _ocr.RecognizeAsync(image, cancellationToken, requireChinese: requireChinese);
        if (Find(tokens) is not { } r)
        {
            _log.Warn("ribbon", $"OCR did not find {word} tokens=\"{string.Join(" ", tokens.Select(t => t.Text))}\"");
            throw new InvalidOperationException($"ERP 找不到{what}。");
        }
        var label = new Rectangle(ribbon.Left + r.Left, ribbon.Top + r.Top, r.Width, r.Height);
        var point = new Point(label.Left + label.Width / 2, label.Top + label.Height / 2);
        SafeClick(window, point);

        // Learn the group so the next query reads only that group.
        if (bars.FirstOrDefault(c => c.Rect.ToRectangle().Contains(point) && c.Text.Trim().Length > 0) is { } home)
        {
            _ribbonGroups[key] = home.Text.Trim();
            _log.Info("ribbon", $"clicked {word} by whole Ribbon; group={home.Text.Trim()} remembered");
        }
        else _log.Info("ribbon", $"clicked {word} by whole Ribbon; no group window");
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
