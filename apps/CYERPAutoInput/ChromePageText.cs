using System.Diagnostics;
using System.Windows.Automation;

namespace CYERPAutoInput;

/// <summary>One piece of page text as Chrome reports it to UI Automation (screen pixels).</summary>
internal sealed record PageText(string Type, string Text, Rectangle Rect);

/// <summary>
/// Reads the text of a Chrome page through UI Automation, the interface screen readers use
/// (user, 2026-10-13: the OCR was slow and misread; the diagnostic read every Shopee screen
/// whole, in the page's own words, in 22–150 ms). Each piece comes with its position on the
/// screen — the whole page, also what is scrolled out of view. Chrome builds its tree for a
/// page the first time a client asks, so a read without the page's document is read again.
/// </summary>
internal static class ChromePageText
{
    public static List<PageText> Read(nint window, AppLogger log, out long elapsedMs, bool needDocument = true)
    {
        var watch = Stopwatch.StartNew();
        var result = new List<PageText>();
        for (var attempt = 1; attempt <= 4; attempt++)
        {
            if (attempt > 1) Thread.Sleep(500);
            result = ReadOnce(window);
            var hasDocument = result.Any(t => t.Type == "Document");
            log.Info("page-text", $"read attempt={attempt} items={result.Count} document={hasDocument} elapsed_ms={watch.ElapsedMilliseconds}");
            if (hasDocument || !needDocument) break;
        }
        elapsedMs = watch.ElapsedMilliseconds;
        return result;
    }

    private static List<PageText> ReadOnce(nint window)
    {
        var result = new List<PageText>();
        var root = AutomationElement.FromHandle(window);
        var cache = new CacheRequest { TreeScope = TreeScope.Element, AutomationElementMode = AutomationElementMode.None };
        cache.Add(AutomationElement.NameProperty);
        cache.Add(AutomationElement.BoundingRectangleProperty);
        cache.Add(AutomationElement.ControlTypeProperty);
        cache.Add(ValuePattern.ValueProperty);
        AutomationElementCollection all;
        using (cache.Activate())
            all = root.FindAll(TreeScope.Descendants, Condition.TrueCondition);
        foreach (AutomationElement element in all)
        {
            try
            {
                var r = element.Cached.BoundingRectangle;
                if (r.IsEmpty || r.Width <= 0 || r.Height <= 0) continue;
                var type = element.Cached.ControlType?.ProgrammaticName.Replace("ControlType.", string.Empty, StringComparison.Ordinal) ?? "?";
                var name = (element.Cached.Name ?? string.Empty).Trim();
                var value = ((element.GetCachedPropertyValue(ValuePattern.ValueProperty, true) as string) ?? string.Empty).Trim();
                // A drop-down's name is its label; what it shows is its value (Chrome's print
                // dialog: 目的地 → the printer, 頁面 → 全部).
                var text = type is "ComboBox" or "Spinner" or "Edit" && value.Length > 0 ? value : name.Length > 0 ? name : value;
                if (text.Length == 0 && type == "Button") text = ShopeeBatchScreens.NamelessButton;
                if (text.Length == 0) continue;
                result.Add(new PageText(type, text, new Rectangle((int)r.Left, (int)r.Top, (int)r.Width, (int)r.Height)));
            }
            catch (ElementNotAvailableException) { }
        }
        return result;
    }

    /// <summary>
    /// The page's own text as boxes relative to <paramref name="page"/> (Chrome's page area):
    /// without the tabs, toolbars and bookmarks above it, and without the summaries that repeat
    /// their children's text (a list item, a whole row's link) when <paramref name="pieces"/>.
    /// Chrome's print dialog, which overlaps the toolbar, is kept.
    /// </summary>
    public static List<OcrBox> PageBoxes(IEnumerable<PageText> texts, Rectangle page, bool pieces = false)
    {
        var boxes = new List<OcrBox>();
        foreach (var t in texts)
        {
            if (t.Type is "TabItem" or "ToolBar" or "TitleBar" or "MenuBar" or "Separator" or "Document" or "Pane" or "Window") continue;
            if (t.Rect.Bottom <= page.Top + 2 && t.Type != "Text") continue;
            if (pieces && t.Type is "ListItem") continue;
            boxes.Add(new OcrBox(t.Text, t.Rect.Left - page.Left, t.Rect.Top - page.Top, t.Rect.Width, t.Rect.Height));
        }
        return boxes;
    }
}
