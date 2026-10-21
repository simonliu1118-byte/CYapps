using System.Globalization;
using CYInvoice.Core.Storage;
using Microsoft.Data.Sqlite;

namespace CYInvoice.Core.Invoicing;

public sealed record LocalAllowanceVoidApproval(
    string ApprovalId, long IssueId, string AccountKey, string AllowanceNumber,
    string RequestHash, string OfficialHash, string ManagerEmployeeNo,
    DateTimeOffset ExpiresUtc, bool Claimed, string LastObservation);

// The existing SQLite owns approvals, permanent send claims and append-only
// observations. No queue runner may turn an observation into another POST.
internal sealed class LocalAllowanceVoidStore
{
    private readonly string path;
    public LocalAllowanceVoidStore(string directory)
    {
        path = Path.Combine(directory, SqliteBootstrapper.DatabaseFileName);
        using var db = Open();
        using var sql = db.CreateCommand();
        sql.CommandText = """
            CREATE TABLE IF NOT EXISTS allowance_void_local_approvals (
                approval_id TEXT PRIMARY KEY, issue_id INTEGER NOT NULL REFERENCES sync_issues(local_id),
                account_key TEXT NOT NULL, allowance_number TEXT NOT NULL,
                request_hash TEXT NOT NULL CHECK(length(request_hash)=64),
                official_hash TEXT NOT NULL CHECK(length(official_hash)=64),
                manager_employee_no TEXT NOT NULL, approved_utc TEXT NOT NULL, expires_utc TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_allowance_void_local_issue
                ON allowance_void_local_approvals(account_key,issue_id,approved_utc DESC);
            CREATE TABLE IF NOT EXISTS allowance_void_local_claims (
                account_key TEXT NOT NULL, allowance_number TEXT NOT NULL,
                approval_id TEXT NOT NULL UNIQUE REFERENCES allowance_void_local_approvals(approval_id),
                issue_id INTEGER NOT NULL UNIQUE REFERENCES sync_issues(local_id), claimed_utc TEXT NOT NULL,
                PRIMARY KEY(account_key,allowance_number)
            );
            CREATE TABLE IF NOT EXISTS allowance_void_local_observations (
                audit_id INTEGER PRIMARY KEY AUTOINCREMENT,
                approval_id TEXT NOT NULL REFERENCES allowance_void_local_approvals(approval_id),
                kind TEXT NOT NULL CHECK(kind IN ('accepted_unconfirmed','rejected_unconfirmed',
                    'unknown_result','official_void_observed_unattributed','official_void_unconfirmed')),
                snapshot_hash TEXT NOT NULL, actor_employee_no TEXT NOT NULL, observed_utc TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_allowance_void_local_observations
                ON allowance_void_local_observations(approval_id,audit_id DESC);
            CREATE TRIGGER IF NOT EXISTS allowance_void_local_no_issue_close
            BEFORE UPDATE OF resolved_utc ON sync_issues
            WHEN NEW.resolved_utc<>OLD.resolved_utc AND EXISTS
                (SELECT 1 FROM allowance_void_local_claims WHERE issue_id=OLD.local_id)
            BEGIN SELECT RAISE(ABORT,'ALLOWANCE_VOID_SENT_READBACK_ONLY'); END;
            CREATE TRIGGER IF NOT EXISTS allowance_void_local_no_issue_delete
            BEFORE DELETE ON sync_issues WHEN EXISTS
                (SELECT 1 FROM allowance_void_local_approvals WHERE issue_id=OLD.local_id)
            BEGIN SELECT RAISE(ABORT,'ALLOWANCE_VOID_HISTORY_REQUIRED'); END;
            """;
        sql.ExecuteNonQuery();
        foreach (var table in new[] { "approvals", "claims", "observations" })
        foreach (var operation in new[] { "UPDATE", "DELETE" })
        {
            sql.CommandText = $"""
                CREATE TRIGGER IF NOT EXISTS allowance_void_local_{table}_no_{operation}
                BEFORE {operation} ON allowance_void_local_{table}
                BEGIN SELECT RAISE(ABORT,'ALLOWANCE_VOID_APPEND_ONLY'); END;
                """;
            sql.ExecuteNonQuery();
        }
    }

    public LocalAllowanceVoidApproval? Find(string account, long issue)
    {
        using var db = Open();
        using var sql = db.CreateCommand();
        sql.CommandText = """
            SELECT a.approval_id,a.issue_id,a.account_key,a.allowance_number,
                a.request_hash,a.official_hash,a.manager_employee_no,a.expires_utc,
                EXISTS(SELECT 1 FROM allowance_void_local_claims c WHERE c.approval_id=a.approval_id),
                COALESCE((SELECT kind FROM allowance_void_local_observations o
                    WHERE o.approval_id=a.approval_id ORDER BY audit_id DESC LIMIT 1),'')
            FROM allowance_void_local_approvals a WHERE a.account_key=$account AND a.issue_id=$issue
            ORDER BY EXISTS(SELECT 1 FROM allowance_void_local_claims c WHERE c.approval_id=a.approval_id) DESC,
                a.approved_utc DESC,a.rowid DESC LIMIT 1
            """;
        sql.Parameters.AddWithValue("$account", account);
        sql.Parameters.AddWithValue("$issue", issue);
        using var reader = sql.ExecuteReader();
        return reader.Read() ? new(reader.GetString(0), reader.GetInt64(1), reader.GetString(2),
            reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetString(6),
            DateTimeOffset.Parse(reader.GetString(7), CultureInfo.InvariantCulture),
            reader.GetInt64(8) == 1, reader.GetString(9)) : null;
    }

    public void Approve(LocalAllowanceVoidApproval a, DateTimeOffset time)
    {
        using var db = Open();
        using var sql = db.CreateCommand();
        sql.CommandText = """
            INSERT INTO allowance_void_local_approvals
                (approval_id,issue_id,account_key,allowance_number,request_hash,official_hash,
                 manager_employee_no,approved_utc,expires_utc)
            SELECT $id,$issue,$account,$number,$request,$official,$manager,$now,$expiry
            WHERE EXISTS(SELECT 1 FROM sync_issues WHERE local_id=$issue AND account_key=$account
                AND issue_type='allowance_void_manual_review' AND TRIM(resolved_utc)='')
            AND NOT EXISTS(SELECT 1 FROM allowance_void_local_claims
                WHERE account_key=$account AND allowance_number=$number)
            """;
        Bind(sql, a);
        sql.Parameters.AddWithValue("$now", Utc(time));
        sql.Parameters.AddWithValue("$expiry", Utc(a.ExpiresUtc));
        if (sql.ExecuteNonQuery() != 1) throw new InvalidOperationException("折讓作廢待辦已變更或原號已送出");
    }

    public void Claim(LocalAllowanceVoidApproval a, DateTimeOffset time)
    {
        using var db = Open();
        using var sql = db.CreateCommand();
        sql.CommandText = """
            INSERT INTO allowance_void_local_claims(account_key,allowance_number,approval_id,issue_id,claimed_utc)
            SELECT a.account_key,a.allowance_number,a.approval_id,a.issue_id,$now
            FROM allowance_void_local_approvals a JOIN sync_issues i ON i.local_id=a.issue_id
            WHERE a.approval_id=$id AND a.account_key=$account AND a.issue_id=$issue
                AND a.allowance_number=$number AND a.request_hash=$request
                AND a.official_hash=$official AND a.manager_employee_no=$manager
                AND a.approved_utc<=$now AND a.expires_utc>$now
                AND i.account_key=a.account_key AND i.issue_type='allowance_void_manual_review'
                AND TRIM(i.resolved_utc)=''
                AND a.approval_id=(SELECT approval_id FROM allowance_void_local_approvals
                    WHERE account_key=$account AND issue_id=$issue
                    ORDER BY approved_utc DESC,rowid DESC LIMIT 1)
            """;
        Bind(sql, a);
        sql.Parameters.AddWithValue("$now", Utc(time));
        try
        {
            if (sql.ExecuteNonQuery() != 1) throw new InvalidOperationException("作廢核准已過期或待辦已變更");
        }
        catch (SqliteException error)
        {
            throw new InvalidOperationException("原折讓單已有送出紀錄，只能查回，禁止重送", error);
        }
    }

    public void Observe(LocalAllowanceVoidApproval a, string kind, string hash, string actor, DateTimeOffset time)
    {
        using var db = Open();
        using var sql = db.CreateCommand();
        sql.CommandText = """
            INSERT INTO allowance_void_local_observations(approval_id,kind,snapshot_hash,actor_employee_no,observed_utc)
            SELECT $id,$kind,$hash,$actor,$now WHERE EXISTS
                (SELECT 1 FROM allowance_void_local_claims WHERE approval_id=$id)
            """;
        sql.Parameters.AddWithValue("$id", a.ApprovalId);
        sql.Parameters.AddWithValue("$kind", kind);
        sql.Parameters.AddWithValue("$hash", hash);
        sql.Parameters.AddWithValue("$actor", actor);
        sql.Parameters.AddWithValue("$now", Utc(time));
        if (sql.ExecuteNonQuery() != 1) throw new InvalidOperationException("作廢稽核找不到原送出紀錄");
    }

    public bool HasClaimForInvoice(string account, string invoice, string order)
    {
        using var db = Open();
        using var sql = db.CreateCommand();
        sql.CommandText = """
            SELECT EXISTS(SELECT 1 FROM allowance_void_local_claims c JOIN sync_issues i ON i.local_id=c.issue_id
                WHERE c.account_key=$account AND ((i.invoice_number=$invoice AND $invoice<>'') OR
                    (i.order_id=$order AND $order<>'')))
            """;
        sql.Parameters.AddWithValue("$account", account);
        sql.Parameters.AddWithValue("$invoice", invoice);
        sql.Parameters.AddWithValue("$order", order);
        return Convert.ToInt64(sql.ExecuteScalar()) == 1;
    }

    public HashSet<long> HistoryIssueIds(string account)
    {
        using var db=Open();
        using var sql=db.CreateCommand();
        sql.CommandText="SELECT issue_id FROM allowance_void_local_approvals WHERE account_key=$account";
        sql.Parameters.AddWithValue("$account",account);
        using var reader=sql.ExecuteReader();
        var ids=new HashSet<long>();
        while(reader.Read())ids.Add(reader.GetInt64(0));
        return ids;
    }

    private static void Bind(SqliteCommand sql, LocalAllowanceVoidApproval a)
    {
        sql.Parameters.AddWithValue("$id", a.ApprovalId);
        sql.Parameters.AddWithValue("$issue", a.IssueId);
        sql.Parameters.AddWithValue("$account", a.AccountKey);
        sql.Parameters.AddWithValue("$number", a.AllowanceNumber);
        sql.Parameters.AddWithValue("$request", a.RequestHash);
        sql.Parameters.AddWithValue("$official", a.OfficialHash);
        sql.Parameters.AddWithValue("$manager", a.ManagerEmployeeNo);
    }
    private static string Utc(DateTimeOffset time) => time.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    private SqliteConnection Open()
    {
        var db = new SqliteConnection(new SqliteConnectionStringBuilder {
            DataSource=path, Mode=SqliteOpenMode.ReadWrite, Pooling=false }.ToString());
        db.Open();
        using var sql = db.CreateCommand();
        sql.CommandText = "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000; PRAGMA synchronous=FULL;";
        sql.ExecuteNonQuery();
        return db;
    }
}
