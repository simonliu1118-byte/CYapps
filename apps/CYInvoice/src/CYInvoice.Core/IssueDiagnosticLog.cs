using System.Globalization;
using CYInvoice.Core;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CYInvoice.Core.Storage;

// Diagnostic breadcrumbs are local-only and intentionally exclude raw API
// bodies, credentials, customer identities and invoice/order identifiers.
public static class IssueDiagnosticLog
{
    private sealed record Breadcrumb(string AtUtc, string Stage, string IssueType, string Code);
    private static readonly Regex CodePattern = new(
        @"(?<![0-9])(?:[1-5][0-9]{2}|[0-9]{7})(?![0-9])",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static string SafeCode(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return "未提供";
        var match = CodePattern.Match(message);
        return match.Success ? match.Value : "無可辨識代碼";
    }

    private static string PathFor(string dataDirectory, long issueId) =>
        Path.Combine(dataDirectory, "Logs", "IssueDiagnostics",
            issueId.ToString(CultureInfo.InvariantCulture) + ".jsonl");

    // Logging must never roll back a successfully persisted work issue.
    // Export detects missing or unreadable breadcrumbs and states the gap.
    public static bool TryAppend(string dataDirectory, long issueId, string stage,
        string issueType, string? providerMessage = null)
    {
        try
        {
            if (issueId <= 0 || string.IsNullOrWhiteSpace(dataDirectory)) return false;
            var path = PathFor(dataDirectory, issueId);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var line = JsonSerializer.Serialize(new Breadcrumb(
                DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                stage, issueType, SafeCode(providerMessage)));
            using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
            using var writer = new StreamWriter(stream, new UTF8Encoding(false));
            writer.WriteLine(line);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    public static string Export(string dataDirectory, InvoiceSyncIssue issue)
    {
        var output = new StringBuilder();
        output.AppendLine("CYInvoice 上傳問題診斷（已遮罩）");
        output.AppendLine("匯出時間 UTC：" + DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        output.AppendLine("本機問題 ID：" + issue.Id.ToString(CultureInfo.InvariantCulture));
        output.AppendLine("問題類型：" + issue.IssueType);
        output.AppendLine("建立時間 UTC：" + issue.CreatedUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        output.AppendLine("解決時間 UTC：" + (issue.ResolvedUtc?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) ?? "尚未解決"));
        output.AppendLine("最後可辨識錯誤代碼：" + SafeCode(issue.Message));
        output.AppendLine("環境：" + (issue.AccountKey.StartsWith("test|", StringComparison.Ordinal) ? "test" :
            issue.AccountKey.StartsWith("prod|", StringComparison.Ordinal) ||
            issue.AccountKey.StartsWith("production|", StringComparison.Ordinal) ? "production" : "未知"));
        output.AppendLine("作業系統版本：" + Environment.OSVersion.Version);
        output.AppendLine(".NET：" + Environment.Version);
        output.AppendLine("備註：不包含 App Key、密碼、Token、買方、公司統編、原發票／訂單號碼或原始 API 內容。");
        output.AppendLine("操作紀錄（最多最近 500 筆）：");
        try
        {
            var path = PathFor(dataDirectory, issue.Id);
            if (!File.Exists(path))
            {
                output.AppendLine("尚無診斷紀錄（舊版問題或記錄失敗）；不可由缺少紀錄推論未送出。");
                return output.ToString();
            }
            var events = File.ReadLines(path).TakeLast(500).ToArray();
            if (events.Length == 0) output.AppendLine("診斷檔為空，可能曾寫入失敗。");
            foreach (var line in events)
            {
                try
                {
                    var item = JsonSerializer.Deserialize<Breadcrumb>(line);
                    if (item is null) continue;
                    output.AppendLine(item.AtUtc + " | " + item.Stage + " | " +
                        item.IssueType + " | " + SafeCode(item.Code));
                }
                catch (JsonException)
                {
                    output.AppendLine("一筆診斷紀錄無法解析，請保留原本檔案供本機查核。");
                }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            output.AppendLine("診斷紀錄讀取失敗：" + error.GetType().Name +
                "。不能以缺少紀錄判定送出結果。");
        }
        return output.ToString();
    }

    public static string ExportFailed(InvoiceRecord record)
    {
        return "CYInvoice 開立失敗診斷（已遮罩）\n" +
            "匯出時間 UTC：" + DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture) + "\n" +
            "類型：開立失敗\n" +
            "可辨識錯誤代碼：" + SafeCode(record.ErrorMessage) + "\n" +
            "作業系統：" + Environment.OSVersion.Version + "\n.NET：" + Environment.Version + "\n" +
            "此舊式失敗記錄可能沒有完整嘗試歷程，請勿根據單一錯誤碼重新送出。\n" +
            "不包含原發票、買方、訂單與 API 原始回覆。\n";
    }
}
