using System.Drawing;

namespace CYERPAutoQuery;

/// <summary>
/// 庫存盤點 writing (WO-1.3.0, user 2026-10-29, screenshots 109–115). The only ERP writes this
/// program makes (PROJECT_RULES 盤點寫入例外):
/// <list type="bullet">
/// <item>INVI14 實盤資料補入作業: F6 一般選項 盤點底稿編號＋序號 → 詳細欄位 → 修改 → 盤點數量 → 儲存.</item>
/// <item>INVI06 盤點資料建立作業: F6 盤點底稿編號 → 修改 → the grid's last row, Down makes a new row →
/// 品號, 批號, 盤點數量 → 儲存; and 確認 once INVR11 shows every count.</item>
/// </list>
/// Every field is read back; a failure after 修改 presses 取消 so nothing half-typed is saved.
/// </summary>
internal sealed partial class ItemQueryService
{
    // 詳細欄位／資料瀏覽 tab centres, measured from the page control's left edge (screenshots 111, 115).
    private static readonly TabStrip StocktakeTabs = new([DetailTab, ErpForm.BrowseTab], 0, 0, Centers: [47, 112]);
    private const string DetailTab = "詳細欄位";
    private const int StocktakeSaveWaitMs = 15000;
    private const int GridRowHeight = 22;

    private static readonly ErpForm Invi14 = new("INVI14", "實盤資料補入作業", "TfrmInvi14", DetailTab, StocktakeTabs,
        HeaderRows: 0, CodeName: "盤點底稿編號", HasDetailSheet: false);
    private static readonly ErpForm Invi06 = new("INVI06", "盤點資料建立作業", "TfrmInvi06", DetailTab, StocktakeTabs,
        HeaderRows: 0, CodeName: "盤點底稿編號", HasDetailSheet: false);

    /// <summary>INVI14: one line's 盤點數量.</summary>
    public Task WriteStocktakeLineAsync(string sheetNo, StocktakeLine line, string quantity, CancellationToken cancellationToken) =>
        GuardErpMessagesAsync(async () =>
        {
            using var gate = ErpAutomationGate.Enter();
            _form = Invi14;
            var root = await QueryStocktakeFormAsync(Invi14, [sheetNo, line.Seq], cancellationToken);

            Invi14Fields fields = default;
            if (!await WaitUntilAsync(() => (fields = ReadInvi14Fields(root)) is { Found: true } f &&
                                            f.SheetNo == sheetNo && f.Seq == line.Seq, AfterActionWaitMs, cancellationToken))
                throw new InvalidOperationException($"INVI14 查不到盤點底稿 {sheetNo} 序號 {line.Seq}（顯示 {fields.SheetNo} {fields.Seq}）。");
            if (fields.ItemCode != line.ItemCode || fields.Batch != line.Batch)
                throw new InvalidOperationException($"INVI14 序號 {line.Seq} 是 {fields.ItemCode} 批號 {fields.Batch}，不是 {line.ItemCode} 批號 {line.Batch}，未寫入。");
            if (StocktakeQuery.SameQuantity(Text(fields.Quantity), quantity)) return true;

            await RibbonClickAsync(root, "修改", "INVI14 工具列的「修改」", cancellationToken);
            try
            {
                if (!await WaitUntilAsync(() => !Win32Automation.IsReadOnly(fields.Quantity.Handle), AfterActionWaitMs, cancellationToken))
                    throw new InvalidOperationException("INVI14 按「修改」後盤點數量仍不能輸入。");
                await TypeIntoAsync(root, fields.Quantity, quantity, cancellationToken);
                if (!StocktakeQuery.SameQuantity(Text(fields.Quantity), quantity))
                    throw new InvalidOperationException($"INVI14 盤點數量輸入後讀回「{Text(fields.Quantity)}」，不是 {quantity}。");
                await SaveStocktakeAsync(root, () => Win32Automation.IsReadOnly(fields.Quantity.Handle), "INVI14", cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await CancelStocktakeEditAsync(root, () => Win32Automation.IsReadOnly(fields.Quantity.Handle));
                throw;
            }
            if (!StocktakeQuery.SameQuantity(Text(fields.Quantity), quantity))
                throw new InvalidOperationException($"INVI14 儲存後盤點數量是「{Text(fields.Quantity)}」，不是 {quantity}。");
            _log.Info("stocktake", $"INVI14 sheet={sheetNo} seq={line.Seq} saved");
            return true;
        });

    /// <summary>INVI06: a new line at the end of the sheet with 品號, 批號 and 盤點數量.</summary>
    public Task AddStocktakeLineAsync(string sheetNo, StocktakeWriteRow row, CancellationToken cancellationToken) =>
        GuardErpMessagesAsync(async () =>
        {
            using var gate = ErpAutomationGate.Enter();
            _form = Invi06;
            var root = await QueryStocktakeFormAsync(Invi06, [sheetNo], cancellationToken);
            var date = await WaitInvi06HeaderAsync(root, sheetNo, cancellationToken);

            await RibbonClickAsync(root, "修改", "INVI06 工具列的「修改」", cancellationToken);
            try
            {
                if (!await WaitUntilAsync(() => !Win32Automation.IsReadOnly(date.Handle), AfterActionWaitMs, cancellationToken))
                    throw new InvalidOperationException("INVI06 按「修改」後沒有進入修改狀態。");
                var grid = StocktakeGrid(root);
                var columns = await GridColumnsAsync(grid, cancellationToken);

                // First row's 序號 (read-only) to focus the grid, Ctrl+End to its last row, Down for a new one.
                SafeClick(root, new Point(columns.Area.Left + columns.X["序號"], columns.Area.Top + columns.HeaderY + GridRowHeight));
                await Pace.Delay(200, cancellationToken);
                InputSender.PressWithControl(NativeMethods.VK_END);
                await Pace.Delay(400, cancellationToken);
                InputSender.Press(NativeMethods.VK_DOWN);
                await Pace.Delay(600, cancellationToken);

                var (newSeq, rowY) = await FindNewGridRowAsync(columns, cancellationToken);
                _log.Info("stocktake", $"INVI06 new row seq={newSeq}");
                await TypeGridCellAsync(root, columns, "品號", rowY, row.ItemCode, cancellationToken);
                await TypeGridCellAsync(root, columns, "批號", rowY, row.Batch, cancellationToken);
                await TypeGridCellAsync(root, columns, "盤點數量", rowY, row.Quantity, cancellationToken);

                await VerifyNewGridRowAsync(columns, rowY, row, cancellationToken);
                await SaveStocktakeAsync(root, () => Win32Automation.IsReadOnly(date.Handle), "INVI06", cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await CancelStocktakeEditAsync(root, () => Win32Automation.IsReadOnly(date.Handle));
                throw;
            }
            _log.Info("stocktake", $"INVI06 sheet={sheetNo} item={row.ItemCode} batch added");
            return true;
        });

    /// <summary>
    /// INVI06 確認. Before it 「確認」 is dark and 「取消確認」 grey (screenshot 110); confirmed is the other
    /// way round, which is how it is read back. A question box from ERP is answered 是.
    /// </summary>
    public Task<bool> ConfirmStocktakeSheetAsync(string sheetNo, CancellationToken cancellationToken) =>
        GuardErpMessagesAsync(async () =>
        {
            using var gate = ErpAutomationGate.Enter();
            _form = Invi06;
            var root = await QueryStocktakeFormAsync(Invi06, [sheetNo], cancellationToken);
            await WaitInvi06HeaderAsync(root, sheetNo, cancellationToken);

            var (confirm, undo) = await ConfirmButtonsAsync(root, cancellationToken);
            if (IsDarkText(undo))
            {
                _log.Info("stocktake", $"INVI06 sheet={sheetNo} already confirmed");
                return true;
            }
            SafeClick(root, new Point(confirm.Left + confirm.Width / 2, confirm.Top + confirm.Height / 2));
            _log.Info("stocktake", $"INVI06 sheet={sheetNo} 確認 clicked");
            var deadline = Environment.TickCount64 + StocktakeSaveWaitMs;
            while (Environment.TickCount64 < deadline)
            {
                await Pace.Delay(300, cancellationToken);
                if (FindErpMessageBox(root) is var box && box != 0)
                {
                    var yes = FindButton(box, c => c.StartsWith("是", StringComparison.Ordinal) || c is "YES" or "&YES");
                    if (yes is null) await ThrowErpMessageAsync(box, "INVI06 確認時");
                    _log.Info("stocktake", $"INVI06 確認 question={_log.Value(await ReadErpMessageAsync(box))} answered 是");
                    SafeClick(box, Win32Automation.Center(yes!.Rect));
                    continue;
                }
                if (NativeMethods.IsWindowEnabled(root) && IsDarkText(undo))
                {
                    _log.Info("stocktake", $"INVI06 sheet={sheetNo} confirmed");
                    return true;
                }
            }
            _log.Warn("stocktake", $"INVI06 sheet={sheetNo} 取消確認 still grey after 確認");
            return false;
        });

    // ---- form and query --------------------------------------------------------------------

    /// <summary>The form (opened through 作業速查 when needed), maximized, F6 一般選項 rows = <paramref name="values"/> → 確定, then 詳細欄位.</summary>
    private async Task<nint> QueryStocktakeFormAsync(ErpForm form, string[] values, CancellationToken cancellationToken)
    {
        var windows = Win32Automation.FindFormWindows(form);
        if (windows.Count == 0)
        {
            await OpenFormAsync(form.Code, form.Title, () => Win32Automation.FindFormWindows(form).Count > 0, cancellationToken);
            windows = Win32Automation.FindFormWindows(form);
        }
        if (windows.Count != 1)
            throw new QueryFailure(ErpAgentErrorCode.ERP_STATE, $"開著 {windows.Count} 個{form.Title}，請只留一個。");
        var root = windows[0];
        NativeMethods.ShowWindow(root, NativeMethods.SW_MAXIMIZE);
        await Pace.Delay(MaximizeSettleMs, cancellationToken);
        if (!NativeMethods.IsWindowEnabled(root) || !Win32Automation.PrepareForeground(root, _log))
            throw new QueryFailure(ErpAgentErrorCode.ERP_STATE, $"{form.Title}有其他視窗開著，請先關閉。");

        var dialog = FindQueryDialogs(root) is { Count: 1 } open ? open[0] : 0;
        if (dialog == 0)
        {
            InputSender.Press(NativeMethods.VK_F6);
            await WaitUntilAsync(() => FindQueryDialogs(root).Count == 1, QueryDialogWaitMs, cancellationToken);
            dialog = FindQueryDialogs(root) is { Count: 1 } shown ? shown[0]
                : throw new InvalidOperationException($"{form.Title}按 F6 後沒有出現「設定查詢條件」（請確認不在修改狀態）。");
            await Pace.Delay(150, cancellationToken);
        }
        await ClearAdvancedConditionAsync(dialog, cancellationToken);
        var sheet = await WaitForTabSheetAsync(dialog, Invi02Layout.GeneralQueryTab, AfterActionWaitMs, cancellationToken)
            ?? throw new InvalidOperationException("「設定查詢條件」找不到「一般選項」頁籤。");
        var page = Win32Automation.EnumerateChildren(dialog).First(c => c.Handle == NativeMethods.GetParent(sheet.Handle));
        await ActivateTabAsync(dialog, page, Invi02Layout.QueryDialogTabs, Invi02Layout.GeneralQueryTab, sheet.Handle, cancellationToken);
        // 一般選項 (screenshots 109, 113): one row per condition, 「=」 drop-down + value box.
        var rows = Invi02Layout.BuildRows(
            Win32Automation.InputControls(sheet.Handle, visibleOnly: true).Where(c => !Invi02Layout.IsCheckBox(c.ClassName)),
            c => c.Rect.ToRectangle());
        if (rows.Count < values.Length || rows.Take(values.Length).Any(r => r.Count != 2))
        {
            LogChildren(dialog, form.Code);
            throw new InvalidOperationException($"{form.Title}的一般選項不是預期的 {values.Length} 列條件。");
        }
        for (var i = 0; i < values.Length; i++)
        {
            await SetOperatorEqualsAsync(dialog, rows[i][0], rows[i][1], cancellationToken);
            await SetValueAsync(dialog, rows[i][1], values[i], cancellationToken);
        }
        await ConfirmQueryAsync(root, dialog, cancellationToken);

        // ERP ends a query on 資料瀏覽; the fields are on 詳細欄位.
        await WaitForTabSheetAsync(root, ErpForm.BrowseTab, QuerySettleWaitMs, cancellationToken, visible: true);
        var detail = await WaitForTabSheetAsync(root, DetailTab, AfterActionWaitMs, cancellationToken)
            ?? throw new InvalidOperationException($"{form.Title}找不到「詳細欄位」頁籤。");
        var tabs = Win32Automation.EnumerateChildren(root).First(c => c.Handle == NativeMethods.GetParent(detail.Handle));
        await Pace.Delay(300, cancellationToken);
        await ActivateTabAsync(root, tabs, StocktakeTabs, DetailTab, detail.Handle, cancellationToken, force: true);
        return root;
    }

    private readonly record struct Invi14Fields(bool Found, string SheetNo, string Seq, string ItemCode, string Batch, WindowControl Quantity);

    /// <summary>
    /// INVI14 詳細欄位 (screenshot 115), rows of inputs top to bottom: 盤點底稿編號＋序號, 盤點日期＋品號,
    /// 盤點數量＋品名, 帳面數量＋規格, 儲位, 批號＋庫別＋庫別名稱, 備註.
    /// </summary>
    private static Invi14Fields ReadInvi14Fields(nint root)
    {
        if (Win32Automation.FindTabSheet(root, DetailTab) is not { } sheet) return default;
        var rows = Invi02Layout.BuildRows(
            Win32Automation.InputControls(sheet.Handle, visibleOnly: true).Where(c => !Invi02Layout.IsCheckBox(c.ClassName)),
            c => c.Rect.ToRectangle());
        if (rows.Count < 6 || rows[0].Count != 2 || rows[1].Count != 2 || rows[2].Count != 2 || rows[5].Count < 2) return default;
        return new Invi14Fields(true, Text(rows[0][0]), Text(rows[0][1]), Text(rows[1][1]), Text(rows[5][0]), rows[2][0]);
    }

    /// <summary>INVI06 詳細欄位 header (screenshot 111): 盤點底稿編號, then 盤點日期＋備註. Returns 盤點日期 (read-only unless 修改).</summary>
    private async Task<WindowControl> WaitInvi06HeaderAsync(nint root, string sheetNo, CancellationToken cancellationToken)
    {
        WindowControl? date = null;
        var shown = "";
        await WaitUntilAsync(() =>
        {
            if (Win32Automation.FindTabSheet(root, DetailTab) is not { } sheet) return false;
            var rows = Invi02Layout.BuildRows(
                Win32Automation.InputControls(sheet.Handle, visibleOnly: true).Where(c => !Invi02Layout.IsCheckBox(c.ClassName)),
                c => c.Rect.ToRectangle());
            if (rows.Count < 2) return false;
            shown = Text(rows[0][0]);
            date = rows[1][0];
            return shown == sheetNo;
        }, AfterActionWaitMs, cancellationToken);
        if (shown != sheetNo || date is null)
            throw new InvalidOperationException($"INVI06 查不到盤點底稿 {sheetNo}（顯示「{shown}」）。");
        return date;
    }

    private static string Text(WindowControl box) => (NativeMethods.ControlText(box.Handle) ?? "").Trim();

    // ---- editing -----------------------------------------------------------------------------

    /// <summary>Click, clear without Ctrl+A (End + Backspace), type, Tab out so ERP takes the value.</summary>
    private async Task TypeIntoAsync(nint root, WindowControl box, string value, CancellationToken cancellationToken)
    {
        SafeClick(root, Win32Automation.Center(box.Rect));
        await Pace.Delay(150, cancellationToken);
        if (!Win32Automation.FocusedIsInside(box.Handle))
            throw new InvalidOperationException("輸入欄沒有取得游標，已停止。");
        InputSender.EndBackspace(Text(box).Length + 2);
        InputSender.UnicodeText(value);
        InputSender.Press(NativeMethods.VK_TAB);
        await Pace.Delay(300, cancellationToken);
        await ThrowIfErpMessageAsync(root);
    }

    /// <summary>儲存, then wait for the form to leave 修改 (<paramref name="saved"/>); an ERP message stops it.</summary>
    private async Task SaveStocktakeAsync(nint root, Func<bool> saved, string code, CancellationToken cancellationToken)
    {
        await RibbonClickAsync(root, "儲存", $"{code} 工具列的「儲存」", cancellationToken);
        var deadline = Environment.TickCount64 + StocktakeSaveWaitMs;
        while (!(NativeMethods.IsWindowEnabled(root) && saved()))
        {
            await ThrowIfErpMessageAsync(root);
            if (Environment.TickCount64 > deadline)
                throw new InvalidOperationException($"{code} 按「儲存」後 {StocktakeSaveWaitMs / 1000} 秒仍在修改狀態。");
            await Pace.Delay(200, cancellationToken);
        }
    }

    /// <summary>取消 after a failure in 修改, so nothing half-typed is saved; the failure itself is what is reported.</summary>
    private async Task CancelStocktakeEditAsync(nint root, Func<bool> browsing)
    {
        try
        {
            if (FindErpMessageBox(root) != 0 || browsing()) return; // a box is left for the user
            await RibbonClickAsync(root, "取消", "工具列的「取消」", CancellationToken.None);
            await WaitUntilAsync(browsing, AfterActionWaitMs, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _log.Warn("stocktake", $"取消 after a failure did not work: {ex.Message}");
        }
    }

    // ---- INVI06 detail grid --------------------------------------------------------------------

    /// <summary>The detail grid (DevExpress, one window): the largest visible grid control of the form.</summary>
    private WindowControl StocktakeGrid(nint root) =>
        Win32Automation.EnumerateChildren(root)
            .Where(c => c.Visible && c.ClassName.Contains("Grid", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(c => (long)c.Rect.Width * c.Rect.Height).FirstOrDefault()
        ?? throw new InvalidOperationException("INVI06 找不到盤點明細表格。");

    private sealed record GridLayout(Rectangle Area, int HeaderY, IReadOnlyDictionary<string, int> X);

    /// <summary>Column centres from the grid's header line (序號 … 品號 … 批號盤點 批號 盤點數量, screenshot 111).</summary>
    private async Task<GridLayout> GridColumnsAsync(WindowControl grid, CancellationToken cancellationToken)
    {
        var area = grid.Rect.ToRectangle();
        IReadOnlyList<OcrToken> tokens;
        using (var image = ScreenCapture.Capture(new Rectangle(area.Left, area.Top, area.Width, Math.Min(area.Height, 40))))
            tokens = await _ocr.RecognizeAsync(image, cancellationToken, requireChinese: true);
        var x = StocktakeQuery.GridColumns(tokens, "序號", "品號", "批號", "盤點數量");
        var header = OcrWordFinder.FindWord(tokens, "序號");
        if (x.Count != 4 || header is null)
        {
            _log.Warn("stocktake", $"INVI06 grid header tokens=\"{string.Join(" ", tokens.Select(t => t.Text))}\"");
            throw new InvalidOperationException("INVI06 明細表格找不到序號／品號／批號／盤點數量欄。");
        }
        return new GridLayout(area, header.Value.Top + header.Value.Height / 2, x);
    }

    /// <summary>
    /// The row Down made: the bottom 序號 on screen, after a smaller one (ERP numbers the new row, e.g.
    /// 00048 after 00047, screenshot 112). Returns its 序號 and centre y (grid coordinates).
    /// </summary>
    private async Task<(string Seq, int Y)> FindNewGridRowAsync(GridLayout grid, CancellationToken cancellationToken)
    {
        var seqs = StocktakeQuery.GridSeqs(await ReadGridAsync(grid, cancellationToken), grid.X["序號"]);
        if (seqs.Count < 2 || string.CompareOrdinal(seqs[^1].Seq, seqs[^2].Seq) <= 0)
            throw new InvalidOperationException($"INVI06 按「下」後沒有產生新的一列（讀到序號 {string.Join(",", seqs.TakeLast(3).Select(s => s.Seq))}）。");
        return seqs[^1];
    }

    /// <summary>
    /// Click the cell, Backspace what is in it, type, Tab. No End or Ctrl+A: outside the cell's editor they
    /// would move to another column. 盤點數量 shows 0 right-aligned, so it is clicked near its right edge
    /// (the caret lands after the 0); 品號 and 批號 are empty and clicked in the middle, left of their ⋯ button.
    /// </summary>
    private async Task TypeGridCellAsync(nint root, GridLayout grid, string column, int rowY, string value, CancellationToken cancellationToken)
    {
        var x = grid.X[column] + (column == "盤點數量" ? 28 : 0);
        SafeClick(root, new Point(grid.Area.Left + x, grid.Area.Top + rowY));
        await Pace.Delay(250, cancellationToken);
        for (var i = 0; i < 16; i++) InputSender.Press(NativeMethods.VK_BACK);
        InputSender.UnicodeText(value);
        InputSender.Press(NativeMethods.VK_TAB);
        await Pace.Delay(500, cancellationToken); // 品號 makes ERP fill 品名／規格／庫別
        await ThrowIfErpMessageAsync(root);
    }

    /// <summary>The new row as OCR reads it: 品號 and 批號 somewhere in its text, 盤點數量 under its column.</summary>
    private async Task VerifyNewGridRowAsync(GridLayout grid, int rowY, StocktakeWriteRow row, CancellationToken cancellationToken)
    {
        var tokens = await ReadGridAsync(grid, cancellationToken);
        var text = StocktakeQuery.RowText(tokens, rowY);
        var quantity = StocktakeQuery.CellText(tokens, rowY, grid.X["盤點數量"], 40);
        var ok = text.Contains(OcrTextNormalizer.Normalize(row.ItemCode), StringComparison.Ordinal) &&
                 text.Contains(OcrTextNormalizer.Normalize(row.Batch), StringComparison.Ordinal);
        try { ok = ok && StocktakeQuery.SameQuantity(quantity, row.Quantity); }
        catch (InvalidDataException) { ok = false; }
        if (!ok)
            throw new InvalidOperationException($"INVI06 新增列讀回「{text}」盤點數量「{quantity}」，不是 {row.ItemCode} {row.Batch} {row.Quantity}。");
    }

    private async Task<IReadOnlyList<OcrToken>> ReadGridAsync(GridLayout grid, CancellationToken cancellationToken)
    {
        // Up to the 盤點數量 column only: what is right of it is not needed.
        var width = Math.Min(grid.Area.Width, grid.X["盤點數量"] + 60);
        using var image = ScreenCapture.Capture(new Rectangle(grid.Area.Left, grid.Area.Top, width, grid.Area.Height));
        return await _ocr.RecognizeAsync(image, cancellationToken, requireChinese: false);
    }

    // ---- INVI06 確認 ---------------------------------------------------------------------------

    /// <summary>The Ribbon's 「確認」 and 「取消確認」 labels (screen coordinates).</summary>
    private async Task<(Rectangle Confirm, Rectangle Undo)> ConfirmButtonsAsync(nint root, CancellationToken cancellationToken)
    {
        if (!NativeMethods.GetWindowRect(root, out var w))
            throw new InvalidOperationException("讀不到 INVI06 視窗位置。");
        var bars = Win32Automation.EnumerateChildren(root)
            .Where(c => c.Visible && c.ClassName.Contains("RibbonGroupBar", StringComparison.OrdinalIgnoreCase)).ToList();
        var bottom = bars.Count > 0 ? bars.Max(c => c.Rect.Bottom) + 2 - w.Top : 150;
        var ribbon = new Rectangle(w.Left, w.Top, Math.Min(w.Width, 1200), Math.Clamp(bottom, 40, 220));
        IReadOnlyList<OcrToken> tokens;
        using (var image = ScreenCapture.Capture(ribbon))
            tokens = await _ocr.RecognizeAsync(image, cancellationToken, requireChinese: true);
        var undo = OcrWordFinder.FindWord(tokens, "取消確認");
        var confirm = OcrWordFinder.FindWords(tokens, "確認").Cast<Rectangle?>()
            .FirstOrDefault(r => undo is null || !undo.Value.IntersectsWith(r!.Value));
        if (undo is null || confirm is null)
        {
            _log.Warn("stocktake", $"INVI06 ribbon tokens=\"{string.Join(" ", tokens.Select(t => t.Text))}\"");
            throw new InvalidOperationException("INVI06 工具列找不到「確認」或「取消確認」。");
        }
        Rectangle Screen(Rectangle r) => new(ribbon.Left + r.Left, ribbon.Top + r.Top, r.Width, r.Height);
        return (Screen(confirm.Value), Screen(undo.Value));
    }

    /// <summary>Enabled Ribbon text is near black; disabled is grey (screenshot 110).</summary>
    private static bool IsDarkText(Rectangle label)
    {
        using var image = ScreenCapture.Capture(label);
        var dark = 0;
        for (var y = 0; y < image.Height; y++)
            for (var x = 0; x < image.Width; x++)
            {
                var c = image.GetPixel(x, y);
                if (c.R * 0.299 + c.G * 0.587 + c.B * 0.114 < 100) dark++;
            }
        return dark >= 12;
    }
}

/// <summary><see cref="IStocktakeErpDriver"/> on the real ERP screens.</summary>
internal sealed class StocktakeWin32Driver(ItemQueryService service) : IStocktakeErpDriver
{
    private static readonly IProgress<string> Quiet = new Progress<string>();

    public Task<IReadOnlyList<StocktakeSheet>> ListSheetsAsync(CancellationToken ct) => service.ListStocktakeSheetsAsync(Quiet, ct);
    public Task CloseReportAsync() => service.CloseStocktakeReportAsync();
    public Task<StocktakeDetail> ReadSheetAsync(string sheetNo, CancellationToken ct) => service.ReadStocktakeSheetAsync(sheetNo, Quiet, ct);
    public Task WriteExistingAsync(string sheetNo, StocktakeLine line, string quantity, CancellationToken ct) =>
        service.WriteStocktakeLineAsync(sheetNo, line, quantity, ct);
    public Task AddBatchAsync(string sheetNo, StocktakeWriteRow row, CancellationToken ct) => service.AddStocktakeLineAsync(sheetNo, row, ct);
    public Task<bool> ConfirmAsync(string sheetNo, CancellationToken ct) => service.ConfirmStocktakeSheetAsync(sheetNo, ct);
}
