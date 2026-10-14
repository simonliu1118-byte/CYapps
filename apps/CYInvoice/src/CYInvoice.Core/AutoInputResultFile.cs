using System.Text;
using System.Text.Json;

namespace CYInvoice.Core.AutoInput;

public static class AutoInputResultFile
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static async Task WriteAtomicAsync(string destination, AutoInputResult result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (!Path.IsPathFullyQualified(destination))
            throw new InvalidDataException("AutoInput result path must be absolute.");
        if (result.Schema != AutoInputContract.ResultSchema || !Guid.TryParseExact(result.RequestId, "D", out _))
            throw new InvalidDataException("Invalid AutoInput result envelope.");
        if (result.Environment is not ("production" or "test" or "unknown"))
            throw new InvalidDataException("Invalid AutoInput environment.");
        if (result.Orders is null || result.Orders.Count > AutoInputContract.MaxOrderCount ||
            result.Orders.Any(item => item.Status is not ("issued" or "existing" or "failed" or "unknown" or "skipped")))
            throw new InvalidDataException("Invalid AutoInput order status.");
        var parent = Path.GetDirectoryName(destination);
        if (string.IsNullOrWhiteSpace(parent) || !Directory.Exists(parent))
            throw new DirectoryNotFoundException("AutoInput result directory not found.");
        // Never overwrite a previously committed result: caller must explicitly reconcile a lost response.
        if (File.Exists(destination))
            throw new IOException("AutoInput result already exists.");
        var temp = Path.Combine(parent, "." + Path.GetFileName(destination) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(result, JsonOptions));
            await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                             4096, FileOptions.WriteThrough | FileOptions.Asynchronous))
            {
                await stream.WriteAsync(bytes, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            File.Move(temp, destination, overwrite: false);
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
