using System.Runtime.CompilerServices;
using CYInvoice.Core.Cloud;

internal static class CompatibilityRegression
{
    [ModuleInitializer]
    internal static void Run()
    {
        NewClientAcceptsOlderApi1StorageSchemas();
        NewClientRejectsApiContractMismatch();
        Build4AcceptsNewWorkerCompatibilityHealth();
        SensitiveCyIdWindowsOperationsRefuseHttpRedirects();
        Console.WriteLine("PASS cross-version Cloud compatibility regression");
    }

    private static void NewClientAcceptsOlderApi1StorageSchemas()
    {
        foreach (var schema in new[] { "7", "8", "9" })
        {
            var health = Healthy(schema, CloudCompatibility.ApiVersion);
            Equal(string.Empty, CloudCompatibility.Problem(health),
                $"API 1 must remain usable when only the server storage migration marker changes (schema {schema})");
        }
    }

    private static void NewClientRejectsApiContractMismatch()
    {
        var health = Healthy("9", "2");
        True(CloudCompatibility.Problem(health).Contains("API 版本不相容", StringComparison.Ordinal),
            "a real API contract change must still be rejected");
    }

    private static void Build4AcceptsNewWorkerCompatibilityHealth()
    {
        // Historical V2.6.5 Build 4 gate: service, storage, API 1 and exact schema marker 8.
        // The new Worker keeps that marker stable while reporting the physical D1 migration separately.
        var newWorkerHealth = Healthy("8", "1");
        True(LegacyBuild4Accepts(newWorkerHealth),
            "new Worker health must remain acceptable to released V2.6.5 Build 4 clients");
    }

    private static void SensitiveCyIdWindowsOperationsRefuseHttpRedirects()
    {
        // These three flows send CYID passwords; company-card provision also
        // installs business secrets, and recovery claim includes a Device Token.
        // HttpClient's default automatic 307/308 redirect would replay the POST
        // body to Location, even when that host is not the configured Cloud.
        foreach (var (source, guard) in new[] {
            ("SettingsForm.cs", "new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })"),
            ("CloudRecoveryForm.cs", "new(new HttpClientHandler { AllowAutoRedirect = false })"),
            ("CloudDirectJoinForm.cs", "new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })")
        })
            Contains(File.ReadAllText(FindWinFormsSource(source)), guard,
                $"V3 CYID credential-bearing {source} must disable automatic HTTP redirects");
    }

    private static string FindWinFormsSource(string fileName)
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(start);
            while (directory is not null)
            {
                foreach (var relative in new[] {
                    Path.Combine("src", "CYInvoice.WinForms", fileName),
                    Path.Combine("apps", "CYInvoice", "src", "CYInvoice.WinForms", fileName)
                })
                {
                    var candidate = Path.Combine(directory.FullName, relative);
                    if (File.Exists(candidate)) return candidate;
                }
                directory = directory.Parent;
            }
        }
        throw new InvalidOperationException($"Could not locate CYInvoice WinForms source {fileName}.");
    }

    private static CloudHealthResult Healthy(string schemaVersion, string apiVersion) => new(
        true,
        true,
        CloudCompatibility.ServiceName,
        "0.8.5",
        apiVersion,
        schemaVersion,
        "test",
        12,
        string.Empty);

    private static bool LegacyBuild4Accepts(CloudHealthResult health) =>
        health.Reachable
        && health.StorageAvailable
        && string.Equals(health.ServiceName, "cyinvoice-cloud", StringComparison.Ordinal)
        && string.Equals(health.ApiVersion, "1", StringComparison.Ordinal)
        && string.Equals(health.SchemaVersion, "8", StringComparison.Ordinal);

    private static void Contains(string value, string expected, string message)
    {
        if (!value.Contains(expected, StringComparison.Ordinal))
            throw new InvalidOperationException(message);
    }

    private static void Equal(string expected, string actual, string message)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
            throw new InvalidOperationException($"{message}: expected '{expected}', actual '{actual}'");
    }

    private static void True(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
