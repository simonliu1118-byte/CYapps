using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;

namespace CYInvoice.Core.Imports;

public static class XlsxRows
{
    public const int MaximumRows = 100_000;
    public const int MaximumColumns = 512;
    private const int MaximumPartBytes = 32 * 1024 * 1024;

    public static IReadOnlyList<IReadOnlyList<string>> ReadFirstWorksheet(string filePath)
    {
        using var archive = ZipFile.OpenRead(filePath);
        return ReadWorksheetRows(archive, WorksheetPath(archive, null));
    }

    public static IReadOnlyList<IReadOnlyList<string>> ReadWorksheet(string filePath, string worksheetName)
    {
        if (string.IsNullOrWhiteSpace(worksheetName)) throw new ArgumentException("工作表名稱不可空白", nameof(worksheetName));
        using var archive = ZipFile.OpenRead(filePath);
        return ReadWorksheetRows(archive, WorksheetPath(archive, worksheetName.Trim()));
    }

    private static IReadOnlyList<IReadOnlyList<string>> ReadWorksheetRows(ZipArchive archive, string worksheetPath)
    {
        var sharedStrings = ReadSharedStrings(archive);
        var worksheet = ReadXml(RequiredEntry(archive, worksheetPath, "xlsx 找不到指定工作表"));
        var rows = worksheet.Descendants().Where(element => element.Name.LocalName == "row").ToArray();
        if (rows.Length > MaximumRows)
        {
            throw new InvalidDataException($"xlsx 超過 {MaximumRows} 列，已停止匯入");
        }

        var result = new List<IReadOnlyList<string>>(rows.Length);
        foreach (var sourceRow in rows)
        {
            var row = new List<string>();
            foreach (var cell in sourceRow.Elements().Where(element => element.Name.LocalName == "c"))
            {
                var column = ColumnIndex((string?)cell.Attribute("r") ?? string.Empty);
                if (column >= MaximumColumns)
                {
                    throw new InvalidDataException($"xlsx 超過 {MaximumColumns} 欄，已停止匯入");
                }
                while (row.Count <= column)
                {
                    row.Add(string.Empty);
                }

                var type = (string?)cell.Attribute("t") ?? string.Empty;
                var value = cell.Elements().FirstOrDefault(element => element.Name.LocalName == "v")?.Value ?? string.Empty;
                if (type == "s")
                {
                    if (!int.TryParse(value, out var index) || index < 0 || index >= sharedStrings.Count)
                    {
                        throw new InvalidDataException($"xlsx 共用文字索引錯誤：{value}");
                    }
                    value = sharedStrings[index];
                }
                else if (type == "inlineStr")
                {
                    value = string.Concat(cell.Descendants().Where(element => element.Name.LocalName == "t").Select(element => element.Value));
                }
                row[column] = value;
            }
            result.Add(row);
        }
        return result;
    }

    public static int ColumnIndex(string reference)
    {
        var result = 0;
        var found = false;
        foreach (var character in reference)
        {
            if (character is < 'A' or > 'Z')
            {
                break;
            }
            found = true;
            result = checked(result * 26 + character - 'A' + 1);
        }
        return found ? result - 1 : 0;
    }

    private static IReadOnlyList<string> ReadSharedStrings(ZipArchive archive)
    {
        var entry = OptionalEntry(archive, "xl/sharedStrings.xml");
        if (entry is null)
        {
            return [];
        }
        var document = ReadXml(entry);
        return document.Descendants().Where(element => element.Name.LocalName == "si")
            .Select(item => string.Concat(item.Descendants().Where(element => element.Name.LocalName == "t").Select(element => element.Value)))
            .ToArray();
    }

    private static string WorksheetPath(ZipArchive archive, string? worksheetName)
    {
        var workbook = OptionalEntry(archive, "xl/workbook.xml");
        var relationships = OptionalEntry(archive, "xl/_rels/workbook.xml.rels");
        if (workbook is null && relationships is null && worksheetName is null)
        {
            return "xl/worksheets/sheet1.xml";
        }
        if (workbook is null || relationships is null)
        {
            throw new InvalidDataException("xlsx 工作表關聯資料不完整");
        }

        var workbookDocument = ReadXml(workbook);
        var sheets = workbookDocument.Descendants().Where(element => element.Name.LocalName == "sheet").ToArray();
        var sheet = worksheetName is null
            ? sheets.FirstOrDefault()
            : sheets.FirstOrDefault(element => string.Equals((string?)element.Attribute("name"), worksheetName, StringComparison.Ordinal));
        if (sheet is null)
        {
            throw new InvalidDataException(worksheetName is null ? "xlsx 找不到第一個工作表關聯" : $"xlsx 找不到工作表：{worksheetName}");
        }
        var relationshipId = sheet.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "id")?.Value ?? string.Empty;
        if (relationshipId.Length == 0)
        {
            throw new InvalidDataException(worksheetName is null ? "xlsx 找不到第一個工作表關聯" : $"xlsx 工作表 {worksheetName} 缺少關聯資料");
        }

        var relationshipDocument = ReadXml(relationships);
        var target = relationshipDocument.Descendants()
            .FirstOrDefault(element => element.Name.LocalName == "Relationship" && (string?)element.Attribute("Id") == relationshipId)
            ?.Attribute("Target")?.Value.Trim().Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(target))
        {
            throw new InvalidDataException(worksheetName is null ? "xlsx 找不到第一個工作表檔案" : $"xlsx 找不到工作表檔案：{worksheetName}");
        }
        target = target.StartsWith("/", StringComparison.Ordinal) ? target.TrimStart('/') : "xl/" + target;
        var segments = target.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(segment => segment == "..") || !target.StartsWith("xl/worksheets/", StringComparison.Ordinal))
        {
            throw new InvalidDataException("xlsx 工作表路徑不安全");
        }
        return target;
    }

    private static ZipArchiveEntry RequiredEntry(ZipArchive archive, string name, string message) =>
        OptionalEntry(archive, name) ?? throw new InvalidDataException(message);

    private static ZipArchiveEntry? OptionalEntry(ZipArchive archive, string name)
    {
        var matches = archive.Entries.Where(entry => entry.FullName == name).ToArray();
        if (matches.Length > 1)
        {
            throw new InvalidDataException($"xlsx 內含重複檔案：{name}");
        }
        return matches.FirstOrDefault();
    }

    private static XDocument ReadXml(ZipArchiveEntry entry)
    {
        if (entry.Length > MaximumPartBytes)
        {
            throw new InvalidDataException($"xlsx 內部檔案 {entry.FullName} 超過 32 MB，已停止匯入");
        }
        using var source = entry.Open();
        using var limited = new LimitedReadStream(source, MaximumPartBytes);
        using var reader = XmlReader.Create(limited, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaximumPartBytes,
        });
        try
        {
            return XDocument.Load(reader, LoadOptions.None);
        }
        catch (XmlException error)
        {
            throw new InvalidDataException($"讀取 xlsx 內部檔案 {entry.FullName} 失敗", error);
        }
    }

    private sealed class LimitedReadStream(Stream source, long maximumBytes) : Stream
    {
        private long readBytes;
        public override bool CanRead => source.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => readBytes; set => throw new NotSupportedException(); }
        public override void Flush() => source.Flush();
        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = source.Read(buffer, offset, count);
            Count(read);
            return read;
        }
        public override int Read(Span<byte> buffer)
        {
            var read = source.Read(buffer);
            Count(read);
            return read;
        }
        private void Count(int count)
        {
            readBytes += count;
            if (readBytes > maximumBytes)
            {
                throw new InvalidDataException("xlsx 解壓縮內容超過 32 MB，已停止匯入");
            }
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
