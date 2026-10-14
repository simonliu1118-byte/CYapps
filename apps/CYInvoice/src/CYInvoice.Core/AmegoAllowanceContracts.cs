using System.Security.Cryptography;
using System.Text.Json;

namespace CYInvoice.Core.Amego;

// The number is supplied by CYInvoice, not assigned by AMEGO. Generate it ONCE
// and durably persist the pending operation BEFORE calling /json/g0401.
public static class AllowanceNumberGenerator
{
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    public static string NewNumber()
    {
        Span<byte> random = stackalloc byte[10]; // 80 random bits, 16 base32 characters.
        RandomNumberGenerator.Fill(random);
        Span<char> text = stackalloc char[16];
        var accumulator = 0;
        var bits = 0;
        var index = 0;
        foreach (var value in random)
        {
            accumulator = (accumulator << 8) | value;
            bits += 8;
            while (bits >= 5)
            {
                bits -= 5;
                text[index++] = Alphabet[(accumulator >> bits) & 31];
            }
        }
        return new string(text);
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
