using System.Globalization;
using System.Text.Json;
using CYInvoice.Core.Amego;
using CYInvoice.Core.Storage;
using Microsoft.Data.Sqlite;

namespace CYInvoice.Core.Invoicing;

// The Local-only counterpart of D1's future atomic allowance ledger.
// A reservation persists the COMPLETE numbered API payload before the first
// send. It is not itself permission to send: MarkSending must first succeed.
// A crash after MarkSending is uncertain, not an invitation to retry.
public sealed record LocalAllowanceReservation(
    string WorkItemId, string AllowanceNumber, string PayloadJson, string Status, int Sequence,
    string ApproverEmployeeNo, DateTimeOffset ApprovedAtUtc);

public sealed class LocalAllowanceNumberStore
{
    private readonly string databasePath;

    public LocalAllowanceNumberStore(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        databasePath = Path.Combine(dataDirectory, SqliteBootstrapper.DatabaseFileName);
        if (!File.Exists(databasePath))
            throw new FileNotFoundException("Initialize CYInvoice SQLite before allocating allowances", databasePath);
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS allowance_number_ledger (
                seller_invoice TEXT NOT NULL,
                environment TEXT NOT NULL,
                allowance_date TEXT NOT NULL,
                sequence INTEGER NOT NULL,
                allowance_number TEXT NOT NULL,
                work_item_id TEXT NOT NULL,
                attempt INTEGER NOT NULL,
                template_json TEXT NOT NULL,
                payload_json TEXT NOT NULL,
                status TEXT NOT NULL CHECK(status IN
                    ('reserved','sending','occupied_external','confirmed')), 
                created_utc TEXT NOT NULL,
                approver_employee_no TEXT NOT NULL,
                approved_at_utc TEXT NOT NULL,
                PRIMARY KEY (seller_invoice, environment, allowance_date, sequence),
                UNIQUE (seller_invoice, environment, work_item_id, attempt),
                UNIQUE (seller_invoice, environment, allowance_number)
            );
            """;
        command.ExecuteNonQuery();
        // This table records an exact official-number readback, NOT
        // proof that this application's HTTP POST created the allowance.
        // It is part of the existing Local SQLite database, not a second
        // Ledger or any new Cloudflare schema.
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS allowance_local_readback_audit (
                audit_id INTEGER PRIMARY KEY AUTOINCREMENT,
                seller_invoice TEXT NOT NULL,
                environment TEXT NOT NULL,
                work_item_id TEXT NOT NULL,
                allowance_number TEXT NOT NULL,
                result_kind TEXT NOT NULL CHECK(result_kind='full_match_unattributed'),
                snapshot_sha256 TEXT NOT NULL CHECK(length(snapshot_sha256)=64),
                approver_employee_no TEXT NOT NULL,
                checked_at_utc TEXT NOT NULL
            );
            CREATE TRIGGER IF NOT EXISTS allowance_local_readback_no_update
            BEFORE UPDATE ON allowance_local_readback_audit
            BEGIN SELECT RAISE(ABORT,'ALLOWANCE_READBACK_AUDIT_APPEND_ONLY'); END;
            CREATE TRIGGER IF NOT EXISTS allowance_local_readback_no_delete
            BEFORE DELETE ON allowance_local_readback_audit
            BEGIN SELECT RAISE(ABORT,'ALLOWANCE_READBACK_AUDIT_APPEND_ONLY'); END;
            CREATE TABLE IF NOT EXISTS allowance_local_collision_reviews (
                review_id INTEGER PRIMARY KEY AUTOINCREMENT,
                seller_invoice TEXT NOT NULL,
                environment TEXT NOT NULL,
                work_item_id TEXT NOT NULL,
                previous_number TEXT NOT NULL,
                new_number TEXT NOT NULL,
                previous_attempt INTEGER NOT NULL CHECK(previous_attempt>0),
                official_snapshot_sha256 TEXT NOT NULL CHECK(
                    length(official_snapshot_sha256)=64 AND
                    official_snapshot_sha256 NOT GLOB '*[^0-9a-f]*'),
                manager_employee_no TEXT NOT NULL,
                reviewed_at_utc TEXT NOT NULL,
                UNIQUE(seller_invoice,environment,work_item_id,previous_attempt)
            );
            CREATE TRIGGER IF NOT EXISTS allowance_local_collision_review_no_update
            BEFORE UPDATE ON allowance_local_collision_reviews
            BEGIN SELECT RAISE(ABORT,'ALLOWANCE_COLLISION_REVIEW_APPEND_ONLY'); END;
            CREATE TRIGGER IF NOT EXISTS allowance_local_collision_review_no_delete
            BEFORE DELETE ON allowance_local_collision_reviews
            BEGIN SELECT RAISE(ABORT,'ALLOWANCE_COLLISION_REVIEW_APPEND_ONLY'); END;
            """;
        command.ExecuteNonQuery();
    }

    public LocalAllowanceReservation Reserve(
        string sellerInvoice, string environment, string workItemId,
        DateOnly date, AllowanceIssueRequest template,
        string approverEmployeeNo, DateTimeOffset approvedAtUtc) =>
        ReserveCore(sellerInvoice,environment,workItemId,date,template,
            approverEmployeeNo,approvedAtUtc,null,null);

    // This is deliberately INTERNAL: caller must revalidate the manager,
    // immutable approval and exact provider-origin collision evidence.
    // It never posts g0401 and cannot release a 'sending' Ledger.
    internal LocalAllowanceReservation ReserveAfterVerifiedExternalCollision(
        string sellerInvoice, string environment, string workItemId,
        DateOnly date, AllowanceIssueRequest template,
        string approverEmployeeNo, DateTimeOffset approvedAtUtc,
        LocalAllowanceReservation previous,string officialSnapshotSha256) =>
        ReserveCore(sellerInvoice,environment,workItemId,date,template,
            approverEmployeeNo,approvedAtUtc,previous,officialSnapshotSha256);

    private LocalAllowanceReservation ReserveCore(
        string sellerInvoice, string environment, string workItemId,
        DateOnly date, AllowanceIssueRequest template,
        string approverEmployeeNo, DateTimeOffset approvedAtUtc,
        LocalAllowanceReservation? reviewedCollision,string? officialSnapshotSha256)
    {
        if (reviewedCollision is not null &&
            (officialSnapshotSha256 is null || officialSnapshotSha256.Length!=64 ||
             officialSnapshotSha256.Any(c=>c is not (>= '0' and <= '9' or >= 'a' and <= 'f'))))
            throw new InvalidDataException("Verified official collision snapshot SHA256 required");
        sellerInvoice = Require(sellerInvoice, nameof(sellerInvoice));
        environment = Require(environment, nameof(environment));
        workItemId = Require(workItemId, nameof(workItemId));
        approverEmployeeNo = Require(approverEmployeeNo, nameof(approverEmployeeNo));
        if (approvedAtUtc == default)
            throw new ArgumentException("A verified manager approval time is required", nameof(approvedAtUtc));
        approvedAtUtc = approvedAtUtc.ToUniversalTime();
        ArgumentNullException.ThrowIfNull(template);
        if (template.AllowanceType != 2 || template.ProductItem is null || template.ProductItem.Count == 0)
            throw new ArgumentException("Only nonempty seller allowances may be reserved", nameof(template));
        if (template.AllowanceNumber.Trim().Length != 0)
            throw new ArgumentException("Pass an unnumbered template; this store owns allocation", nameof(template));
        if (template.AllowanceDate != date.ToString("yyyyMMdd", CultureInfo.InvariantCulture))
            throw new ArgumentException("Template allowance date differs from allocation date", nameof(template));

        var templateJson = JsonSerializer.Serialize(template);
        var dateText = date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        using var connection = Open();
        using var transaction = connection.BeginTransaction(deferred: false);
        LocalAllowanceReservation? latest = null;
        string? originalTemplate = null;
        var previousAttempt = 0;
        using (var lookup = connection.CreateCommand())
        {
            lookup.Transaction = transaction;
            lookup.CommandText = """
                SELECT allowance_number, payload_json, status, sequence, attempt, allowance_date, template_json,
                       approver_employee_no, approved_at_utc
                FROM allowance_number_ledger
                WHERE seller_invoice=$seller AND environment=$environment AND work_item_id=$work
                ORDER BY attempt DESC LIMIT 1;
                """;
            lookup.Parameters.AddWithValue("$seller", sellerInvoice);
            lookup.Parameters.AddWithValue("$environment", environment);
            lookup.Parameters.AddWithValue("$work", workItemId);
            using var reader = lookup.ExecuteReader();
            if (reader.Read())
            {
                if (!string.Equals(reader.GetString(5), dateText, StringComparison.Ordinal))
                    throw new InvalidOperationException("Existing Work Item allowance date cannot change");
                latest = new LocalAllowanceReservation(
                    workItemId, reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3),
                    reader.GetString(7), DateTimeOffset.Parse(reader.GetString(8), CultureInfo.InvariantCulture));
                previousAttempt = reader.GetInt32(4);
                originalTemplate = reader.GetString(6);
            }
        }
        if (latest is not null)
        {
            if (!string.Equals(originalTemplate, templateJson, StringComparison.Ordinal) ||
                !string.Equals(latest.ApproverEmployeeNo, approverEmployeeNo, StringComparison.Ordinal) ||
                latest.ApprovedAtUtc != approvedAtUtc)
                throw new InvalidOperationException("Approved Work Item payload and manager identity are immutable");
            if (latest.Status == "reserved" && reviewedCollision is null)
            {
                transaction.Commit();
                return latest; // same number and immutable payload; no allocation
            }
            // Only a distinct operation after a provider-confirmed collision.
            // Never reallocate a sending/confirmed/unknown original attempt.
            if (latest.Status!="occupied_external" ||
                reviewedCollision is null || latest != reviewedCollision)
                throw new InvalidOperationException(
                    "Only a freshly revalidated external collision supports a new reservation");
        }

        if (reviewedCollision is not null && latest is null)
            throw new InvalidOperationException("Missing original collision attempt");
        int next;
        using (var sequence = connection.CreateCommand())
        {
            sequence.Transaction = transaction;
            sequence.CommandText = """
                SELECT COALESCE(MAX(sequence), 0) + 1 FROM allowance_number_ledger
                WHERE seller_invoice=$seller AND environment=$environment AND allowance_date=$date;
                """;
            sequence.Parameters.AddWithValue("$seller", sellerInvoice);
            sequence.Parameters.AddWithValue("$environment", environment);
            sequence.Parameters.AddWithValue("$date", dateText);
            next = Convert.ToInt32(sequence.ExecuteScalar(), CultureInfo.InvariantCulture);
        }
        if (next > AllowanceNumberGenerator.DailyMaximum)
            throw new InvalidOperationException("Allowance daily numbering capacity exhausted");
        var number = AllowanceNumberGenerator.Format(date, next);
        // Keep a complete frozen payload together with the durable assignment.
        var payload = new AllowanceIssueRequest
        {
            AllowanceNumber = number,
            AllowanceDate = template.AllowanceDate,
            AllowanceType = template.AllowanceType,
            BuyerIdentifier = template.BuyerIdentifier,
            BuyerName = template.BuyerName,
            BuyerAddress = template.BuyerAddress,
            BuyerTelephoneNumber = template.BuyerTelephoneNumber,
            BuyerEmailAddress = template.BuyerEmailAddress,
            ProductItem = template.ProductItem,
            TaxAmount = template.TaxAmount,
            TotalAmount = template.TotalAmount,
        };
        var payloadJson = JsonSerializer.Serialize(payload);
        using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO allowance_number_ledger
                (seller_invoice,environment,allowance_date,sequence,allowance_number,work_item_id,
                 attempt,template_json,payload_json,status,created_utc,approver_employee_no,approved_at_utc)
                VALUES ($seller,$environment,$date,$sequence,$number,$work,
                        $attempt,$template,$payload,'reserved',$created,$approver,$approved);
                """;
            insert.Parameters.AddWithValue("$seller", sellerInvoice);
            insert.Parameters.AddWithValue("$environment", environment);
            insert.Parameters.AddWithValue("$date", dateText);
            insert.Parameters.AddWithValue("$sequence", next);
            insert.Parameters.AddWithValue("$number", number);
            insert.Parameters.AddWithValue("$work", workItemId);
            insert.Parameters.AddWithValue("$attempt", previousAttempt + 1);
            insert.Parameters.AddWithValue("$template", templateJson);
            insert.Parameters.AddWithValue("$payload", payloadJson);
            insert.Parameters.AddWithValue("$created", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            insert.Parameters.AddWithValue("$approver", approverEmployeeNo);
            insert.Parameters.AddWithValue("$approved", approvedAtUtc.ToString("O", CultureInfo.InvariantCulture));
            if (insert.ExecuteNonQuery() != 1)
                throw new InvalidDataException("Failed to reserve allowance number");
        }
        if (reviewedCollision is not null)
        {
            // One atomic transaction: the new immutable reservation cannot
            // exist without a privacy-minimized, append-only manager review.
            using var audit=connection.CreateCommand();
            audit.Transaction=transaction;
            audit.CommandText="""
                INSERT INTO allowance_local_collision_reviews (
                    seller_invoice,environment,work_item_id,previous_number,
                    new_number,previous_attempt,official_snapshot_sha256,
                    manager_employee_no,reviewed_at_utc)
                SELECT seller_invoice,environment,work_item_id,allowance_number,
                    $new,attempt,$hash,approver_employee_no,$now
                FROM allowance_number_ledger
                WHERE seller_invoice=$seller AND environment=$env
                  AND work_item_id=$work AND allowance_number=$old
                  AND attempt=$attempt AND status='occupied_external'
                  AND approver_employee_no=$manager AND approved_at_utc=$approved;
                """;
            audit.Parameters.AddWithValue("$new",number);
            audit.Parameters.AddWithValue("$hash",officialSnapshotSha256!);
            audit.Parameters.AddWithValue("$now",DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture));
            audit.Parameters.AddWithValue("$seller",sellerInvoice);
            audit.Parameters.AddWithValue("$env",environment);
            audit.Parameters.AddWithValue("$work",workItemId);
            audit.Parameters.AddWithValue("$old",reviewedCollision.AllowanceNumber);
            audit.Parameters.AddWithValue("$attempt",previousAttempt);
            audit.Parameters.AddWithValue("$manager",approverEmployeeNo);
            audit.Parameters.AddWithValue("$approved",approvedAtUtc.ToString("O",CultureInfo.InvariantCulture));
            if (audit.ExecuteNonQuery()!=1)
                throw new InvalidOperationException("Collision review was not durably recorded");
        }
        transaction.Commit();
        return new LocalAllowanceReservation(workItemId, number, payloadJson, "reserved", next,
            approverEmployeeNo, approvedAtUtc);
    }

    // Read-only recovery by the original local work item identity. A sending
    // reservation is NOT permission to send again; only official readback can
    // promote it to confirmed.
    public LocalAllowanceReservation? FindLatest(
        string sellerInvoice, string environment, string workItemId)
    {
        using var connection = Open();
        using var query = connection.CreateCommand();
        query.CommandText = """
            SELECT allowance_number, payload_json, status, sequence, approver_employee_no, approved_at_utc
            FROM allowance_number_ledger
            WHERE seller_invoice=$seller AND environment=$environment
              AND work_item_id=$work
            ORDER BY attempt DESC LIMIT 1;
            """;
        query.Parameters.AddWithValue("$seller", Require(sellerInvoice, nameof(sellerInvoice)));
        query.Parameters.AddWithValue("$environment", Require(environment, nameof(environment)));
        query.Parameters.AddWithValue("$work", Require(workItemId, nameof(workItemId)));
        using var reader = query.ExecuteReader();
        return reader.Read()
            ? new LocalAllowanceReservation(workItemId, reader.GetString(0),
                reader.GetString(1), reader.GetString(2), reader.GetInt32(3),
                reader.GetString(4), DateTimeOffset.Parse(reader.GetString(5), CultureInfo.InvariantCulture))
            : null;
    }

    // Must commit immediately BEFORE sending the frozen payload. Never mark
    // an already-sending operation as safe for retry after a crash or timeout.
    public void MarkSending(string sellerInvoice, string environment, LocalAllowanceReservation reservation) =>
        Move(sellerInvoice, environment, reservation, "reserved", "sending");

    // Caller may invoke this only for the actual FIRST g0401 response explicitly
    // rejecting this number; any uncertain prior attempt must be reconciled.
    public void MarkExternalCollision(string sellerInvoice, string environment,
        LocalAllowanceReservation reservation, int confirmedAmegoCode)
    {
        if (!AmegoAllowanceErrorCodes.IsAllowanceNumberOccupied(confirmedAmegoCode))
            throw new ArgumentException("No confirmed AMEGO number collision", nameof(confirmedAmegoCode));
        Move(sellerInvoice, environment, reservation, "sending", "occupied_external");
    }

    // Record official *content* proof separately from transaction-origin proof.
    // Even a complete match must remain 'sending' until an independently
    // attributable financial confirmation policy is approved. No PII is
    // written to the audit table, only a digest of the queried snapshot.
    internal void RecordUnattributedFullMatch(string sellerInvoice,string environment,
        LocalAllowanceReservation reservation,string snapshotSha256)
    {
        ArgumentNullException.ThrowIfNull(reservation);
        if (snapshotSha256 is null || snapshotSha256.Length != 64 ||
            snapshotSha256.Any(c => c is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
            throw new ArgumentException("Exact readback SHA256 required",nameof(snapshotSha256));
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO allowance_local_readback_audit (
                seller_invoice,environment,work_item_id,allowance_number,
                result_kind,snapshot_sha256,approver_employee_no,checked_at_utc)
            SELECT seller_invoice,environment,work_item_id,allowance_number,
                'full_match_unattributed',$digest,approver_employee_no,$at
            FROM allowance_number_ledger
            WHERE seller_invoice=$seller AND environment=$env AND
                work_item_id=$work AND allowance_number=$number AND
                payload_json=$payload AND approver_employee_no=$approver AND
                approved_at_utc=$approved AND status='sending'
            ORDER BY attempt DESC LIMIT 1;
            """;
        command.Parameters.AddWithValue("$seller",Require(sellerInvoice,nameof(sellerInvoice)));
        command.Parameters.AddWithValue("$env",Require(environment,nameof(environment)));
        command.Parameters.AddWithValue("$work",reservation.WorkItemId);
        command.Parameters.AddWithValue("$number",reservation.AllowanceNumber);
        command.Parameters.AddWithValue("$payload",reservation.PayloadJson);
        command.Parameters.AddWithValue("$approver",reservation.ApproverEmployeeNo);
        command.Parameters.AddWithValue("$approved",reservation.ApprovedAtUtc.ToString("O",
            CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$digest",snapshotSha256);
        command.Parameters.AddWithValue("$at",DateTimeOffset.UtcNow.ToString("O",
            CultureInfo.InvariantCulture));
        if (command.ExecuteNonQuery()!=1)
            throw new InvalidOperationException("Local allowance Ledger changed; no readback audit written");
    }

    // Reserved for a later independent, auditable origin-attribution flow.
    // Neither code=0 nor a matching allowance_query authorizes calling this.
    // No current Local API can establish attributable g0401 origin proof.
    // Keep the historical entrypoint fail-closed rather than letting a
    // caller treat code=0 or a matching number as success.
    public void MarkOfficiallyConfirmed(string sellerInvoice, string environment,
        LocalAllowanceReservation reservation) =>
        throw new InvalidOperationException(
            "g0401 origin not attributable: preserve sending and seek audited human reconciliation");

    private void Move(string sellerInvoice, string environment,
        LocalAllowanceReservation reservation, string from, string to)
    {
        ArgumentNullException.ThrowIfNull(reservation);
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE allowance_number_ledger SET status=$to
            WHERE seller_invoice=$seller AND environment=$environment
              AND work_item_id=$work AND allowance_number=$number
              AND payload_json=$payload AND status=$from;
            """;
        command.Parameters.AddWithValue("$to", to);
        command.Parameters.AddWithValue("$from", from);
        command.Parameters.AddWithValue("$seller", Require(sellerInvoice, nameof(sellerInvoice)));
        command.Parameters.AddWithValue("$environment", Require(environment, nameof(environment)));
        command.Parameters.AddWithValue("$work", reservation.WorkItemId);
        command.Parameters.AddWithValue("$number", reservation.AllowanceNumber);
        command.Parameters.AddWithValue("$payload", reservation.PayloadJson);
        if (command.ExecuteNonQuery() != 1)
            throw new InvalidOperationException("Allowance operation state changed or was already sent; reconcile with AMEGO");
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath, Mode = SqliteOpenMode.ReadWrite,
            Cache = SqliteCacheMode.Private, Pooling = false,
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA busy_timeout=5000; PRAGMA synchronous=FULL;";
        command.ExecuteNonQuery();
        return connection;
    }

    private static string Require(string value, string name) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value required", name) : value.Trim();
}
