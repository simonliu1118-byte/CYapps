using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CYInvoice.Core.Storage;
using Microsoft.Data.Sqlite;

namespace CYInvoice.Core.Invoicing;

// Self-reported, READ-ONLY installation snapshot. This is neither a device
// attestation nor proof that all legacy installations have been inventoried.
// No data is written and no cutover/send authority can be issued here.
public sealed record LocalAllowanceVoidCutoverSnapshot(
    string InstallationId,
    string CapturedAtUtc,
    string EvidenceSha256,
    bool LocalG0501CurrentlyBlocked,
    bool PersistedCloudQuarantinePresent,
    long KnownClaimCount,
    long UnresolvedClaimCount,
    long UnclaimedApprovalCount,
    bool HistoricalClaimsReconciled,
    bool DeviceStoppedOrMigrated,
    bool CanAuthorizeCloudG0501);

public static class LocalAllowanceVoidCutoverEvidenceReader
{
    // The caller supplies a separately inventoried installation identifier;
    // this snapshot does NOT verify that identity or sign the report.
    public static LocalAllowanceVoidCutoverSnapshot Capture(
        LocalRepository repository, string installationId, DateTimeOffset capturedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(repository);
        if (string.IsNullOrWhiteSpace(installationId) ||
            installationId.Length is < 6 or > 100 ||
            installationId.Any(character => !(char.IsAsciiLetterOrDigit(character) ||
                character is '_' or '-')))
            throw new ArgumentException("無效的本機安裝清冊識別碼", nameof(installationId));

        // Opening a LocalAllowanceVoidStore would create tables/triggers.
        // Instead use SQLite read-only and fail CLOSED when the historical
        // g0501 tables are unavailable. Missing does not imply zero claims.
        var database = Path.Combine(repository.DataDirectory, SqliteBootstrapper.DatabaseFileName);
        using var db = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = database, Mode = SqliteOpenMode.ReadOnly, Pooling = false
        }.ToString());
        db.Open();
        using var transaction = db.BeginTransaction();
        using var sql = db.CreateCommand();
        sql.Transaction = transaction;
        sql.CommandText = """
            SELECT COUNT(*) FROM sqlite_master
            WHERE type='table' AND name IN
                ('allowance_void_local_approvals','allowance_void_local_claims',
                 'allowance_void_local_observations')
            """;
        if (Convert.ToInt64(sql.ExecuteScalar(), CultureInfo.InvariantCulture) != 3)
            throw new InvalidDataException("本機 g0501 稽核結構不完整，不得推定沒有歷史交易");

        sql.CommandText = """
            SELECT
                (SELECT COUNT(*) FROM allowance_void_local_claims),
                (SELECT COUNT(*) FROM allowance_void_local_approvals a
                 WHERE NOT EXISTS (SELECT 1 FROM allowance_void_local_claims c
                   WHERE c.approval_id=a.approval_id))
            """;
        using var reader = sql.ExecuteReader();
        if (!reader.Read()) throw new InvalidDataException("本機 g0501 歷史快照無法讀取");
        var claims = reader.GetInt64(0);
        var approvals = reader.GetInt64(1);
        reader.Close();
        transaction.Commit();

        var settings = repository.Settings.LoadOrCreate();
        var quarantine = settings.CloudVoidLocalSendBlocked;
        // Reflect current entry-point eligibility only. Even a persisted
        // quarantine bit can be tampered with by editing settings.json.
        var blocked = quarantine ||
            repository.IdentityProvider.Kind != IdentityProviderKind.Local ||
            settings.CloudMode != CloudModes.LocalOnly ||
            settings.CloudWorkspaceId.Length != 0 ||
            settings.CloudDeviceId.Length != 0 ||
            settings.CloudIdentityProvider == "CYID" ||
            settings.CloudEmployeeAuthorityReady ||
            settings.CloudCompanyAppliedRevision != 0;
        var timestamp = capturedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        var canonical = JsonSerializer.Serialize(new
        {
            version = 1, installationId, capturedAtUtc = timestamp,
            localG0501CurrentlyBlocked = blocked,
            persistedCloudQuarantinePresent = quarantine,
            knownClaimCount = claims,
            unresolvedClaimCount = claims,
            unclaimedApprovalCount = approvals,
            historicalClaimsReconciled = false,
            deviceStoppedOrMigrated = false,
            canAuthorizeCloudG0501 = false
        });
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();
        return new LocalAllowanceVoidCutoverSnapshot(
            installationId, timestamp, digest, blocked, quarantine,
            claims, claims, approvals,
            HistoricalClaimsReconciled: false,
            DeviceStoppedOrMigrated: false,
            CanAuthorizeCloudG0501: false);
    }
}
