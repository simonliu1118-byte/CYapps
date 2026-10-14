using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CYInvoice.Core.AutoInput;

namespace CYInvoice.WinForms;

// A pipe is private to this portable directory and the current Windows user.
// Only the primary process owns a LocalRepository/InvoiceService writer.
internal static class AutoInputPipe
{
    private const int MaxPacketBytes = 16 * 1024 * 1024;
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    internal sealed record Request(bool Issue, string RequestJson);
    internal sealed record Reply(int ExitCode, AutoInputResult? Result, string Error = "");

    internal static string DirectoryKey(string baseDirectory) =>
        Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(Path.GetFullPath(baseDirectory).ToUpperInvariant())));

    internal static string Name(string baseDirectory) => "CYInvoice-" + DirectoryKey(baseDirectory) + "-autoinput";

    internal static int ExitCode(AutoInputResult result) =>
        result.Orders.Count != 0 && result.Orders.All(order => order.Status is "issued" or "existing") ? 0 : 1;

    internal static async Task<Reply> ForwardAsync(bool issue, string requestJson, CancellationToken token)
    {
        using var pipe = new NamedPipeClientStream(".", Name(AppContext.BaseDirectory),
            PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(15_000, token).ConfigureAwait(false);
        await SendAsync(pipe, new Request(issue, requestJson), token).ConfigureAwait(false);
        return await ReceiveAsync<Reply>(pipe, token).ConfigureAwait(false);
    }

    internal static async Task SendAsync<T>(Stream pipe, T message, CancellationToken token)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message, Options);
        if (bytes.Length > MaxPacketBytes) throw new InvalidDataException("AutoInput IPC packet too large.");
        await pipe.WriteAsync(BitConverter.GetBytes(bytes.Length), token).ConfigureAwait(false);
        await pipe.WriteAsync(bytes, token).ConfigureAwait(false);
        await pipe.FlushAsync(token).ConfigureAwait(false);
    }

    internal static async Task<T> ReceiveAsync<T>(Stream pipe, CancellationToken token)
    {
        var lengthBytes = new byte[4];
        await pipe.ReadExactlyAsync(lengthBytes, token).ConfigureAwait(false);
        var length = BitConverter.ToInt32(lengthBytes, 0);
        if (length < 2 || length > MaxPacketBytes)
            throw new InvalidDataException("Invalid AutoInput IPC packet length.");
        var content = new byte[length];
        await pipe.ReadExactlyAsync(content, token).ConfigureAwait(false);
        return JsonSerializer.Deserialize<T>(content, Options)
            ?? throw new InvalidDataException("Invalid AutoInput IPC packet.");
    }
}

internal sealed class AutoInputPipeServer(AutoInputProcessor processor, Action onCompleted) : IDisposable
{
    private readonly CancellationTokenSource stop = new();
    private Task? worker;
    private int processing;
    internal bool IsProcessing => Volatile.Read(ref processing) != 0;
    internal void Start() => worker ??= Task.Run(ListenAsync);

    private async Task ListenAsync()
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(
                    AutoInputPipe.Name(AppContext.BaseDirectory),
                    PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(stop.Token).ConfigureAwait(false);
                Interlocked.Exchange(ref processing, 1);
                try
                {
                    AutoInputPipe.Reply reply;
                    try
                    {
                        var command = await AutoInputPipe.ReceiveAsync<AutoInputPipe.Request>(server, stop.Token)
                            .ConfigureAwait(false);
                        var request = AutoInputContract.ParseRequest(command.RequestJson);
                        var result = await processor.ExecuteAsync(request, command.Issue).ConfigureAwait(false);
                        reply = new(AutoInputPipe.ExitCode(result), result);
                        onCompleted();
                    }
                    catch (Exception error) when (error is InvalidDataException or JsonException)
                    {
                        reply = new(2, null, "請求格式或匯入檔案無效");
                    }
                    catch (Exception)
                    {
                        reply = new(3, null, "CYInvoice 目前無法安全處理此要求");
                    }
                    await AutoInputPipe.SendAsync(server, reply, stop.Token).ConfigureAwait(false);
                }
                finally { Interlocked.Exchange(ref processing, 0); }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
            catch (Exception) when (!stop.IsCancellationRequested)
            {
                // A disconnected client is not permission to retry an invoice issue.
            }
        }
    }

    public void Dispose()
    {
        stop.Cancel();
        // The main window refuses normal closing while an issue is running.
        // Never block the UI waiting for the listener task.
    }
}
