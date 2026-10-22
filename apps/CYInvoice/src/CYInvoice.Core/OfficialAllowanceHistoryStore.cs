using System.Globalization;
using System.Text.Json;
using CYInvoice.Core.Amego;
using CYInvoice.Core.Storage;
using Microsoft.Data.Sqlite;

namespace CYInvoice.Core.Invoicing;

// Read-only view of the official observations written transactionally by
// InvoiceSyncRepository. Original invoice pruning is unchanged; these
// snapshots record provider state, NOT attribution of a local financial POST.
public sealed record OfficialAllowanceObservation(
    long AuditId, string InvoiceNumber, string AllowanceNumber,
    InvoiceAllowanceResult Snapshot, DateTimeOffset ObservedUtc);

public sealed class OfficialAllowanceHistoryStore(string dataDirectory)
{
    private readonly string path = Path.Combine(dataDirectory, SqliteBootstrapper.DatabaseFileName);

    public IReadOnlyList<OfficialAllowanceObservation> Read(
        string sellerInvoice, string environment, string invoiceNumber)
    {
        if (string.IsNullOrWhiteSpace(sellerInvoice) ||
            string.IsNullOrWhiteSpace(environment) || string.IsNullOrWhiteSpace(invoiceNumber))
            return [];
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false,
        }.ToString());
        connection.Open();

        using (var exists = connection.CreateCommand())
        {
            exists.CommandText = """
                SELECT COUNT(*) FROM sqlite_master
                WHERE type='table' AND name='official_allowance_observations';
                """;
            if (Convert.ToInt64(exists.ExecuteScalar(), CultureInfo.InvariantCulture) == 0)
                return []; // Existing installations have no backdated audit.
        }

        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT audit_id,invoice_number,allowance_number,snapshot_json,observed_utc
            FROM official_allowance_observations
            WHERE seller_invoice=$seller AND environment=$environment
              AND invoice_number=$invoice COLLATE NOCASE
            ORDER BY audit_id DESC;
            """;
        command.Parameters.AddWithValue("$seller", sellerInvoice.Trim());
        command.Parameters.AddWithValue("$environment", environment.Trim());
        command.Parameters.AddWithValue("$invoice", invoiceNumber.Trim());
        var observations = new List<OfficialAllowanceObservation>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            InvoiceAllowanceResult? snapshot;
            try { snapshot = JsonSerializer.Deserialize<InvoiceAllowanceResult>(reader.GetString(3)); }
            catch (JsonException error)
            {
                throw new InvalidDataException("官方折讓歷史紀錄格式錯誤，不可當成確認狀態", error);
            }
            if (snapshot is null)
                throw new InvalidDataException("官方折讓歷史缺少快照，不可當成確認狀態");
            observations.Add(new OfficialAllowanceObservation(
                reader.GetInt64(0), reader.GetString(1), reader.GetString(2), snapshot,
                DateTimeOffset.Parse(reader.GetString(4), CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind)));
        }
        return observations;
    }
}
