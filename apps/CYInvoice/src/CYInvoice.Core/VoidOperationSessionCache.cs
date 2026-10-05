using System.Collections.Concurrent;

namespace CYInvoice.Core.Invoicing;

public sealed record ParsedVoidReason(
    string UserEmployeeNo,
    string ReviewerEmployeeNo,
    string Reason)
{
    public bool Reviewed => ReviewerEmployeeNo.Length != 0;
}

public static class VoidOperationSessionCache
{
    private static readonly ConcurrentDictionary<string, string> Reasons = new(StringComparer.OrdinalIgnoreCase);

    public static void Remember(string invoiceNumber, string cancelReason)
    {
        invoiceNumber = (invoiceNumber ?? string.Empty).Trim();
        cancelReason = (cancelReason ?? string.Empty).Trim();
        if (invoiceNumber.Length == 0 || cancelReason.Length == 0) return;
        Reasons[invoiceNumber] = cancelReason;
    }

    public static string ReasonFor(string invoiceNumber)
    {
        invoiceNumber = (invoiceNumber ?? string.Empty).Trim();
        return invoiceNumber.Length != 0 && Reasons.TryGetValue(invoiceNumber, out var value) ? value : string.Empty;
    }

    public static ParsedVoidReason? Parse(string value)
    {
        value = (value ?? string.Empty).Trim();
        if (value.Length == 0) return null;
        var parts = value.Split('-', StringSplitOptions.None);
        if (parts.Length < 2) return null;

        static bool EmployeeNo(string text) => text.Length == 4 && text.All(char.IsAsciiDigit);
        if (!EmployeeNo(parts[0])) return null;

        if (parts.Length >= 3 && EmployeeNo(parts[1]))
        {
            var reason = string.Join('-', parts.Skip(2)).Trim();
            return reason.Length == 0 ? null : new ParsedVoidReason(parts[1], parts[0], reason);
        }

        var directReason = string.Join('-', parts.Skip(1)).Trim();
        return directReason.Length == 0 ? null : new ParsedVoidReason(parts[0], string.Empty, directReason);
    }
}
