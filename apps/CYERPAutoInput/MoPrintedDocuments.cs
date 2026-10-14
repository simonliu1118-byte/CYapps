using System.Text.Json;

namespace CYERPAutoInput;

/// <summary>One printed MO店+ document kept for 重新列印.</summary>
internal sealed record MoPrintedDocument(string Kind, string File, DateTime Printed, string Detail)
{
    public const string Family = "全家標籤";
    public const string Seven = "7-11標籤";
    public const string HctLabel = "新竹寄件單";
    public const string Summary = "新竹到廠取貨總表";
}

/// <summary>
/// MO店+ 寄件單 kept for 重新列印 (user, 2026-10-14: the program cannot tell a printer that
/// failed, so what was printed stays): each run's Temp\mo-… folder keeps its label and 總表
/// PDFs, listed in documents.json, for 7 days; the exports go once the orders are saved.
/// </summary>
internal static class MoPrintedDocuments
{
    public const int KeepDays = 7;
    private const string ListName = "documents.json";

    public static string TempRoot => Path.Combine(AppContext.BaseDirectory, "Temp");

    public static void Save(string folder, IReadOnlyList<MoPrintedDocument> documents)
    {
        if (documents.Count == 0) return;
        var list = documents.Select(d => d with { File = Path.GetFileName(d.File) }).ToList();
        File.WriteAllText(Path.Combine(folder, ListName), JsonSerializer.Serialize(list));
    }

    /// <summary>Every kept document, newest first, with its full path.</summary>
    public static List<MoPrintedDocument> All()
    {
        var all = new List<MoPrintedDocument>();
        if (!Directory.Exists(TempRoot)) return all;
        foreach (var folder in Directory.GetDirectories(TempRoot, "mo-*"))
        {
            var path = Path.Combine(folder, ListName);
            if (!File.Exists(path)) continue;
            try
            {
                foreach (var d in JsonSerializer.Deserialize<List<MoPrintedDocument>>(File.ReadAllText(path)) ?? [])
                {
                    var file = Path.Combine(folder, d.File);
                    if (File.Exists(file)) all.Add(d with { File = file });
                }
            }
            catch (JsonException) { }
        }
        return all.OrderByDescending(d => d.Printed).ThenBy(d => d.Kind).ToList();
    }

    /// <summary>Once the batch's orders are saved: the folder keeps only the printed documents.</summary>
    public static void KeepOnlyDocuments(string folder)
    {
        if (!Directory.Exists(folder)) return;
        var keep = File.Exists(Path.Combine(folder, ListName))
            ? (JsonSerializer.Deserialize<List<MoPrintedDocument>>(File.ReadAllText(Path.Combine(folder, ListName))) ?? []).Select(d => d.File).Append(ListName).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : [];
        if (keep.Count == 0) { Directory.Delete(folder, recursive: true); return; }
        foreach (var file in Directory.GetFiles(folder)) if (!keep.Contains(Path.GetFileName(file))) File.Delete(file);
        foreach (var dir in Directory.GetDirectories(folder)) Directory.Delete(dir, recursive: true);
    }

    /// <summary>Runs older than <see cref="KeepDays"/> days go.</summary>
    public static void Prune(AppLogger log)
    {
        if (!Directory.Exists(TempRoot)) return;
        foreach (var folder in Directory.GetDirectories(TempRoot, "mo-*"))
        {
            if (Directory.GetCreationTime(folder) > DateTime.Now.AddDays(-KeepDays)) continue;
            try { Directory.Delete(folder, recursive: true); log.Info("mo-batch", "old run folder removed"); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { log.Warn("mo-batch", $"old run folder left: {ex.Message}"); }
        }
    }
}

/// <summary>重新列印寄件單 (user, 2026-10-14): the kept documents, ticked and printed again.</summary>
internal sealed class MoReprintForm : Form
{
    private readonly CheckedListBox _list = new() { Dock = DockStyle.Fill, CheckOnClick = true, IntegralHeight = false };
    private readonly List<MoPrintedDocument> _documents;

    public List<MoPrintedDocument> Selected => _list.CheckedIndices.Cast<int>().Select(i => _documents[i]).ToList();

    public MoReprintForm(List<MoPrintedDocument> documents)
    {
        _documents = documents;
        Text = "MO店+ 重新列印寄件單";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(720, 460);
        MinimumSize = new Size(520, 320);
        MinimizeBox = false;
        ShowIcon = false;
        Font = new Font("Microsoft JhengHei UI", 10F);
        foreach (var d in documents)
            _list.Items.Add($"{d.Printed:MM/dd HH:mm}　{d.Kind}　{d.Detail}　{Path.GetFileName(d.File)}");
        var top = new Label
        {
            Dock = DockStyle.Top,
            Height = 48,
            Padding = new Padding(12, 10, 12, 0),
            Text = documents.Count == 0
                ? $"最近 {MoPrintedDocuments.KeepDays} 天沒有 MO店+ 全自動印過的寄件單。"
                : $"最近 {MoPrintedDocuments.KeepDays} 天 MO店+ 全自動印過的寄件單與總表。勾選要重印的，按「重新列印」。"
        };
        var pad = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 4, 12, 4) };
        pad.Controls.Add(_list);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 52, Padding = new Padding(8) };
        var print = new CyPrimaryButton { Text = "重新列印", Size = new Size(110, 34), DialogResult = DialogResult.OK };
        var close = new CyButton { Text = "關閉", Size = new Size(92, 34), DialogResult = DialogResult.Cancel };
        print.Click += (_, _) => { if (_list.CheckedItems.Count == 0) DialogResult = DialogResult.None; };
        buttons.Controls.Add(print);
        buttons.Controls.Add(close);
        Controls.Add(pad);
        Controls.Add(top);
        Controls.Add(buttons);
        AcceptButton = print;
        CancelButton = close;
        CyVisualTheme.Apply(this);
    }
}
