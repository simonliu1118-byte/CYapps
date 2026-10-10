using CYInvoice.Core.Storage;
using CYInvoice.Core.Cloud;
using System.Text.Json;

internal static class LocalResetCoordinatorTests
{
    public static async Task RunAsync()
    {
        await TestRevokedRecoveryAsync();
        var directory = Path.Combine(Path.GetTempPath(), "CYInvoice.LocalReset.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var data = Path.Combine(directory, "Data");
            var cache = Path.Combine(directory, "Cache");
            var logs = Path.Combine(directory, "Logs");
            Directory.CreateDirectory(data);
            Directory.CreateDirectory(cache);
            Directory.CreateDirectory(logs);
            File.WriteAllText(Path.Combine(data, "sentinel.db"), "data");
            File.WriteAllText(Path.Combine(cache, "sentinel.pdf"), "cache");
            File.WriteAllText(Path.Combine(logs, "keep.log"), "keep");

            await LocalResetCoordinator.ExecuteAsync(
                directory,
                new ResetTestProtector(),
                new LocalResetExecutionRequest(LocalResetKind.Local));

            True(!Directory.Exists(data), "Local reset must remove Data recursively");
            True(!Directory.Exists(cache), "Local reset must remove Cache recursively");
            True(Directory.Exists(logs), "Local reset must not delete unrelated diagnostic folders");
            True(File.Exists(Path.Combine(logs, "keep.log")), "Local reset must preserve unrelated files");
            True(!LocalResetCoordinator.HasPendingReset(directory), "successful Local reset must clear its transaction marker");

            var recovery = await LocalResetCoordinator.RecoverPendingAsync(directory, new ResetTestProtector());
            Equal(LocalResetRecoveryDisposition.None, recovery.Disposition,
                "completed Local reset must not leave recovery work behind");
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task TestRevokedRecoveryAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "CYInvoice.RevokedReset.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var settings = new Settings { CloudMode = CloudModes.CloudPreferred, CloudWorkspaceId = "workspace",
                CloudDeviceId = "device", CloudBaseUrl = "https://synthetic.invalid/" };
            var revoked = new CloudDeviceSelfStatus("device", "workspace", "revoked", "active", DateTimeOffset.UtcNow);
            File.WriteAllText(Path.Combine(directory, "CYInvoice.exe"), "original");
            Directory.CreateDirectory(Path.Combine(directory, "Runtime", "WebView2"));
            File.WriteAllText(Path.Combine(directory, "Runtime", "WebView2", "library.dll"), "original");
            File.WriteAllText(Path.Combine(directory, LocalResetCoordinator.PackageManifestFileName),
                JsonSerializer.Serialize(new[] { "CYInvoice.exe", "Runtime/WebView2/library.dll" }));
            foreach (var name in new[] { "Data", "Cache", "Logs", "ExtraRuntime" })
            {
                Directory.CreateDirectory(Path.Combine(directory, name));
                File.WriteAllText(Path.Combine(directory, name, "private.txt"), "runtime");
            }
            File.WriteAllText(Path.Combine(directory, "runtime-root.json"), "runtime");
            try
            {
                LocalResetCoordinator.PrepareRevokedDeviceReset(directory, settings, revoked with { DeviceId = "other" });
                throw new Exception("Mismatched device must not authorize wipe");
            }
            catch (InvalidDataException) { }
            True(!LocalResetCoordinator.HasPendingReset(directory), "mismatched status must not write marker");
            LocalResetCoordinator.PrepareRevokedDeviceReset(directory, settings, revoked);
            True(LocalResetCoordinator.IsRevokedDeviceResetPending(directory), "authorization must be durable before shutdown");
            // Simulate a process dying after only one runtime directory was removed.
            Directory.Delete(Path.Combine(directory, "Data"), true);
            var recovery = await LocalResetCoordinator.RecoverPendingAsync(directory, new ResetTestProtector());
            Equal(LocalResetRecoveryDisposition.Completed, recovery.Disposition, "partial wipe must recover without cloud or settings");
            True(!Directory.Exists(Path.Combine(directory, "Logs")), "automatic revoke must wipe Logs");
            True(!Directory.Exists(Path.Combine(directory, "Cache")), "automatic revoke must wipe cache");
            True(!Directory.Exists(Path.Combine(directory, "ExtraRuntime")), "non-package runtime directories must be wiped");
            True(!File.Exists(Path.Combine(directory, "runtime-root.json")), "root runtime files must be wiped");
            True(File.Exists(Path.Combine(directory, "CYInvoice.exe")), "original program must survive");
            True(File.Exists(Path.Combine(directory, "Runtime", "WebView2", "library.dll")), "original runtime binaries must survive");
            True(!LocalResetCoordinator.HasPendingReset(directory), "marker removed after complete wipe");

            LocalResetCoordinator.PrepareRevokedDeviceReset(directory, settings, revoked);
            File.WriteAllText(Path.Combine(directory, LocalResetCoordinator.PackageManifestFileName), "[\"../outside\"]");
            try { await LocalResetCoordinator.RecoverPendingAsync(directory, new ResetTestProtector()); throw new Exception("Invalid manifest must block"); }
            catch (InvalidDataException) { }
            True(LocalResetCoordinator.HasPendingReset(directory), "invalid manifest must retain recovery marker");
            var marker = Directory.GetFiles(directory, ".cyinvoice-local-reset.pending.json").Single();
            File.WriteAllText(marker, "{malformed");
            try { await LocalResetCoordinator.RecoverPendingAsync(directory, new ResetTestProtector()); throw new Exception("Unreadable marker must block startup"); }
            catch (InvalidDataException) { }
            True(LocalResetCoordinator.HasPendingReset(directory), "unreadable marker cannot be mistaken for completed recovery");
        }
        finally { Directory.Delete(directory, true); }
    }

    private static void Equal<T>(T expected, T actual, string description)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{description}: expected '{expected}', got '{actual}'");
    }

    private static void True(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
    }

    private sealed class ResetTestProtector : ISecretProtector
    {
        public string Protect(ReadOnlySpan<byte> plaintext)
        {
            var bytes = plaintext.ToArray();
            Array.Reverse(bytes);
            return Convert.ToBase64String(bytes);
        }

        public byte[] Unprotect(string ciphertext)
        {
            var bytes = Convert.FromBase64String(ciphertext);
            Array.Reverse(bytes);
            return bytes;
        }
    }
}
