using System.Runtime.InteropServices;
using System.Text.Json;

namespace CYInvoice.Core.Storage;

internal static class JsonFile
{
    private const long MaximumBytes = 64L * 1024 * 1024;
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = false,
    };

    public static bool TryRead<T>(string path, out T? value)
    {
        value = default;
        if (!File.Exists(path)) return false;
        var information = new FileInfo(path);
        if (information.Length > MaximumBytes) throw new InvalidDataException($"{Path.GetFileName(path)} exceeds 64 MiB");
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.SequentialScan);
        try
        {
            value = JsonSerializer.Deserialize<T>(stream, Options)
                ?? throw new InvalidDataException($"decode {Path.GetFileName(path)}: JSON root is null");
            return true;
        }
        catch (JsonException error)
        {
            throw new InvalidDataException($"decode {Path.GetFileName(path)}", error);
        }
    }

    public static void Write<T>(string path, T value)
    {
        var directory = Path.GetDirectoryName(path) ?? throw new InvalidOperationException("data path has no directory");
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".{Path.GetFileName(path)}-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(stream, value, Options);
                stream.WriteByte((byte)'\n');
                stream.Flush(flushToDisk: true);
            }
            AtomicReplace(temporary, path);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static void AtomicReplace(string source, string destination)
    {
        if (OperatingSystem.IsWindows())
        {
            if (!MoveFileEx(source, destination, MoveFileFlags.ReplaceExisting | MoveFileFlags.WriteThrough))
                throw new IOException("MoveFileExW failed", Marshal.GetExceptionForHR(Marshal.GetHRForLastWin32Error()));
            return;
        }
        File.Move(source, destination, overwrite: true);
    }

    [Flags]
    private enum MoveFileFlags : uint { ReplaceExisting = 0x1, WriteThrough = 0x8 }

#pragma warning disable SYSLIB1054 // The same small Win32 boundary is kept for exact Go V1.1.0 replace semantics.
    [DllImport("kernel32.dll", EntryPoint = "MoveFileExW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MoveFileEx(string source, string destination, MoveFileFlags flags);
#pragma warning restore SYSLIB1054
}
