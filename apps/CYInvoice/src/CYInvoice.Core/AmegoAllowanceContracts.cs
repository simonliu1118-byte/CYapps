using System.Globalization;
using System.Text.Json;

namespace CYInvoice.Core.Amego;

// Approved format: A + yyyyMMdd + three-digit daily sequence, e.g. A20261015001.
// The caller must allocate the sequence against the correct company's existing
// official and locally pending numbers. Formatting does NOT reserve any number.
// Only allocate at manager approval and persist the number before /json/g0401.
public static class AllowanceNumberGenerator
{
    public const char DefaultPrefix = 'A';
    public const int DailyMaximum = 999;

    public static string Format(DateOnly allowanceDate, int sequence, char prefix = DefaultPrefix)
    {
        if (prefix is < 'A' or > 'Z')
            throw new ArgumentOutOfRangeException(nameof(prefix), "prefix must be one uppercase ASCII letter");
        if (sequence is < 1 or > DailyMaximum)
            throw new ArgumentOutOfRangeException(nameof(sequence), "daily allowance sequence must be from 001 to 999");
        return string.Concat(
            prefix,
            allowanceDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
            sequence.ToString("D3", CultureInfo.InvariantCulture));
    }

    public static bool TryReadSequence(string? number, DateOnly allowanceDate, out int sequence, char prefix = DefaultPrefix)
    {
        sequence = 0;
        if (prefix is < 'A' or > 'Z' || number is not { Length: 12 } || number[0] != prefix ||
            !string.Equals(number.Substring(1, 8), allowanceDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture), StringComparison.Ordinal))
            return false;
        if (!int.TryParse(number.AsSpan(9, 3), NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) ||
            parsed is < 1 or > DailyMaximum)
            return false;
        sequence = parsed;
        return true;
    }
}

public sealed class AllowanceProductItem
{
    public string OriginalInvoiceNumber { get; init; } = string.Empty;
    public int OriginalInvoiceDate { get; init; }
    public string OriginalDescription { get; init; } = string.Empty;
    public decimal Quantity { get; init; }
    public decimal UnitPrice { get; init; }
    public decimal Amount { get; init; }
    public decimal Tax { get; init; }
    public int TaxType { get; init; }
}

public sealed class AllowanceIssueRequest
{
    public string AllowanceNumber { get; init; } = string.Empty;
    public string AllowanceDate { get; init; } = string.Empty;
    public int AllowanceType { get; init; } = 2;
    public string BuyerIdentifier { get; init; } = string.Empty;
    public string BuyerName { get; init; } = string.Empty;
    public string BuyerAddress { get; init; } = string.Empty;
    public string BuyerTelephoneNumber { get; init; } = string.Empty;
    public string BuyerEmailAddress { get; init; } = string.Empty;
    public IReadOnlyList<AllowanceProductItem> ProductItem { get; init; } = [];
    public decimal TaxAmount { get; init; }
    public decimal TotalAmount { get; init; }
}

// Code 0 is acceptance of the API call, NOT proof of completed upload.
// The response has no official AllowanceNumber; query by the originally persisted number.
public sealed record AllowanceIssueResponse(int Code, string Message);

public sealed record AllowanceStatusItem(
    string AllowanceNumber,
    string Type,
    int Status,
    string TaxAmount,
    string TotalAmount);
public sealed record AllowanceStatusResponse(int Code, string Message, IReadOnlyList<AllowanceStatusItem> Data);

public sealed record AllowanceQueryItem(
    string AllowanceNumber,
    string InvoiceType,
    int InvoiceStatus,
    string AllowanceDate,
    int AllowanceType,
    string BuyerIdentifier,
    string BuyerName,
    string TaxAmount,
    string TotalAmount,
    long CancelDate,
    JsonElement ProductItems);
public sealed record AllowanceQueryResponse(int Code, string Message, AllowanceQueryItem Data);
