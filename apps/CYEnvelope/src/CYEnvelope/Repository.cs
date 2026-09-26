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
