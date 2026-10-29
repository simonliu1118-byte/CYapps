using System.Drawing;

namespace CYERPAutoQuery;

internal sealed partial class ItemQueryService
{
    /// <summary>
    /// Read-only INVR11 F2 discovery. Reuses the proven 作業速查,
    /// OCR, mouse and ERP process-window primitives.
    /// Mutation is deliberately not exposed by this entry point.
    /// </summary>
    internal async Task<IReadOnlyList<StocktakeSheet>> ReadStocktakeF2Async(
        CancellationToken cancellationToken)
    {
        using var gate = ErpAutomationGate.Enter();
        const string code = "INVR11";
        const string title = "盤點";
        var opened = new Func<bool>(() => FindStocktakeWindows(code).Count == 1);
        await OpenFormAsync(code, title, opened, cancellationToken);
        var forms = FindStocktakeWindows(code);
        if (forms.Count != 1)
            throw new InvalidOperationException("INVR11 視窗數量不正確，禁止操作。");
        var form = forms[0];
        if (!NativeMethods.IsWindowEnabled(form) ||
            !Win32Automation.PrepareForeground(form, _log) ||
            !NativeMethods.GetWindowRect(form, out var formRect))
            throw new InvalidOperationException("INVR11 未在可操作的檢視狀態。");

        // F2 is opened only from the exact labelled field, never by guessing
        // a fixed screen coordinate or pressing F2 with unknown focus.
        IReadOnlyList<OcrToken> formTokens;
        using (var shot = ScreenCapture.Capture(formRect.ToRectangle()))
            formTokens = await _ocr.RecognizeAsync(shot, cancellationToken, requireChinese: true);
        var label = OcrWordFinder.FindWord(formTokens, "選擇盤點底稿編號");
        if (label is null)
            throw new InvalidOperationException("INVR11 找不到「選擇盤點底稿編號」，未開啟 F2。");
        var labelBounds = new Rectangle(
            formRect.Left + label.Left, formRect.Top + label.Top, label.Width, label.Height);
        var entries = Win32Automation.InputControls(form, visibleOnly: true)
            .Where(c => c.Enabled && c.Rect.Left >= labelBounds.Right &&
                        Math.Abs((c.Rect.Top + c.Rect.Bottom) / 2 -
                                 (labelBounds.Top + labelBounds.Bottom) / 2) <= 24)
            .OrderBy(c => c.Rect.Left).ToArray();
        if (entries.Length != 1)
            throw new InvalidOperationException("INVR11 底稿選擇欄位無法唯一識別，拒絕猜測。");
        SafeClick(form, Win32Automation.Center(entries[0].Rect));
        await Pace.Delay(200, cancellationToken);
        if (NativeMethods.FocusedControlOfForeground() != entries[0].Handle)
            throw new InvalidOperationException("INVR11 底稿欄位未取得焦點，拒絕 F2。");
        var before = Win32Automation.VisibleProcessWindows(form);
        InputSender.Press(0x71); // VK_F2
        var popup = await WaitNewWindowAsync(form, before,
            h => NativeMethods.GetWindowRect(h, out var bounds) &&
                 bounds.Width >= 220 && bounds.Height >= 130,
            10000, cancellationToken);
        if (popup is null || !NativeMethods.GetWindowRect(popup.Value, out var popupRect))
            throw new InvalidOperationException("INVR11 F2 清單視窗未出現。");
        IReadOnlyList<OcrToken> tokens;
        using (var image = ScreenCapture.Capture(popupRect.ToRectangle()))
            tokens = await _ocr.RecognizeAsync(image, cancellationToken, requireChinese: false);
        var text = string.Join(" ", tokens.OrderBy(t => t.Rect.Top)
            .ThenBy(t => t.Rect.Left).Select(t => t.Text));
        var sheets = StocktakeQuery.ParseF2(text);
        if (sheets.Count == 0)
            throw new InvalidDataException("F2 畫面未辨識到任何盤點底稿，不得當作空清單。");
        // A multi-page F2 must not be silently treated as a complete listing.
        if (System.Text.RegularExpressions.Regex.IsMatch(text, @"\b1\s*/\s*[2-9][0-9]*\b"))
            throw new InvalidDataException("F2 有多頁底稿，尚未核對全部頁數，禁止回傳不完整清單。");
        return sheets;
    }

    private static IReadOnlyList<nint> FindStocktakeWindows(string code)
    {
        var found = new List<nint>();
        NativeMethods.EnumWindows((h, _) =>
        {
            if (NativeMethods.IsWindowVisible(h) &&
                NativeMethods.WindowText(h).Contains(code, StringComparison.OrdinalIgnoreCase) &&
                !Invi02Layout.IsMessageDialogClass(NativeMethods.ClassName(h)))
                found.Add(h);
            return true;
        }, 0);
        return found;
    }
}
