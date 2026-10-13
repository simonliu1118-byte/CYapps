using System.Diagnostics;
using System.Windows.Automation;

namespace CYERPAutoInput;

/// <summary>One piece of page text as Chrome reports it to UI Automation (screen pixels).</summary>
internal sealed record PageText(string Type, string Text, Rectangle Rect);

/// <summary>
/// Reads the text of a Chrome page through UI Automation, the interface screen readers use
/// (user, 2026-10-13: the OCR is slow and misreads). The text is the page's own, with each
/// piece's position, without a screenshot. Chrome builds its accessibility tree the first
/// time a client asks, so an empty first answer is read again.
/// </summary>
internal static class ChromePageText
{
    public static List<PageText> Read(nint window, AppLogger log, out long elapsedMs)
    {
        var watch = Stopwatch.StartNew();
        var result = new List<PageText>();
        for (var attempt = 1; attempt <= 3 && result.Count == 0; attempt++)
        {
            if (attempt > 1) Thread.Sleep(800);
            result = ReadOnce(window, log);
            log.Info("page-text", $"read attempt={attempt} items={result.Count} elapsed_ms={watch.ElapsedMilliseconds}");
        }
        elapsedMs = watch.ElapsedMilliseconds;
        return result;
    }

    private static List<PageText> ReadOnce(nint window, AppLogger log)
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
                var name = element.Cached.Name ?? string.Empty;
                var value = element.GetCachedPropertyValue(ValuePattern.ValueProperty, true) as string ?? string.Empty;
                var text = name.Trim().Length > 0 ? name.Trim() : value.Trim();
                if (text.Length == 0) continue;
                var r = element.Cached.BoundingRectangle;
                if (r.IsEmpty || r.Width <= 0 || r.Height <= 0) continue;
                var type = element.Cached.ControlType?.ProgrammaticName.Replace("ControlType.", string.Empty, StringComparison.Ordinal) ?? "?";
                result.Add(new PageText(type, text, new Rectangle((int)r.Left, (int)r.Top, (int)r.Width, (int)r.Height)));
            }
            catch (ElementNotAvailableException) { }
        }
        log.Info("page-text", $"elements={all.Count} with_text={result.Count}");
        return result;
    }
}
