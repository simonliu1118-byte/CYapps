namespace CYInvoice.Core;

// Presentation data only. Financial values remain exact text from their original owner.
public sealed record BusinessDetailTable(string Title, IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyList<string>> Rows);
