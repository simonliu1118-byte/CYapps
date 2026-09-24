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
        WorkerKeepsCompatibilityAndStorageVersionsSeparate();
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

    private static void WorkerKeepsCompatibilityAndStorageVersionsSeparate()
    {
        var worker = File.ReadAllText(FindWorkerSource());
        Contains(worker, "const LEGACY_SCHEMA_COMPATIBILITY_VERSION = \"8\";",
            "Worker must retain the Build 4 API 1 compatibility marker");
        Contains(worker, "body.storageSchemaVersion = actualSchemaVersion;",
            "Worker must expose physical D1 migration progress separately");
        Contains(worker, "body.schemaVersion = LEGACY_SCHEMA_COMPATIBILITY_VERSION;",
            "Worker health must not expose D1 migration progress as a client hard gate");
        Contains(worker, "CLIENT_UPDATE_REQUIRED",
            "retired security-sensitive onboarding shapes must fail with an explicit client update requirement");
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

    private static string FindWorkerSource()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(start);
            while (directory is not null)
            {
                var direct = Path.Combine(directory.FullName, "cloud", "src", "worker.ts");
                if (File.Exists(direct)) return direct;

                var repository = Path.Combine(directory.FullName, "apps", "CYInvoice", "cloud", "src", "worker.ts");
                if (File.Exists(repository)) return repository;

                directory = directory.Parent;
            }
        }
        throw new InvalidOperationException("Could not locate apps/CYInvoice/cloud/src/worker.ts for compatibility regression checks.");
    }

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
