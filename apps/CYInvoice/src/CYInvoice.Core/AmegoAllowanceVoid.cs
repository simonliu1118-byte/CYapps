using System.Text.Json;

namespace CYInvoice.Core.Amego;

public interface IAmegoAllowanceVoidGateway
{
    Task<AllowanceQueryResponse> QueryAllowanceAsync(string number, CancellationToken cancellationToken = default);
    Task<AllowanceIssueResponse> VoidAllowanceOnceAsync(string number, CancellationToken cancellationToken = default);
    Task<AllowanceVoidStatus> ReadAllowanceVoidStatusAsync(string number, CancellationToken cancellationToken = default);
}

public sealed record AllowanceVoidStatus(string AllowanceNumber, string Type, int Status)
{
    // Official state only; this does not attribute a particular HTTP POST.
    public bool OfficiallyVoided => Type == "D0501" && Status == UploadStatuses.Complete;
}

public sealed partial class AmegoClient
{
    // Official contract: https://invoice.amego.tw/api_doc/ (g0501 / allowance_status).
    // Historical allowance IDs are not necessarily CYInvoice-generated IDs.
    internal static void ValidateOfficialAllowanceNumber(string number)
    {
        if (string.IsNullOrWhiteSpace(number) || number.Length > 16 ||
            number.Trim() != number || number.Any(char.IsControl))
            throw new ArgumentException("折讓單號格式無效", nameof(number));
    }

    public async Task<AllowanceIssueResponse> VoidAllowanceOnceAsync(
        string number, CancellationToken cancellationToken = default)
    {
        ValidateOfficialAllowanceNumber(number);
        if (Invoice.Length == 0 || AppKey.Length == 0)
            throw new InvalidOperationException("invoice and App Key are required");
        var data = JsonSerializer.Serialize(new[] { new { CancelAllowanceNumber = number } });
        // Deliberately bypass PostAsync: even code 15 must not retry g0501.
        var body = await PostSignedAsync("/json/g0501", data, cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(body);
        var (code, message) = ReadEnvelope(document.RootElement);
        ThrowResponseError(code, message);
        return new AllowanceIssueResponse(code, message);
    }

    public async Task<AllowanceVoidStatus> ReadAllowanceVoidStatusAsync(
        string number, CancellationToken cancellationToken = default)
    {
        ValidateOfficialAllowanceNumber(number);
        using var document = await PostAsync("/json/allowance_status",
            new[] { new { AllowanceNumber = number } }, cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;
        var (code, message) = ReadEnvelope(root);
        ThrowResponseError(code, message);
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object ||
            !data.TryGetProperty(number, out var row) || row.ValueKind != JsonValueKind.Object ||
            !row.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String ||
            !row.TryGetProperty("status", out var status))
            throw new InvalidDataException("光貿未提供原折讓單號的狀態");
        var value = status.ValueKind == JsonValueKind.Number && status.TryGetInt32(out var numeric)
            ? numeric : status.ValueKind == JsonValueKind.String &&
                int.TryParse(status.GetString(), System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out numeric) ? numeric : -1;
        if (value < 0) throw new InvalidDataException("光貿折讓狀態格式無效");
        return new AllowanceVoidStatus(number, type.GetString()!, value);
    }
}
