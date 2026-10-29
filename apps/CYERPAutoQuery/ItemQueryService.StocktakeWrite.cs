using System.Drawing;

namespace CYERPAutoQuery;

/// <summary>
/// INVI14/INVI06 stocktake UI operations built on the existing F6 dialog,
/// Win32 input controls, OCR-labelled buttons, and INVR11 report readback.
/// No separate automation engine is introduced.
/// </summary>
internal sealed partial class ItemQueryService
{
    internal async Task<StocktakeWriteRowResult> WriteStocktakeExistingAsync(
        string sheetNo, StocktakeWriteRow row, CancellationToken ct)
    {
        if (row.Seq is null) throw new InvalidDataException("INVI14 需底稿序號。");
        using var gate = ErpAutomationGate.Enter();
        var root = await OpenStocktakeEditorAsync("INVI14", ct);
        await QueryStocktakeFormAsync(root, [sheetNo,row.Seq], ct);
        await VerifyStocktakeIdentityAsync(root, sheetNo, row, ct);
        await RibbonClickAsync(root, "修改", "INVI14 修改", ct);
        try
        {
            await SetStocktakeFieldAsync(root, "盤點數量", row.Quantity, ct);
            await RibbonClickAsync(root, "儲存", "INVI14 儲存", ct);
            await Pace.Delay(500, ct);
            await VerifyStocktakeFieldAsync(root, "盤點數量", row.Quantity, ct);
            _log.Info("stocktake", $"write seq={row.Seq} item={row.ItemCode} batch={row.Batch} ok");
            return new StocktakeWriteRowResult(row.Key, true, row.Seq, "已儲存");
        }
        catch
        {
            await CancelStocktakeEditAsync(root);
            throw;
        }
    }

    internal async Task<StocktakeWriteRowResult> AddStocktakeBatchAsync(
        string sheetNo, StocktakeWriteRow row, CancellationToken ct)
    {
        using var gate = ErpAutomationGate.Enter();
        var root = await OpenStocktakeEditorAsync("INVI06", ct);
        await QueryStocktakeFormAsync(root,[sheetNo],ct);
        await VerifyStocktakeFieldAsync(root,"盤點底稿編號",sheetNo,ct);
        await RibbonClickAsync(root,"修改","INVI06 修改",ct);
        try
        {
            // The last grid row is located through the labelled quantity column.
            // The existing Delphi keyboard convention inserts a new row on Down.
            var grid = Win32Automation.EnumerateChildren(root)
                .Where(x => x.Visible && x.ClassName.Contains("Grid",StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => (long)x.Rect.Width * x.Rect.Height).FirstOrDefault();
            if (grid is null) throw new InvalidOperationException("INVI06 找不到明細 Grid。");
            SafeClick(root,new Point(grid.Rect.Left + grid.Rect.Width / 2,grid.Rect.Bottom - 16));
            InputSender.Press(NativeMethods.VK_DOWN);
            await Pace.Delay(250,ct);
            await SetStocktakeFieldAsync(root,"品號",row.ItemCode,ct);
            await SetStocktakeFieldAsync(root,"批號",row.Batch,ct);
            await SetStocktakeFieldAsync(root,"盤點數量",row.Quantity,ct);
            await RibbonClickAsync(root,"儲存","INVI06 儲存",ct);
            await Pace.Delay(500,ct);
        }
        catch
        {
            await CancelStocktakeEditAsync(root);
            throw;
        }

        // New sequence is ERP-assigned: read INVR11 instead of predicting it.
        // This occurs after exiting the editing scope.
        var detail = await ReadStocktakeSheetAsync(sheetNo,ct);
        var matches = detail.Rows.Where(x => x.ItemCode==row.ItemCode && x.Batch==row.Batch).ToArray();
        if (matches.Length!=1 || !StocktakeQuery.AlreadyApplied(row,matches[0]))
            throw new InvalidDataException("INVI06 新增後 INVR11 無法核對唯一批號。");
        return new StocktakeWriteRowResult(row.Key,true,matches[0].Seq,"已新增並讀回");
    }

    internal async Task<bool> ConfirmStocktakeSheetAsync(string sheetNo,CancellationToken ct)
    {
        using var gate = ErpAutomationGate.Enter();
        var root = await OpenStocktakeEditorAsync("INVI06",ct);
        await QueryStocktakeFormAsync(root,[sheetNo],ct);
        await VerifyStocktakeFieldAsync(root,"盤點底稿編號",sheetNo,ct);
        await RibbonClickAsync(root,"確認","INVI06 底稿確認",ct);
        await Pace.Delay(500,ct);
        // ERP confirmation state must be read back, not inferred from the click.
        return await StocktakeVisibleTextAsync(root,"確認狀態",ct) is { } state &&
            (state.Contains("已確認",StringComparison.Ordinal) ||
             state.Contains("Y",StringComparison.OrdinalIgnoreCase));
    }

    private async Task<nint> OpenStocktakeEditorAsync(string code,CancellationToken ct)
    {
        await OpenFormAsync(code,code,()=>FindStocktakeWindows(code).Count==1,ct);
        var windows=FindStocktakeWindows(code);
        if(windows.Count!=1 || !NativeMethods.IsWindowEnabled(windows[0]))
            throw new InvalidOperationException(code+" 未在可操作狀態。");
        return windows[0];
    }

    private async Task QueryStocktakeFormAsync(nint root,string[] values,CancellationToken ct)
    {
        if(!Win32Automation.PrepareForeground(root,_log))
            throw new InvalidOperationException("ERP 查詢視窗無法帶到前景。");
        var before=Win32Automation.VisibleProcessWindows(root);
        InputSender.Press(NativeMethods.VK_F6);
        var dialog=await WaitNewWindowAsync(root,before,
            h=>Invi02Layout.IsQueryDialogTitle(NativeMethods.WindowText(h)) ||
               Win32Automation.FindTabSheet(h,Invi02Layout.GeneralQueryTab) is not null,
            QueryDialogWaitMs,ct)
            ??throw new InvalidOperationException("F6 設定查詢條件視窗未出現。");
        var sheet=Win32Automation.FindTabSheet(dialog,Invi02Layout.GeneralQueryTab)
            ??throw new InvalidOperationException("沒有一般選項頁籤。");
        var rows=Invi02Layout.BuildRows(
            Win32Automation.InputControls(sheet.Handle,visibleOnly:true)
                .Where(x=>!Invi02Layout.IsCheckBox(x.ClassName)),x=>x.Rect.ToRectangle());
        if(rows.Count<values.Length)
            throw new InvalidOperationException("查詢條件列數不足。");
        for(var i=0;i<values.Length;i++)
        {
            if(rows[i].Count!=2)throw new InvalidOperationException("ERP 查詢條件列結構錯誤。");
            await SetOperatorEqualsAsync(dialog,rows[i][0],rows[i][1],ct);
            await SetValueAsync(dialog,rows[i][1],values[i],ct);
        }
        await ConfirmQueryAsync(root,dialog,ct);
    }

    private async Task VerifyStocktakeIdentityAsync(
        nint root,string sheetNo,StocktakeWriteRow row,CancellationToken ct)
    {
        await VerifyStocktakeFieldAsync(root,"盤點底稿編號",sheetNo,ct);
        await VerifyStocktakeFieldAsync(root,"序號",row.Seq!,ct);
        await VerifyStocktakeFieldAsync(root,"品號",row.ItemCode,ct);
        await VerifyStocktakeFieldAsync(root,"批號",row.Batch,ct);
    }

    private async Task<WindowControl> StocktakeFieldAsync(nint root,string label,CancellationToken ct)
    {
        if(!NativeMethods.GetWindowRect(root,out var rect))
            throw new InvalidOperationException("ERP 視窗無法取得尺寸。");
        IReadOnlyList<OcrToken> tokens;
        using(var image=ScreenCapture.Capture(rect.ToRectangle()))
            tokens=await _ocr.RecognizeAsync(image,ct,requireChinese:true);
        if(OcrWordFinder.FindWord(tokens,label) is not { } found)
            throw new InvalidOperationException("ERP 找不到欄位："+label);
        var middle=rect.Top+found.Top+found.Height/2;
        var right=rect.Left+found.Right;
        var choices=Win32Automation.InputControls(root,visibleOnly:true)
            .Where(x=>x.Enabled && x.Rect.Left>=right &&
                Math.Abs((x.Rect.Top+x.Rect.Bottom)/2-middle)<18)
            .OrderBy(x=>x.Rect.Left).ToArray();
        if(choices.Length==0) throw new InvalidOperationException("ERP 找不到欄位輸入框："+label);
        return choices[0];
    }

    private async Task SetStocktakeFieldAsync(nint root,string label,string value,CancellationToken ct)
    {
        var field=await StocktakeFieldAsync(root,label,ct);
        if(!NativeMethods.SetControlText(field.Handle,value))
            throw new InvalidOperationException("ERP 欄位無法寫入："+label);
        await VerifyStocktakeFieldAsync(root,label,value,ct);
    }

    private async Task VerifyStocktakeFieldAsync(nint root,string label,string expected,CancellationToken ct)
    {
        var control=await StocktakeFieldAsync(root,label,ct);
        var actual=(NativeMethods.ControlText(control.Handle)??"").Trim();
        if(actual!=expected)
            throw new InvalidOperationException("ERP 欄位讀回不一致："+label);
    }

    private async Task<string?> StocktakeVisibleTextAsync(nint root,string label,CancellationToken ct)
    {
        var control=await StocktakeFieldAsync(root,label,ct);
        return (NativeMethods.ControlText(control.Handle)??"").Trim();
    }

    private async Task CancelStocktakeEditAsync(nint root)
    {
        try
        {
            await RibbonClickAsync(root,"取消","ERP 取消未儲存的修改",CancellationToken.None);
        }
        catch(Exception ex)
        {
            _log.Warn("stocktake",$"cancel failed: {ex.GetType().Name}");
        }
    }
}

/// <summary>One adapter for the existing Win32 service; no second automation engine.</summary>
internal sealed class StocktakeWin32Driver(ItemQueryService service) : IStocktakeErpDriver
{
    public Task<IReadOnlyList<StocktakeSheet>> ListSheetsAsync(CancellationToken ct) =>
        service.ReadStocktakeF2Async(ct);
    public Task<StocktakeDetail> ReadSheetAsync(string sheetNo,CancellationToken ct) =>
        service.ReadStocktakeSheetAsync(sheetNo,ct);
    public Task<StocktakeWriteRowResult> WriteExistingAsync(
        string sheetNo,StocktakeWriteRow row,CancellationToken ct) =>
        service.WriteStocktakeExistingAsync(sheetNo,row,ct);
    public Task<StocktakeWriteRowResult> AddBatchAsync(
        string sheetNo,StocktakeWriteRow row,CancellationToken ct) =>
        service.AddStocktakeBatchAsync(sheetNo,row,ct);
    public Task<bool> ConfirmAsync(string sheetNo,CancellationToken ct) =>
        service.ConfirmStocktakeSheetAsync(sheetNo,ct);
    public Task CancelUncommittedEditAsync(CancellationToken ct) => Task.CompletedTask;
}
