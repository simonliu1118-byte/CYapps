using System.Net;
using CYInvoice.Core.Storage;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CYInvoice.Core.Cloud;

// Status and edits never disclose secrets. Event-driven Device synchronization
// receives changed credentials over HTTPS and immediately seals them with DPAPI.
public sealed record CloudCompanySettingsStatus(
    string SellerInvoice, int Revision, bool HasAmegoAppKey, bool HasMoExcelPassword, string CompanyName = "");

public sealed record CloudCompanySecretChange(string Action, string? Value = null)
{
    public static CloudCompanySecretChange Keep() => new("keep");
    public static CloudCompanySecretChange Clear() => new("clear");
    public static CloudCompanySecretChange Replace(string value) => new("replace", value);
}

public sealed record CloudCompanySettingsEdit(
    int ExpectedRevision, string SellerInvoice,
    CloudCompanySecretChange AmegoAppKey, CloudCompanySecretChange MoExcelPassword, string CompanyName = "");

// Decrypted credentials are held in memory only until they are sealed by
// SettingsStore into the current Windows user's existing DPAPI settings.
public sealed record CloudCompanyProvision(
    string SellerInvoice, int Revision, string AmegoAppKey, string MoExcelPassword, string CompanyName = "")
{
    public bool Matches(CloudCompanySettingsStatus card) =>
        Revision == card.Revision && SellerInvoice == card.SellerInvoice && CompanyName == card.CompanyName &&
        card.HasAmegoAppKey && card.HasMoExcelPassword &&
        !string.IsNullOrWhiteSpace(AmegoAppKey) && AmegoAppKey.Length <= 512 &&
        !AmegoAppKey.Any(char.IsControl) &&
        // Local SettingsStore trims App Keys. Never stamp a cloud revision if
        // the protected Windows value would differ from the cloud secret.
        AmegoAppKey == AmegoAppKey.Trim() &&
        !string.IsNullOrWhiteSpace(MoExcelPassword) && MoExcelPassword.Length <= 256 &&
        !MoExcelPassword.Any(char.IsControl);
}

public sealed class CloudCompanySettingsClient
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(8);
    private readonly HttpClient http;
    private readonly Uri endpoint;
    private readonly string deviceToken;

    public CloudCompanySettingsClient(HttpClient http, Uri endpoint, string deviceToken)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(endpoint);
        if (!endpoint.IsAbsoluteUri || endpoint.Scheme != Uri.UriSchemeHttps ||
            endpoint.UserInfo.Length != 0 || endpoint.Query.Length != 0 ||
            endpoint.Fragment.Length != 0)
            throw new ArgumentException("Company settings need a trusted HTTPS URL.", nameof(endpoint));
        if (deviceToken is null ||
            !Regex.IsMatch(deviceToken, @"^cydev_[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant))
            throw new ArgumentException("Invalid company settings Device token.", nameof(deviceToken));
        this.http = http;
        this.endpoint = new Uri(endpoint.AbsoluteUri.TrimEnd('/') + "/", UriKind.Absolute);
        this.deviceToken = deviceToken.ToLowerInvariant();
    }

    public async Task<CloudCompanySettingsStatus?> GetAsync(
        string employeeNo, string password, CancellationToken cancellationToken = default)
    {
        Credentials(employeeNo, password);
        using var response = await SendAsync("get", new { employeeNo, password }, cancellationToken)
            .ConfigureAwait(false);
        var root = response.RootElement;
        if (!root.TryGetProperty("company", out var company))
            throw new InvalidDataException("Company settings status is missing.");
        return company.ValueKind == JsonValueKind.Null ? null : Parse(company);
    }

    public async Task<CloudCompanySettingsStatus> UpdateAsync(
        CloudCompanySettingsEdit edit, string employeeNo, string password,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(edit);
        Credentials(employeeNo, password);
        if (edit.ExpectedRevision < 0 || !Invoice(edit.SellerInvoice) ||
            !Change(edit.AmegoAppKey, 512) || !Change(edit.MoExcelPassword, 256))
            throw new ArgumentException("Company settings edit is invalid.", nameof(edit));
        // Explicit "keep", "replace", or "clear"; an empty UI field must not
        // accidentally clear an existing encrypted App Key / MO password.
        using var response = await SendAsync("update", new {
            expectedRevision = edit.ExpectedRevision,
            sellerInvoice = edit.SellerInvoice,
            companyName = edit.CompanyName,
            amegoAppKey = ToWire(edit.AmegoAppKey),
            moExcelPassword = ToWire(edit.MoExcelPassword),
            employeeNo, password
        }, cancellationToken).ConfigureAwait(false);
        var root = response.RootElement;
        if (!root.TryGetProperty("company", out var company))
            throw new InvalidDataException("Company settings update is missing confirmation.");
        var card = Parse(company);
        if (card.Revision != edit.ExpectedRevision + 1 ||
            card.SellerInvoice != edit.SellerInvoice)
            throw new InvalidDataException("Company settings update reply disagrees with request.");
        return card;
    }

    // Event-driven only. A revision check never downloads unchanged secrets.
    public async Task<CloudCompanySettingsStatus?> SynchronizeAsync(
        SettingsStore store, CancellationToken cancellationToken = default)
    {
        var original = store.LoadOrCreate();
        if (original.CloudMode != CloudModes.CloudPreferred ||
            original.CloudIdentityProvider != "CYID" || !original.CloudEmployeeAuthorityReady)
            return null;
        if (store.CloudDeviceToken(original) != deviceToken ||
            new Uri(original.CloudBaseUrl.TrimEnd('/') + "/") != endpoint)
            throw new InvalidOperationException("公司卡裝置綁定已變更，請重新連線。");
        var knownRevision = original.CloudCompanyAppliedWorkspaceId == original.CloudWorkspaceId &&
            original.CloudCompanyAppliedDeviceId == original.CloudDeviceId &&
            original.CloudCompanyAppliedSellerInvoice == original.ProductionInvoice &&
            original.ProductionAppKeyEncrypted.Length != 0 && original.MoPasswordEncrypted.Length != 0
                ? original.CloudCompanyAppliedRevision : 0;
        using var response = await SendAsync("sync", new { knownRevision }, cancellationToken).ConfigureAwait(false);
        var root = response.RootElement;
        if (!root.TryGetProperty("company", out var value)) throw new InvalidDataException("公司卡回應缺少狀態。");
        if (value.ValueKind == JsonValueKind.Null)
        {
            if (knownRevision > 0) throw new InvalidDataException("雲端公司卡遺失，保留本機憑證並停止套用。");
            return null;
        }
        var card = Parse(value);
        if (card.Revision < knownRevision) throw new InvalidDataException("公司卡版次倒退，未覆寫本機設定。");
        if (!card.HasAmegoAppKey || !card.HasMoExcelPassword)
            return card; // Editor can repair an incomplete cloud card; local credentials stay intact.
        if (root.TryGetProperty("credentials", out var credentials))
        {
            var received = new CloudCompanyProvision(
                credentials.GetProperty("sellerInvoice").GetString()!,
                credentials.GetProperty("revision").GetInt32(),
                credentials.GetProperty("amegoAppKey").GetString()!,
                credentials.GetProperty("moExcelPassword").GetString()!,
                credentials.GetProperty("companyName").GetString()!);
            if (!received.Matches(card)) throw new InvalidDataException("公司卡內容與版次不一致。");
            store.ApplyCloudCompanyProvision(original, received);
        }
        else if (!CloudCompanySettingsUsage.Inspect(card, original).CompanyCardAppliedToCurrentDevice ||
                 original.ProductionCompanyName != card.CompanyName)
            throw new InvalidDataException("公司卡缺少必要憑證，未套用不完整的回應。");
        return card;
    }

    private async Task<JsonDocument> SendAsync(
        string action, object body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post,
            new Uri(endpoint, "v1/company-settings/" + action)) { Content = JsonContent.Create(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", deviceToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("X-Request-ID", "win-" + Guid.NewGuid().ToString("N"));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        // A D1 commit can survive a lost HTTP reply; NEVER retry an update.
        using var result = await http.SendAsync(request,
            HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        await using var stream = await result.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        JsonDocument document;
        try { document = await JsonDocument.ParseAsync(stream, cancellationToken: timeout.Token)
            .ConfigureAwait(false); }
        catch (JsonException ex) { throw new InvalidDataException(
            "Invalid Cloud company settings JSON reply.", ex); }
        var root = document.RootElement;
        if (result.IsSuccessStatusCode && root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True)
            return document;
        var errorCode = root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object &&
            error.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.String
            ? code.GetString() ?? "COMPANY_SETTINGS_UNAVAILABLE" : "COMPANY_SETTINGS_UNAVAILABLE";
        document.Dispose();
        throw new CloudApiException(errorCode,
            "Company settings result is not confirmed; get status before retry.", result.StatusCode);
    }

    private static CloudCompanySettingsStatus Parse(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty("sellerInvoice", out var seller) ||
            seller.ValueKind != JsonValueKind.String ||
            !Invoice(seller.GetString()) ||
            !element.TryGetProperty("revision", out var revision) ||
            revision.ValueKind != JsonValueKind.Number || !revision.TryGetInt32(out var rev) || rev < 1 ||
            !element.TryGetProperty("hasAmegoAppKey", out var key) ||
            key.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
            !element.TryGetProperty("hasMoExcelPassword", out var mo) ||
            mo.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new InvalidDataException("Company settings status has invalid fields.");
        var name = element.TryGetProperty("companyName", out var label) && label.ValueKind == JsonValueKind.String
            ? label.GetString()! : "";
        if (name.Length > 100 || name.Any(char.IsControl)) throw new InvalidDataException("Invalid company name.");
        return new CloudCompanySettingsStatus(seller.GetString()!, rev, key.GetBoolean(), mo.GetBoolean(), name);
    }

    private static bool Invoice(string? value) =>
        value is { Length: 8 } && value.All(c => c is >= '0' and <= '9');

    private static bool Change(CloudCompanySecretChange? value, int limit) =>
        value is not null && (
            (value.Action is "keep" or "clear" && value.Value is null) ||
            (value.Action == "replace" && value.Value is { Length: > 0 } secret &&
                secret.Length <= limit && !secret.Any(char.IsControl))
        );

    private static Dictionary<string, string> ToWire(CloudCompanySecretChange change) =>
        change.Action == "replace" ? new() { ["action"] = "replace", ["value"] = change.Value! } :
        new() { ["action"] = change.Action };

    private static void Credentials(string employeeNo, string password)
    {
        if (employeeNo is null ||
            !Regex.IsMatch(employeeNo, @"^\d{4}$", RegexOptions.CultureInvariant) ||
            password is null || password.EnumerateRunes().Count() is < 8 or > 16)
            throw new ArgumentException("CYID credentials are invalid.");
    }
}
