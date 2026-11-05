namespace CYEnvelope;

public static class Names
{
    // One comparison/storage form for customer names: trimmed, inner whitespace (incl. full-width) collapsed.
    public static string Normalize(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}
