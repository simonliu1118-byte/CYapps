using System.Drawing;

namespace CYERPAutoQuery;

/// <summary>
/// Stocktake INVR11 report operations, using the existing SMART ERP window,
/// Ribbon, report export, and XLSX reader code. The editable INVI14/INVI06
/// operations require their field mappings to be verified in review.
/// </summary>
internal sealed partial class ItemQueryService
{
    private static readonly ErpReport StocktakeReport = new(
        "INVR11", "盤點底稿明細表",
        ["基本選項"], new TabStrip(["基本選項"], Origin: 17, Pitch: 80));

    /// <summary>Read one selected INVR11 sheet through its actual ERP report, not synthetic data.</summary>
    internal async Task<StocktakeDetail> ReadStocktakeSheetAsync(
        string sheetNo, CancellationToken ct)
    {
        if (!StocktakeQuery.IsSheetNo(sheetNo))
            throw new InvalidDataException("盤點底稿編號格式錯誤。");
        // Reuse the existing F2 list and its focused selection field.
        var list = await ReadStocktakeF2Async(ct, keepPopup: true);
        if (!list.Any(x => x.SheetNo == sheetNo))
            throw new InvalidDataException("INVR11 清單沒有指定底稿。");
        var roots = FindStocktakeWindows("INVR11");
        if (roots.Count != 1)
            throw new InvalidOperationException("INVR11 無法唯一識別視窗。");
        var root = roots[0];
        var chooser = Win32Automation.VisibleProcessWindows(root)
            .Where(h => NativeMethods.IsWindowVisible(h) &&
                        NativeMethods.GetWindowRect(h, out var r) &&
                        r.Width > 220 && r.Height > 130)
            .ToArray();
        if (chooser.Length != 1)
            throw new InvalidOperationException("F2 底稿選單不唯一。");
        var popup = chooser[0];
        if (!NativeMethods.GetWindowRect(popup, out var rect))
            throw new InvalidOperationException("讀不到 F2 選單位置。");
        IReadOnlyList<OcrToken> tokens;
        using (var image = ScreenCapture.Capture(rect.ToRectangle()))
            tokens = await _ocr.RecognizeAsync(image, ct, requireChinese: false);
        if (OcrWordFinder.FindWords(tokens, sheetNo) is not { Count: 1 } hits)
            throw new InvalidOperationException("F2 找不到唯一的盤點底稿列。");
        var hit = hits[0];
        SafeClick(popup, new Point(rect.Left + hit.Left + hit.Width / 2,
            rect.Top + hit.Top + hit.Height / 2));
        await Pace.Delay(150, ct);
        InputSender.Press(NativeMethods.VK_RETURN);
        await Pace.Delay(300, ct);

        if (!NativeMethods.GetWindowRect(root, out var area))
            throw new InvalidOperationException("讀不到 INVR11 視窗。");
        var inputs = Win32Automation.InputControls(root, visibleOnly: true);
        if (!inputs.Any(c => (NativeMethods.ControlText(c.Handle) ?? "").Trim() == sheetNo))
            throw new InvalidOperationException("F2 選擇的底稿編號未讀回。");

        var options = inputs.Where(c => Invi02Layout.IsCombo(c.ClassName))
            .Where(c => NativeMethods.ComboItems(c.Handle)?.Any(x=>x.Contains("兩者",StringComparison.Ordinal)) == true)
            .ToList();
        if (options.Count != 1)
            throw new InvalidOperationException("INVR11 不能唯一識別「選擇列印」下拉。");
        var combo = options[0];
        var items = NativeMethods.ComboItems(combo.Handle)!;
        var index = items.ToList().FindIndex(x => x.Contains("兩者", StringComparison.Ordinal));
        if (index < 0) throw new InvalidOperationException("INVR11 沒有「兩者皆列印」選項。");
        SafeClick(root, Win32Automation.Center(combo.Rect));
        InputSender.Press(NativeMethods.VK_HOME);
        for (var n = 0; n < index; n++) InputSender.Press(NativeMethods.VK_DOWN);
        InputSender.Press(NativeMethods.VK_RETURN);
        await Pace.Delay(150, ct);
        if (!(NativeMethods.ControlText(combo.Handle) ?? "").Contains("兩者",StringComparison.Ordinal))
            throw new InvalidOperationException("INVR11 列印選項讀回不一致。");

        var before = Win32Automation.VisibleProcessWindows(root);
        await ClickButtonAsync(root, "即時報表", "INVR11 即時報表", ct);
        var viewer = await WaitNewWindowAsync(root, before,
            h => NativeMethods.WindowText(h).Contains("盤點",StringComparison.Ordinal) &&
                 !Invi02Layout.IsMessageDialogClass(NativeMethods.ClassName(h)),
            ReportRunWaitMs, ct) ?? throw new InvalidOperationException("INVR11 沒有產生報表視窗。");
        var path = ExportFiles.NewPath("INVR11");
        try
        {
            await ExportReportAsync(viewer, StocktakeReport, path, ct);
            var sheets = await ExportFiles.ReadAsync(path, ct);
            return StocktakeQuery.ParseExport(sheetNo, sheets);
        }
        finally
        {
            ExportFiles.Delete(path, _log);
        }
    }
}
