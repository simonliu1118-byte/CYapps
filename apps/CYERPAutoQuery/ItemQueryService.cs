using System.Diagnostics;

namespace CYERPAutoQuery;

/// <summary>
/// Looks up one 品號 in SMART ERP 品號資料建立作業 (INVI02) and reads the header and the
/// 基本資料1／基本資料2／採購生管1／售價 tabs. Read-only towards ERP data: it opens the
/// query dialog (F6), sets 一般選項 品號 = code, confirms, and switches tabs; it never
/// presses 新增／修改／刪除／儲存／取消.
/// </summary>
internal sealed class ItemQueryService
{
    private const int MaximizeSettleMs = 300;
    private const int QueryDialogWaitMs = 3000;
    private const int QueryRunWaitMs = 15000;
    private const int RecordWaitMs = 5000;
    private const int TabWaitMs = 1200;

    private readonly AppLogger _log;
    private readonly PaddleOcrService _ocr;

    public ItemQueryService(AppLogger log)
    {
        _log = log;
        _ocr = new PaddleOcrService(log);
    }

    public async Task<ItemQueryResult> QueryAsync(string itemCode, IProgress<string> progress, CancellationToken cancellationToken)
    {
        if (!ItemCodeRules.TryNormalize(itemCode, out var code, out var error))
            throw new InvalidOperationException(error);

        using var gate = ErpAutomationGate.Enter();
        var watch = Stopwatch.StartNew();
        _log.Info("query", $"start item={_log.Value(code)}");

        progress.Report("ERP：尋找品號資料建立作業…");
        var root = FindSingleInvi02();

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

        var fields = new List<ErpFieldValue>(ReadFields(ItemQueryResult.HeaderGroup, header.Inputs));
        foreach (var tab in Invi02Layout.ReadTabs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress.Report($"ERP：讀取「{tab}」…");
            var sheet = Win32Automation.FindTabSheet(root, tab)
                ?? throw new InvalidOperationException($"找不到 ERP 頁籤「{tab}」。");
            await ActivateTabAsync(root, page, Invi02Layout.Invi02Tabs, tab, sheet.Handle, cancellationToken);
            var inputs = Win32Automation.InputControls(sheet.Handle, visibleOnly: true);
            fields.AddRange(ReadFields(tab, inputs));
            _log.Info("read", $"tab={tab} fields={inputs.Count}");
        }

        // The record must still be the one asked for after all the tab switching.
        var finalCode = NativeMethods.ControlText(header.ItemCode.Handle);
        if (!ItemCodeRules.SameCode(finalCode, code))
            throw new InvalidOperationException("讀取頁籤途中 ERP 顯示的品號改變了；結果不採用，請重新查詢。");

        var result = new ItemQueryResult(
            code,
            (NativeMethods.ControlText(header.ItemName.Handle) ?? string.Empty).Trim(),
            (NativeMethods.ControlText(header.Specification.Handle) ?? string.Empty).Trim(),
            fields,
            watch.Elapsed);
        _log.Info("query", $"done item={_log.Value(code)} fields={fields.Count} elapsed_ms={watch.ElapsedMilliseconds}");
        return result;
    }

    private nint FindSingleInvi02()
    {
        var windows = Win32Automation.FindInvi02Windows();
        if (windows.Count == 0)
            throw new InvalidOperationException("找不到 SMART ERP「品號資料建立作業 (INVI02)」視窗。\n請先在 ERP 開啟品號資料建立作業。");
        if (windows.Count > 1)
            throw new InvalidOperationException($"偵測到 {windows.Count} 個品號資料建立作業視窗。\n請只保留一個再查詢，避免讀到錯誤的視窗。");
        var root = windows[0];
        _log.Info("window", $"INVI02 found class={NativeMethods.ClassName(root)} visible={NativeMethods.IsWindowVisible(root)}");
        return root;
    }

    private static WindowControl FindMainPageControl(nint root)
    {
        var first = Win32Automation.FindTabSheet(root, Invi02Layout.ReadTabs[0])
            ?? throw new InvalidOperationException($"找不到 ERP 頁籤「{Invi02Layout.ReadTabs[0]}」。");
        var pageHandle = NativeMethods.GetParent(first.Handle);
        return Win32Automation.EnumerateChildren(root).FirstOrDefault(c => c.Handle == pageHandle)
            ?? throw new InvalidOperationException("找不到 ERP 品號資料頁籤的 TcxPageControl。");
    }

    // ---- header -----------------------------------------------------------------------

    private sealed record HeaderControls(
        WindowControl ItemCode, WindowControl ItemName, WindowControl Specification, IReadOnlyList<WindowControl> Inputs);

    /// <summary>
    /// The header is every input above the tab page control. Its first three rows start
    /// with 品號, 品名 and 規格 (checkboxes such as 定重 are left out of that row structure).
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
        return new HeaderControls(rows[0][0], rows[1][0], rows[2][0], inputs);
    }

    // ---- query dialog -----------------------------------------------------------------

    private async Task<nint> OpenQueryDialogAsync(nint root, WindowControl page, CancellationToken cancellationToken)
    {
        // A dialog left open by the user is reused instead of pressing F6 again.
        var open = Win32Automation.FindProcessWindows(root, Invi02Layout.IsQueryDialogTitle);
        if (open.Count == 1)
        {
            _log.Info("dialog", "query dialog already open; reused");
            return open[0];
        }
        if (open.Count > 1) throw new InvalidOperationException("ERP 開著多個「設定查詢條件」視窗，請先關閉再查詢。");
        if (!NativeMethods.IsWindowEnabled(root))
            throw new InvalidOperationException("ERP 品號資料建立作業目前有其他對話框開著，請先處理後再查詢。");

        var header = ResolveHeader(root, page);
        if (header.ItemCode.ClassName.Equals("TDBEdit", StringComparison.OrdinalIgnoreCase) &&
            !Win32Automation.IsReadOnly(header.ItemCode.Handle))
            throw new InvalidOperationException("ERP 品號資料建立作業目前在新增或修改狀態。\n請先儲存或取消後再查詢。");

        if (!Win32Automation.PrepareForeground(root, _log))
            throw new InvalidOperationException("按 F6 前無法把品號資料建立作業帶到前景。");
        InputSender.Press(NativeMethods.VK_F6);
        _log.Info("dialog", "F6 sent");

        var deadline = Environment.TickCount64 + QueryDialogWaitMs;
        while (Environment.TickCount64 < deadline)
        {
            await Pace.Delay(100, cancellationToken);
            var found = Win32Automation.FindProcessWindows(root, Invi02Layout.IsQueryDialogTitle);
            if (found.Count == 1)
            {
                await Pace.Delay(150, cancellationToken); // let the dialog finish painting
                return found[0];
            }
        }
        throw new InvalidOperationException("已按 F6，但 3 秒內沒有出現「設定查詢條件」視窗。\n請確認品號資料建立作業在檢視狀態。");
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

    private async Task<HeaderControls> WaitForRecordAsync(nint root, WindowControl page, string code, CancellationToken cancellationToken)
    {
        var deadline = Environment.TickCount64 + RecordWaitMs;
        var shown = string.Empty;
        while (true)
        {
            var header = ResolveHeader(root, page);
            shown = (NativeMethods.ControlText(header.ItemCode.Handle) ?? string.Empty).Trim();
            if (ItemCodeRules.SameCode(shown, code)) return header;
            if (Environment.TickCount64 > deadline) break;
            await Pace.Delay(120, cancellationToken);
        }
        _log.Warn("query", $"record mismatch shown={_log.Value(shown)}");
        throw new InvalidOperationException(shown.Length == 0
            ? $"查無品號「{code}」。"
            : $"查無品號「{code}」（ERP 目前顯示的是「{shown}」）。");
    }

    // ---- tabs -------------------------------------------------------------------------

    /// <summary>
    /// Shows <paramref name="tab"/>: nothing to do when it is already shown; otherwise click
    /// the measured position, and when that does not show it, find the caption by OCR and
    /// click there (user decision 2026-10-16). Success is always the sheet becoming visible.
    /// </summary>
    private async Task ActivateTabAsync(nint root, WindowControl page, TabStrip strip, string tab, nint sheet, CancellationToken cancellationToken)
    {
        if (NativeMethods.IsWindowVisible(sheet)) return;
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

    // ---- reading ----------------------------------------------------------------------

    private static IEnumerable<ErpFieldValue> ReadFields(string group, IReadOnlyList<WindowControl> inputs)
    {
        var rows = Invi02Layout.BuildRows(inputs, c => c.Rect.ToRectangle());
        for (var r = 0; r < rows.Count; r++)
        for (var c = 0; c < rows[r].Count; c++)
        {
            var control = rows[r][c];
            yield return new ErpFieldValue(group, r, c, control.ClassName, ReadValue(control));
        }
    }

    private static string ReadValue(WindowControl control)
    {
        if (Invi02Layout.IsCheckBox(control.ClassName))
        {
            return NativeMethods.QueryNumber(control.Handle, NativeMethods.BM_GETCHECK) switch
            {
                null => "（讀取逾時）",
                1 => "勾選",
                _ => "未勾選"
            };
        }
        return NativeMethods.ControlText(control.Handle)?.Trim() ?? "（讀取逾時）";
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
            throw new InvalidOperationException("另一支 CY 程式正在操作 SMART ERP，請等它完成後再查詢。");
        }
        return new ErpAutomationGate(mutex);
    }

    public void Dispose()
    {
        _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
