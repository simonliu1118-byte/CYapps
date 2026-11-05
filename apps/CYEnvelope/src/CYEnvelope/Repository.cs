using System.Text.Json;
using System.IO;
using Microsoft.Data.Sqlite;

namespace CYEnvelope;

public sealed class Repository
{
    private readonly string _path;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };
    public Repository(string path)
    {
        _path = path;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS contacts(id TEXT PRIMARY KEY, name TEXT NOT NULL, document TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS contacts_name ON contacts(name);
            CREATE TABLE IF NOT EXISTS formats(id TEXT PRIMARY KEY, document TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS settings(id INTEGER PRIMARY KEY CHECK(id=1), document TEXT NOT NULL);
            """;
        command.ExecuteNonQuery();
        // user_version marks the schema for future migrations; 1 = document tables above.
        command.CommandText = "PRAGMA user_version";
        if (Convert.ToInt32(command.ExecuteScalar()) == 0)
        {
            command.CommandText = "PRAGMA user_version = 1";
            command.ExecuteNonQuery();
        }
        if (Formats().Count == 0) SaveFormat(new EnvelopeFormat { Id = "format-15k" });
        else UpgradePristineBuiltIn();
        DailyBackup();
    }

    public string DatabasePath => _path;

    // Integrity problem found before the app opened the database.
    public sealed class CorruptDatabaseException(string message) : Exception(message);

    // Runs before the constructor touches the file: a damaged database must never be silently overwritten.
    public static void EnsureReadable(string path)
    {
        if (!File.Exists(path)) return;
        try
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
                { DataSource = path, Mode = SqliteOpenMode.ReadWrite, Pooling = false }.ToString());
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA quick_check";
            if ((command.ExecuteScalar() as string) != "ok") throw new CorruptDatabaseException("資料庫完整性檢查未通過。");
        }
        catch (SqliteException error)
        {
            throw new CorruptDatabaseException("無法讀取資料庫：" + error.Message);
        }
    }

    public static string BackupFolder(string dbPath) => System.IO.Path.Combine(System.IO.Path.GetDirectoryName(dbPath)!, "Backups");

    public static string? LatestBackup(string dbPath) =>
        Directory.Exists(BackupFolder(dbPath))
            ? Directory.EnumerateFiles(BackupFolder(dbPath), "CYEnvelope-*.db").OrderDescending().FirstOrDefault()
            : null;

    // Keeps the damaged file next to the data (never deleted) and restores the newest backup in its place.
    public static bool RestoreLatestBackup(string dbPath)
    {
        var backup = LatestBackup(dbPath);
        if (backup is null) return false;
        if (File.Exists(dbPath))
            File.Move(dbPath, dbPath + $".damaged-{DateTime.Now:yyyyMMdd-HHmmss}", overwrite: false);
        File.Copy(backup, dbPath);
        return true;
    }

    // One consistent copy per day (only when there is customer data), newest KeepBackups kept.
    private const int KeepBackups = 14;
    private void DailyBackup()
    {
        if (Contacts().Count == 0) return;
        var folder = BackupFolder(_path);
        var target = System.IO.Path.Combine(folder, $"CYEnvelope-{DateTime.Now:yyyyMMdd}.db");
        if (File.Exists(target)) return;
        Directory.CreateDirectory(folder);
        using (var connection = Open())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "VACUUM INTO $target";
            command.Parameters.AddWithValue("$target", target);
            command.ExecuteNonQuery();
        }
        foreach (var old in Directory.EnumerateFiles(folder, "CYEnvelope-*.db").OrderDescending().Skip(KeepBackups))
            File.Delete(old);
    }

    // Built-in 15K layouts shipped earlier. A stored format that still equals one of them was never
    // edited by the user and is replaced by the current defaults; edited or custom formats are kept.
    private static IEnumerable<EnvelopeFormat> PreviousBuiltIns()
    {
        // Go-era layout (V0.1.x).
        var goEra = new EnvelopeFormat
        {
            Id = "format-15k",
            Recipient = new() { Rect = new(43, 49, 18, 142), FontSize = 24 },
            Address = new() { Rect = new(64, 46, 29, 148), FontSize = 14, Columns = 2 },
            Phone = new() { Rect = new(32, 59, 8, 132), FontSize = 10 },
            PostalCode = new() { Rect = new(52, 21, 40, 8), FontSize = 12, Vertical = false }
        };
        for (var i = 0; i < goEra.Delivery.Count; i++) { goEra.Delivery[i].X = 8.2; goEra.Delivery[i].Y = 63 + i * 4; }
        yield return goEra;
        // C# V0.2.1 Build 1-3: phone left of the recipient frame, address above the recipient.
        yield return new EnvelopeFormat
        {
            Id = "format-15k",
            Recipient = new() { Rect = new(39, 62, 26, 134), FontSize = 24 },
            Address = new() { Rect = new(72, 60, 27, 140), FontSize = 14, Columns = 2 },
            Phone = new() { Rect = new(28, 98, 8, 104), FontSize = 10 },
            PostalCode = new() { Rect = new(49, 31, 24, 9), FontSize = 12, Vertical = false }
        };
    }

    private void UpgradePristineBuiltIn()
    {
        var current = Formats().FirstOrDefault(f => f.Id == "format-15k");
        if (current is null) return;
        var stored = JsonSerializer.Serialize(current, Json);
        if (PreviousBuiltIns().Any(old => JsonSerializer.Serialize(old, Json) == stored))
            SaveFormat(new EnvelopeFormat { Id = current.Id });
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _path, Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString());
        connection.Open();
        return connection;
    }

    public List<Contact> Contacts()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT document FROM contacts ORDER BY name COLLATE NOCASE, id";
        using var rows = command.ExecuteReader();
        var result = new List<Contact>();
        while (rows.Read()) result.Add(JsonSerializer.Deserialize<Contact>(rows.GetString(0), Json)!);
        return result;
    }

    // Customer-name search: names containing the query, names starting with it first.
    public List<Contact> SearchContacts(string query, int limit = 12)
    {
        var q = Names.Normalize(query);
        var result = new List<Contact>();
        if (q.Length == 0) return result;
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT document FROM contacts WHERE instr(lower(name), lower($q)) > 0
            ORDER BY (instr(lower(name), lower($q)) = 1) DESC, name COLLATE NOCASE, id LIMIT $limit
            """;
        command.Parameters.AddWithValue("$q", q);
        command.Parameters.AddWithValue("$limit", limit);
        using var rows = command.ExecuteReader();
        while (rows.Read()) result.Add(JsonSerializer.Deserialize<Contact>(rows.GetString(0), Json)!);
        return result;
    }

    public Contact? GetContact(string id)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT document FROM contacts WHERE id=$id";
        command.Parameters.AddWithValue("$id", id);
        return command.ExecuteScalar() is string value ? JsonSerializer.Deserialize<Contact>(value, Json) : null;
    }

    // Customers whose normalised name equals the given one (there should be at most one).
    public List<Contact> FindByName(string name)
    {
        var n = Names.Normalize(name);
        return Contacts().Where(c => string.Equals(Names.Normalize(c.Name), n, StringComparison.CurrentCultureIgnoreCase)).ToList();
    }

    public void SaveContact(Contact contact)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO contacts(id,name,document) VALUES($id,$name,$document)
            ON CONFLICT(id) DO UPDATE SET name=excluded.name,document=excluded.document
            """;
        command.Parameters.AddWithValue("$id", contact.Id);
        command.Parameters.AddWithValue("$name", contact.Name);
        command.Parameters.AddWithValue("$document", JsonSerializer.Serialize(contact, Json));
        command.ExecuteNonQuery();
    }

    public void DeleteContact(string id)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM contacts WHERE id=$id";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    public List<EnvelopeFormat> Formats()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT document FROM formats ORDER BY id";
        using var rows = command.ExecuteReader();
        var result = new List<EnvelopeFormat>();
        while (rows.Read()) result.Add(JsonSerializer.Deserialize<EnvelopeFormat>(rows.GetString(0), Json)!);
        return result;
    }

    public void SaveFormat(EnvelopeFormat format)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO formats(id,document) VALUES($id,$document)
            ON CONFLICT(id) DO UPDATE SET document=excluded.document
            """;
        command.Parameters.AddWithValue("$id", format.Id);
        command.Parameters.AddWithValue("$document", JsonSerializer.Serialize(format, Json));
        command.ExecuteNonQuery();
    }

    // One transaction: never leaves two default formats, or none, after a failure.
    public void SetDefaultFormat(string id)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        var formats = new List<EnvelopeFormat>();
        using (var read = connection.CreateCommand())
        {
            read.Transaction = transaction;
            read.CommandText = "SELECT document FROM formats";
            using var rows = read.ExecuteReader();
            while (rows.Read()) formats.Add(JsonSerializer.Deserialize<EnvelopeFormat>(rows.GetString(0), Json)!);
        }
        foreach (var format in formats)
        {
            format.IsDefault = format.Id == id;
            using var write = connection.CreateCommand();
            write.Transaction = transaction;
            write.CommandText = "UPDATE formats SET document=$document WHERE id=$id";
            write.Parameters.AddWithValue("$id", format.Id);
            write.Parameters.AddWithValue("$document", JsonSerializer.Serialize(format, Json));
            write.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    public void DeleteFormat(string id)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM formats WHERE id=$id";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    public AppSettings Settings()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT document FROM settings WHERE id=1";
        return command.ExecuteScalar() is string value
            ? JsonSerializer.Deserialize<AppSettings>(value, Json)!
            : new AppSettings();
    }

    public void SaveSettings(AppSettings settings)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO settings(id,document) VALUES(1,$document)
            ON CONFLICT(id) DO UPDATE SET document=excluded.document
            """;
        command.Parameters.AddWithValue("$document", JsonSerializer.Serialize(settings, Json));
        command.ExecuteNonQuery();
    }
}
