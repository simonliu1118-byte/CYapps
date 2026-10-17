using System.IO.Compression;
using System.Xml.Linq;

namespace CYERPAutoQuery;

/// <summary>
/// Minimal read-only .xlsx reader (copied from CYERPAutoInput and extended to every sheet):
/// returns each worksheet, by name, as rows of cell text. Handles shared strings, inline
/// strings and plain values; formulas are read as their cached value. No third-party
/// dependency, so neither Excel nor OpenOffice is needed to read ERP's export.
/// </summary>
internal static class XlsxReader
{
    private static readonly XNamespace Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRel = "http://schemas.openxmlformats.org/package/2006/relationships";

    public static IReadOnlyList<(string Name, List<string[]> Rows)> ReadSheets(Stream xlsx)
    {
        using var zip = new ZipArchive(xlsx, ZipArchiveMode.Read, leaveOpen: true);
        var shared = ReadSharedStrings(zip);
        var result = new List<(string, List<string[]>)>();
        foreach (var (name, path) in SheetPaths(zip))
        {
            var sheet = Load(zip, path) ?? throw new InvalidDataException($"Excel 工作表「{name}」讀取失敗。");
            result.Add((name, ReadRows(sheet, shared)));
        }
        if (result.Count == 0) throw new InvalidDataException("Excel 檔案沒有工作表。");
        return result;
    }

    private static List<string[]> ReadRows(XDocument sheet, IReadOnlyList<string> shared)
    {
        var rows = new List<string[]>();
        foreach (var row in sheet.Descendants(Main + "row"))
        {
            var cells = new SortedDictionary<int, string>();
            var next = 0;
            foreach (var cell in row.Elements(Main + "c"))
            {
                var reference = (string?)cell.Attribute("r");
                var column = reference is null ? next : ColumnIndex(reference);
                next = column + 1;
                cells[column] = CellText(cell, shared);
            }
            var width = cells.Count == 0 ? 0 : cells.Keys.Max() + 1;
            var values = new string[width];
            for (var i = 0; i < width; i++) values[i] = cells.TryGetValue(i, out var v) ? v : string.Empty;
            var rowIndex = (int?)row.Attribute("r");
            while (rowIndex is not null && rows.Count < rowIndex - 1) rows.Add([]);
            rows.Add(values);
        }
        return rows;
    }

    private static string CellText(XElement cell, IReadOnlyList<string> shared)
    {
        var type = (string?)cell.Attribute("t");
        if (type == "inlineStr")
            return string.Concat(cell.Descendants(Main + "t").Select(t => t.Value));
        var value = cell.Element(Main + "v")?.Value ?? string.Empty;
        if (type == "s" && int.TryParse(value, out var index) && index >= 0 && index < shared.Count)
            return shared[index];
        return value;
    }

    private static List<string> ReadSharedStrings(ZipArchive zip)
    {
        var doc = Load(zip, "xl/sharedStrings.xml");
        if (doc is null) return [];
        return doc.Root!.Elements(Main + "si")
            .Select(si => string.Concat(si.Descendants(Main + "t").Select(t => t.Value)))
            .ToList();
    }

    private static IEnumerable<(string Name, string Path)> SheetPaths(ZipArchive zip)
    {
        var workbook = Load(zip, "xl/workbook.xml") ?? throw new InvalidDataException("不是有效的 Excel（.xlsx）檔案。");
        var rels = Load(zip, "xl/_rels/workbook.xml.rels");
        var index = 0;
        foreach (var sheet in workbook.Descendants(Main + "sheet"))
        {
            index++;
            var name = (string?)sheet.Attribute("name") ?? $"Sheet{index}";
            var relId = (string?)sheet.Attribute(Rel + "id");
            var target = rels?.Root?.Elements(PackageRel + "Relationship")
                .FirstOrDefault(r => (string?)r.Attribute("Id") == relId)?
                .Attribute("Target")?.Value;
            if (string.IsNullOrEmpty(target))
            {
                yield return (name, $"xl/worksheets/sheet{index}.xml");
                continue;
            }
            target = target.Replace('\\', '/');
            yield return (name, target.StartsWith('/') ? target.TrimStart('/') : "xl/" + target);
        }
    }

    private static XDocument? Load(ZipArchive zip, string path)
    {
        var entry = zip.GetEntry(path);
        if (entry is null) return null;
        using var stream = entry.Open();
        return XDocument.Load(stream);
    }

    /// <summary>"A1" → 0, "C7" → 2, "AA3" → 26.</summary>
    internal static int ColumnIndex(string reference)
    {
        var column = 0;
        foreach (var ch in reference)
        {
            if (!char.IsAsciiLetter(ch)) break;
            column = column * 26 + (char.ToUpperInvariant(ch) - 'A' + 1);
        }
        return Math.Max(0, column - 1);
    }
}
