using CYInvoice.Core.Storage;

internal static class LocalResetCoordinatorTests
{
    public static async Task RunAsync()
    {
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
