using System.Globalization;
using CYInvoice.Core;
using CYInvoice.Core.Cloud;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace CYInvoice.Core.Storage;

// Diagnostic breadcrumbs are local-only and intentionally exclude raw API
// bodies, credentials, customer identities and invoice/order identifiers.
public static class IssueDiagnosticLog
{
    private sealed record Breadcrumb(string AtUtc, string Stage, string IssueType, string Code,
        int? Attempt = null, bool? ResponseReceived = null, string? ErrorType = null,
        string? Stack = null, string? RequestId = null, int? HttpStatus = null);
    // Require an explicit API error label. An unlabeled number may belong
    // to a phone, a buyer, an invoice or another private identifier.
    private static readonly Regex CodePattern = new(
        @"(?:\bAPI[ _-]?code\b|\berror[ _-]?code\b|\bcode\b|光貿(?:錯誤)?代碼|錯誤碼)\s*[:=]?\s*(?<code>[0-9]{1,7})(?![0-9])",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SafeEventName = new(
        @"^[a-zA-Z0-9_:-]{1,72}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static string SafeCode(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return "未提供";
        var match = CodePattern.Match(message);
        return match.Success ? match.Groups["code"].Value : "無可辨識代碼";
    }

    // Same owner as the existing per-issue evidence. New files live beside the EXE.
    public static string LogDirectory(string dataDirectory) =>
        Path.Combine(string.Equals(Path.GetFileName(Path.GetFullPath(dataDirectory)), "Data", StringComparison.OrdinalIgnoreCase)
            ? Path.GetDirectoryName(Path.GetFullPath(dataDirectory))! : dataDirectory, "log");
    private static readonly Lock LogGate = new();
    private static string cleanedDay = "";
    private static string PathFor(string dataDirectory, long issueId) =>
        Path.Combine(LogDirectory(dataDirectory), "issue-" + issueId.ToString(CultureInfo.InvariantCulture) + ".jsonl");
    private static IEnumerable<string> ReadPathsFor(string dataDirectory, long issueId)
    {
        var legacy = Path.Combine(dataDirectory, "Logs", "IssueDiagnostics", issueId.ToString(CultureInfo.InvariantCulture) + ".jsonl");
        if (File.Exists(legacy)) yield return legacy;
        var current = PathFor(dataDirectory, issueId);
        if (File.Exists(current)) yield return current;
    }

    public static bool RecordCloud(string dataDirectory, string action, string phase, string requestId,
        string? applicationId = null, long? elapsedMs = null, int? httpStatus = null,
        bool? responseReceived = null, string? code = null, Exception? error = null,
        string? serverStage = null, string? status = null, string? executionState = null,
        int? attempt = null, int? providerCode = null, string? serverAtUtc = null)
    {
        try {
            var directory = LogDirectory(dataDirectory);
            var day = DateTime.UtcNow.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            var validId = applicationId is not null && Regex.IsMatch(applicationId, @"^[A-Za-z0-9_-]{8,100}$") ? applicationId : null;
            string? Safe(string? value) => value is not null && Regex.IsMatch(value, @"^[A-Za-z0-9_.:-]{1,128}$") ? value : null;
            var root = Path.GetDirectoryName(directory)!;
            var versionPath = Path.Combine(root, "VERSION");
            var buildPath = Path.Combine(root, "BUILD");
            var productVersion = File.Exists(versionPath) ? File.ReadAllText(versionPath).Trim() : typeof(IssueDiagnosticLog).Assembly.GetName().Version?.ToString();
            var build = File.Exists(buildPath) && int.TryParse(File.ReadAllText(buildPath).Trim(), out var buildNumber) ? buildNumber : (int?)null;
            var line = JsonSerializer.Serialize(new {
                format = "CYInvoice-diagnostic-1", atUtc = DateTimeOffset.UtcNow.ToString("O"),
                version = productVersion is not null && Regex.IsMatch(productVersion, @"^\d+\.\d+\.\d+(?:\.\d+)?$") ? productVersion : "unknown", build,
                runtime = Environment.Version.ToString(), action = Safe(action), phase = Safe(phase),
                requestId = Safe(requestId), applicationId = validId, elapsedMs, httpStatus,
                responseReceived, code = Safe(code), serverStage = Safe(serverStage),
                causeCode = Safe(error?.Data["CloudCauseCode"] as string),
                serverDiagnosticsSaved = error?.Data["CloudDiagnosticsSaved"] as bool?,
                status = Safe(status), executionState = Safe(executionState), attempt, providerCode,
                serverAtUtc = DateTimeOffset.TryParse(serverAtUtc, out var serverTime) ? serverTime.ToUniversalTime().ToString("O") : null,
                errorType = error?.GetType().FullName,
                stack = error is null ? null : new System.Diagnostics.StackTrace(error, false).ToString()
            });
            lock (LogGate) {
                Directory.CreateDirectory(directory);
                if (cleanedDay != directory + day) {
                    foreach (var old in Directory.EnumerateFiles(directory, "cloud-????????*.jsonl"))
                        if (File.GetLastWriteTimeUtc(old) < DateTime.UtcNow.AddDays(-14)) File.Delete(old);
                    cleanedDay = directory + day;
                }
                var path = Path.Combine(directory, "cloud-" + day + ".jsonl");
                if (File.Exists(path) && new FileInfo(path).Length >= 4 * 1024 * 1024)
                    File.Move(path, Path.Combine(directory, "cloud-" + day + "-previous.jsonl"), true);
                using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
                using var writer = new StreamWriter(stream, new UTF8Encoding(false));
                writer.WriteLine(line);writer.Flush();stream.Flush(true);
            }
            return true;
        } catch (Exception logError) when (logError is IOException or UnauthorizedAccessException or ArgumentException) {
            return false;
        }
    }

    public static void Record(string directory, long issueId, string stage, string issueType,
        int? code = null, Exception? error = null, int? attempt = null,
        bool? responseReceived = null, string? requestId = null)
    {
        var path = PathFor(directory, issueId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // Error messages can contain payloads. Keep type/stack and labelled
        // provider codes only; correlation never includes credentials or PII.
        var entry = new Breadcrumb(DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            stage, issueType, code?.ToString(CultureInfo.InvariantCulture) ?? (error is CloudApiException cloud && SafeEventName.IsMatch(cloud.Code) ? cloud.Code : SafeCode(error?.Message)),
            attempt, responseReceived, error?.GetType().FullName, error?.StackTrace,
            requestId is not null && SafeEventName.IsMatch(requestId) ? requestId : null,
            error is CloudApiException cloudError ? (int?)cloudError.StatusCode :
            error is HttpRequestException requestError ? (int?)requestError.StatusCode : error?.Data["HttpStatus"] as int?);
        using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.WriteLine(JsonSerializer.Serialize(entry));
        writer.Flush();
        stream.Flush(true);
    }

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
                SafeEventName.IsMatch(stage) ? stage : "unknown",
                SafeEventName.IsMatch(issueType) ? issueType : "unknown",
                SafeCode(providerMessage)));
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

    public static string Export(string dataDirectory, InvoiceSyncIssue issue, string? applicationVersion = null)
    {
        var output = new StringBuilder();
        output.AppendLine("CYInvoice 上傳問題診斷（已遮罩）");
        output.AppendLine("匯出時間 UTC：" + DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        output.AppendLine("本機問題 ID：" + issue.Id.ToString(CultureInfo.InvariantCulture));
        output.AppendLine("問題類型：" + (SafeEventName.IsMatch(issue.IssueType) ? issue.IssueType : "unknown"));
        output.AppendLine("建立時間 UTC：" + issue.CreatedUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        output.AppendLine("解決時間 UTC：" + (issue.ResolvedUtc?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) ?? "尚未解決"));
        output.AppendLine("最後可辨識錯誤代碼：" + SafeCode(issue.Message));
        output.AppendLine("環境：" + (issue.AccountKey.StartsWith("test|", StringComparison.Ordinal) ? "test" :
            issue.AccountKey.StartsWith("prod|", StringComparison.Ordinal) ||
            issue.AccountKey.StartsWith("production|", StringComparison.Ordinal) ? "production" : "未知"));
        output.AppendLine("程式版本：" + (applicationVersion ?? "未提供"));
        output.AppendLine("作業系統版本：" + Environment.OSVersion.Version);
        output.AppendLine(".NET：" + Environment.Version);
        output.AppendLine("備註：不包含 App Key、密碼、Token、買方、公司統編、原發票／訂單號碼或原始 API 內容。");
        AppendFinancialEvidence(output, dataDirectory, issue);
        output.AppendLine("操作紀錄（保留首錯與完整嘗試歷程）：");
        try
        {
            var paths = ReadPathsFor(dataDirectory, issue.Id).ToArray();
            if (paths.Length == 0)
            {
                output.AppendLine("尚無診斷紀錄（舊版問題或記錄失敗）；不可由缺少紀錄推論未送出。");
                return output.ToString();
            }
            var events = paths.SelectMany(File.ReadLines).ToArray();
            if (events.Length == 0) output.AppendLine("診斷檔為空，可能曾寫入失敗。");
            foreach (var line in events)
            {
                try
                {
                    var item = JsonSerializer.Deserialize<Breadcrumb>(line);
                    if (item is null) continue;
                    output.AppendLine(item.AtUtc + " | " +
                        (SafeEventName.IsMatch(item.Stage) ? item.Stage : "unknown") + " | " +
                        (SafeEventName.IsMatch(item.IssueType) ? item.IssueType : "unknown") +
                        " | " + (SafeEventName.IsMatch(item.Code) ? item.Code : "unknown") + " attempt=" + item.Attempt +
                        " response=" + item.ResponseReceived + " request=" + item.RequestId +
                        " http=" + item.HttpStatus + " error=" + item.ErrorType + "\n" + item.Stack);
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

    // Reconstruct original financial attempts from the EXISTING durable SQLite
    // owners. Export only sequence/status, never the actual allowance number,
    // company identifier, frozen payload, buyer, App Key or request body.
    // Both queries are bounded and use the existing work-item/issue indexes.
    private static void AppendFinancialEvidence(StringBuilder output,
        string dataDirectory, InvoiceSyncIssue issue)
    {
        if (issue.IssueType is not ("allowance_manual_review" or
            "allowance_void_manual_review")) return;
        try
        {
            using var db = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = System.IO.Path.Combine(dataDirectory,
                    SqliteBootstrapper.DatabaseFileName),
                Mode = SqliteOpenMode.ReadOnly, Pooling = false
            }.ToString());
            db.Open();
            bool Exists(string name)
            {
                using var query = db.CreateCommand();
                query.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name=$table LIMIT 1";
                query.Parameters.AddWithValue("$table", name);
                return query.ExecuteScalar() is not null;
            }

            if (issue.IssueType == "allowance_manual_review" &&
                Exists("allowance_number_ledger"))
            {
                output.AppendLine("原申請 g0401 配號／送出歷程（完整原操作）：");
                using var command = db.CreateCommand();
                command.CommandText = Exists("allowance_local_number_rejections") ? """
                    SELECT l.attempt, l.status, r.provider_code
                    FROM allowance_number_ledger l
                    LEFT JOIN allowance_local_number_rejections r
                      ON r.seller_invoice=l.seller_invoice
                     AND r.environment=l.environment
                     AND r.work_item_id=l.work_item_id
                     AND r.allowance_number=l.allowance_number
                    WHERE l.seller_invoice=$seller AND l.environment=$environment
                      AND l.work_item_id=$work
                    ORDER BY l.attempt ASC
                    """ : """
                    SELECT attempt, status, NULL FROM allowance_number_ledger
                    WHERE seller_invoice=$seller AND environment=$environment
                      AND work_item_id=$work
                    ORDER BY attempt ASC
                    """;
                var separator = issue.AccountKey.IndexOf('|');
                if (separator <= 0) return;
                command.Parameters.AddWithValue("$environment",
                    issue.AccountKey[..separator]);
                command.Parameters.AddWithValue("$seller",
                    issue.AccountKey[(separator + 1)..]);
                command.Parameters.AddWithValue("$work",
                    issue.Id.ToString(CultureInfo.InvariantCulture));
                using var reader = command.ExecuteReader();
                var count = 0;
                while (reader.Read())
                {
                    count++;
                    var state = reader.GetString(1);
                    var verifiedCode = !reader.IsDBNull(2) &&
                        reader.GetInt32(2) is 4040161 or 4040162
                        ? "；官方明確拒號碼 " + reader.GetInt32(2).ToString(CultureInfo.InvariantCulture)
                        : "";
                    output.AppendLine("嘗試 " + reader.GetInt32(0) + "：" +
                        (state is "reserved" or "sending" or "confirmed" or
                            "occupied_external" ? state : "unknown") + verifiedCode);
                }
            }
            else if (issue.IssueType == "allowance_void_manual_review" &&
                Exists("allowance_void_local_claims") &&
                Exists("allowance_void_local_observations"))
            {
                output.AppendLine("原申請 g0501 唯一 Claim 與官方結果歷程（完整原操作）：");
                using var command = db.CreateCommand();
                command.CommandText = """
                    SELECT o.kind FROM allowance_void_local_claims c
                    JOIN allowance_void_local_observations o
                        ON o.approval_id=c.approval_id
                    WHERE c.issue_id=$issue AND c.account_key=$account
                    ORDER BY o.audit_id ASC
                    """;
                command.Parameters.AddWithValue("$issue", issue.Id);
                command.Parameters.AddWithValue("$account", issue.AccountKey);
                using var reader = command.ExecuteReader();
                var count = 0;
                while (reader.Read())
                {
                    count++;
                    var kind = reader.GetString(0);
                    output.AppendLine("觀察 " + count + "：" +
                        (kind is "accepted_unconfirmed" or "rejected_unconfirmed" or
                            "unknown_result" or "official_void_observed_unattributed" or
                            "official_void_unconfirmed" ? kind : "unknown"));
                }
            }
            if(issue.IssueType=="allowance_void_manual_review" && Exists("allowance_void_local_attempts"))
            {
                using var attempts=db.CreateCommand();
                attempts.CommandText="SELECT t.attempt,t.response_kind,t.provider_code,t.started_utc FROM allowance_void_local_attempts t JOIN allowance_void_local_approvals a ON a.approval_id=t.approval_id WHERE a.issue_id=$issue AND a.account_key=$account ORDER BY t.attempt";
                attempts.Parameters.AddWithValue("$issue",issue.Id);attempts.Parameters.AddWithValue("$account",issue.AccountKey);
                using var rows=attempts.ExecuteReader();
                while(rows.Read())output.AppendLine($"g0501 嘗試 {rows.GetInt32(0)}：{rows.GetString(1)}，code={(rows.IsDBNull(2)?"unknown":rows.GetInt32(2).ToString(CultureInfo.InvariantCulture))}，UTC={rows.GetString(3)}");
            }
        }
        catch (Exception error) when (error is SqliteException or IOException or
            UnauthorizedAccessException or InvalidOperationException)
        {
            output.AppendLine("財務歷程暫時無法讀取：" + error.GetType().Name +
                "。不得據此判定沒有發送。");
        }
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
