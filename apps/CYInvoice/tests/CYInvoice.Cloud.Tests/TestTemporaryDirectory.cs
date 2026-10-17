// Shared isolated test directory for Local and CYID Cloud tests.
// Has no dependency on retired Built-in Employee fixtures.
internal sealed class TestTemporaryDirectory : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(), "CYInvoice.V3.Tests", Guid.NewGuid().ToString("N"));

    public TestTemporaryDirectory() => Directory.CreateDirectory(Path);

    public void Dispose()
    {
        if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
    }
}
