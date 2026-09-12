namespace CYInvoice.Core.Storage;

public sealed record StatusUpdate(
    string InvoiceNumber,
    string InvoiceState,
    int UploadStatus,
    string UploadStatusText,
    string ErrorMessage,
    string InvoiceDate,
    string InvoiceTime,
    string LastChecked);

public sealed class InvoiceStore(string dataDirectory)
{
    private readonly Lock gate = new();
    private readonly string path = Path.Combine(dataDirectory, "invoices.json");

    public IReadOnlyList<InvoiceRecord> LoadOrCreate()
    {
        lock (gate)
        {
            var found = JsonFile.TryRead<List<InvoiceRecord>>(path, out var records);
            records ??= [];
            var changed = false;
            foreach (var record in records)
            {
                if (string.IsNullOrWhiteSpace(record.SentAt))
                {
                    record.SentAt = LegacySentAt(record);
                    changed |= record.SentAt.Length != 0;
                }
            }
            Validate(records);
            if (!found || changed) JsonFile.Write(path, records);
            return Clone(records);
        }
    }

    public void Save(IEnumerable<InvoiceRecord> records)
    {
        lock (gate)
        {
            var materialized = records.ToList();
            Validate(materialized);
            JsonFile.Write(path, materialized);
        }
    }

    public void Append(InvoiceRecord record)
    {
        lock (gate)
        {
            JsonFile.TryRead<List<InvoiceRecord>>(path, out var records);
            records ??= [];
            ValidateRecord(record);
            if (record.Id.Length != 0 && records.Any(existing => existing.Id == record.Id))
                throw new InvalidOperationException($"invoice record ID {record.Id} already exists");
            records.Add(record);
            JsonFile.Write(path, records);
        }
    }

    public void UpdateStatus(string id, StatusUpdate update)
    {
        lock (gate)
        {
            JsonFile.TryRead<List<InvoiceRecord>>(path, out var records);
            records ??= [];
            var record = records.FirstOrDefault(candidate => candidate.Id == id)
                ?? throw new KeyNotFoundException($"invoice record {id} not found");
            record.InvoiceNumber = update.InvoiceNumber;
            record.InvoiceState = update.InvoiceState;
            record.UploadStatus = update.UploadStatus;
            record.UploadStatusText = update.UploadStatusText;
            record.ErrorMessage = update.ErrorMessage;
            record.InvoiceDate = update.InvoiceDate;
            record.InvoiceTime = update.InvoiceTime;
            record.LastChecked = update.LastChecked;
            JsonFile.Write(path, records);
        }
    }

    private static string LegacySentAt(InvoiceRecord record)
    {
        var date = record.InvoiceDate.Trim();
        var time = record.InvoiceTime.Trim();
        if (date.Length != 0 && time.Length != 0) return date + " " + time;
        if (date.Length != 0) return date;
        return record.LastChecked.Trim();
    }

    private static void Validate(IReadOnlyList<InvoiceRecord> records)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < records.Count; index++)
        {
            ValidateRecord(records[index]);
            if (records[index].Id.Length != 0 && !ids.Add(records[index].Id))
                throw new InvalidDataException($"invoice record ID {records[index].Id} is duplicated");
        }
    }

    private static void ValidateRecord(InvoiceRecord record)
    {
        if (string.IsNullOrWhiteSpace(record.OrderId)) throw new InvalidDataException("order ID is required");
        if (record.Amount < 0) throw new InvalidDataException("invoice amount cannot be negative");
        if (string.IsNullOrWhiteSpace(record.InvoiceState)) throw new InvalidDataException("invoice state is required");
    }

    private static IReadOnlyList<InvoiceRecord> Clone(List<InvoiceRecord> records)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(records);
        return System.Text.Json.JsonSerializer.Deserialize<List<InvoiceRecord>>(json) ?? [];
    }
}
