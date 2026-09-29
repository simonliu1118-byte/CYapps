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
        if (Formats().Count == 0) SaveFormat(new EnvelopeFormat { Id = "format-15k" });
        else UpgradePristineBuiltIn();
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
