using System.Globalization;
using CYInvoice.Core.Amego;
using CYInvoice.Core.Storage;
using Microsoft.Data.Sqlite;

namespace CYInvoice.Core.Invoicing;

public sealed record InvoiceRetentionResult(
    int Deleted,
    IReadOnlyList<string> Problems);

public sealed class InvoiceRetentionService
{
    private readonly LocalRepository repository;
    private readonly string databasePath;

    public InvoiceRetentionService(LocalRepository repository)
    {
        this.repository = repository ?? throw new ArgumentNullException(nameof(repository));
        databasePath = Path.Combine(repository.DataDirectory, SqliteBootstrapper.DatabaseFileName);
    }

    public InvoiceRetentionResult Prune(DateOnly today)
    {
        var account = CurrentAccount();
        var cutoff = account.Environment == Environments.Test
            ? today
            : InvoiceAutomaticSyncService.TwoPeriodRangeStart(today);
        var accountKey = account.Environment + "|" + account.SellerInvoice;

        using var connection = Open();
        Configure(connection);
        var candidates = LoadCandidates(connection, account)
            .Where(candidate => ShouldDelete(candidate, cutoff))
            .ToArray();
        var expiredIssueIds = LoadExpiredIssueIds(connection, accountKey, cutoff);
        if (candidates.Length == 0 && expiredIssueIds.Count == 0)
            return new InvoiceRetentionResult(0, []);

        using (var transaction = connection.BeginTransaction())
        {
            DeleteExpiredIssues(connection, transaction, expiredIssueIds);
            foreach (var candidate in candidates)
            {
                DeleteMatchingSyncIssues(connection, transaction, accountKey, candidate);

                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = "DELETE FROM invoices WHERE local_id = $local_id;";
                command.Parameters.AddWithValue("$local_id", candidate.LocalId);
                if (command.ExecuteNonQuery() != 1)
                    throw new InvalidDataException($"retention invoice {candidate.LocalId} was not deleted exactly once");
            }
            transaction.Commit();
        }

        var problems = new List<string>();
        foreach (var invoiceNumber in candidates
                     .Select(candidate => candidate.InvoiceNumber.Trim())
                     .Where(number => number.Length != 0)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            problems.AddRange(InvoiceCacheInvalidator
                .Invalidate(repository, account.Environment, invoiceNumber)
                .Select(problem => $"{invoiceNumber}: {problem}"));
        }

        return new InvoiceRetentionResult(candidates.Length, problems);
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWrite,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
        }.ToString());
        connection.Open();
        return connection;
    }

    private static void Configure(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA foreign_keys = ON;
            PRAGMA journal_mode = DELETE;
            PRAGMA synchronous = FULL;
            """;
        command.ExecuteNonQuery();
    }

    private static IReadOnlyList<Candidate> LoadCandidates(SqliteConnection connection, Account account)
    {
        var candidates = new List<Candidate>();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT local_id, invoice_number, original_order_id, order_id, api_order_id,
                   invoice_date, sent_at
            FROM invoices
            WHERE seller_invoice = $seller_invoice AND environment = $environment
            ORDER BY local_id;
            """;
        command.Parameters.AddWithValue("$seller_invoice", account.SellerInvoice);
        command.Parameters.AddWithValue("$environment", account.Environment);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            candidates.Add(new Candidate(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6)));
        }
        return candidates;
    }

    private static IReadOnlyList<long> LoadExpiredIssueIds(
        SqliteConnection connection,
        string accountKey,
        DateOnly cutoff)
    {
        var ids = new List<long>();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT local_id, created_utc
            FROM sync_issues
            WHERE account_key = $account_key
            ORDER BY local_id;
            """;
        command.Parameters.AddWithValue("$account_key", accountKey);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var text = reader.GetString(1).Trim();
            if (!DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var created))
                throw new InvalidDataException("sync_issues contains invalid created_utc");
            var localDate = DateOnly.FromDateTime(created.ToLocalTime().DateTime);
            if (localDate < cutoff) ids.Add(reader.GetInt64(0));
        }
        return ids;
    }

    private static void DeleteExpiredIssues(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyList<long> ids)
    {
        foreach (var id in ids)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "DELETE FROM sync_issues WHERE local_id = $local_id;";
            command.Parameters.AddWithValue("$local_id", id);
            if (command.ExecuteNonQuery() != 1)
                throw new InvalidDataException($"retention sync issue {id} was not deleted exactly once");
        }
    }

    private static void DeleteMatchingSyncIssues(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string accountKey,
        Candidate candidate)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            DELETE FROM sync_issues
            WHERE account_key = $account_key
              AND (
                    ($invoice_number <> '' AND invoice_number = $invoice_number)
                 OR ($original_order_id <> '' AND order_id = $original_order_id)
                 OR ($order_id <> '' AND order_id = $order_id)
                 OR ($api_order_id <> '' AND order_id = $api_order_id)
              );
            """;
        command.Parameters.AddWithValue("$account_key", accountKey);
        command.Parameters.AddWithValue("$invoice_number", candidate.InvoiceNumber.Trim());
        command.Parameters.AddWithValue("$original_order_id", candidate.OriginalOrderId.Trim());
        command.Parameters.AddWithValue("$order_id", candidate.OrderId.Trim());
        command.Parameters.AddWithValue("$api_order_id", candidate.ApiOrderId.Trim());
        command.ExecuteNonQuery();
    }

    private static bool ShouldDelete(Candidate candidate, DateOnly cutoff)
    {
        var date = RecordDate(candidate.InvoiceDate, candidate.SentAt);
        return date is not null && date.Value < cutoff;
    }

    private static DateOnly? RecordDate(string invoiceDate, string sentAt)
    {
        var direct = ParseDate(invoiceDate);
        if (direct is not null) return direct;

        var value = sentAt.Trim();
        if (value.Length >= 10 && (value[4] == '/' || value[4] == '-')) value = value[..10];
        else if (value.Length >= 8 && value[..8].All(char.IsAsciiDigit)) value = value[..8];
        return ParseDate(value);
    }

    private static DateOnly? ParseDate(string value)
    {
        value = value.Trim();
        foreach (var format in new[] { "yyyy/MM/dd", "yyyy-MM-dd", "yyyyMMdd" })
        {
            if (DateOnly.TryParseExact(value, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                return parsed;
        }
        return null;
    }

    private Account CurrentAccount()
    {
        var settings = repository.Settings.LoadOrCreate();
        var sellerInvoice = settings.Environment == Environments.Test
            ? AmegoDefaults.TestInvoice
            : settings.ProductionInvoice.Trim();
        if (sellerInvoice.Length == 0)
            throw new InvalidOperationException("目前環境缺少可識別的公司統編");
        return new Account(settings.Environment, sellerInvoice);
    }

    private sealed record Account(string Environment, string SellerInvoice);

    private sealed record Candidate(
        long LocalId,
        string InvoiceNumber,
        string OriginalOrderId,
        string OrderId,
        string ApiOrderId,
        string InvoiceDate,
        string SentAt);
}
