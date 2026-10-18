using System.Globalization;
using System.Text.Json;

namespace CYInvoice.Core.Amego;

// A + yyyyMMdd + XXX. 001..999 are decimal; 1000..34695 map to A00..ZZZ.
// Number assignment is NOT performed here: the approving manager's operation
// must allocate and durably persist exactly one number via the authoritative
// company/environment store before invoking /json/g0401.
// Allocated numbers are never recycled, even after rejection or cancellation.
public static class AllowanceNumberGenerator
{
    public const char DefaultPrefix = 'A';
    public const int DailyMaximum = 999 + 26 * 36 * 36;
    private const string Digits = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";

    public static string Format(DateOnly allowanceDate, int sequence, char prefix = DefaultPrefix)
    {
        if (prefix is < 'A' or > 'Z')
            throw new ArgumentOutOfRangeException(nameof(prefix), "prefix must be one uppercase ASCII letter");
        if (sequence is < 1 or > DailyMaximum)
            throw new ArgumentOutOfRangeException(nameof(sequence), "daily allowance sequence out of range");
        string suffix;
        if (sequence <= 999)
        {
            suffix = sequence.ToString("D3", CultureInfo.InvariantCulture);
        }
        else
        {
            var index = sequence - 1000;
            suffix = string.Concat(
                (char)('A' + index / (36 * 36)),
                Digits[(index / 36) % 36],
                Digits[index % 36]);
        }
        return string.Concat(prefix, allowanceDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture), suffix);
    }

    public static bool TryReadSequence(string? number, DateOnly allowanceDate, out int sequence, char prefix = DefaultPrefix)
    {
        sequence = 0;
        if (prefix is < 'A' or > 'Z' || number is not { Length: 12 } || number[0] != prefix ||
            !string.Equals(number.Substring(1, 8), allowanceDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture), StringComparison.Ordinal))
            return false;

        var suffix = number.AsSpan(9, 3);
        if (int.TryParse(suffix, NumberStyles.None, CultureInfo.InvariantCulture, out var decimalSequence))
        {
            if (decimalSequence is < 1 or > 999) return false;
            sequence = decimalSequence;
            return true;
        }

        if (suffix[0] is < 'A' or > 'Z') return false;
        var second = Digits.IndexOf(suffix[1]);
        var third = Digits.IndexOf(suffix[2]);
        if (second < 0 || third < 0) return false;
        sequence = 1000 + (suffix[0] - 'A') * (36 * 36) + second * 36 + third;
        return true;
    }
}

// Official AMEGO g0401 errors: https://invoice.amego.tw/info_detail?mid=71
// The code identifies an existing allowance number, but retrying with a new
// number is safe ONLY when the operation had no earlier ambiguous submission.
// The workflow (not this classifier) must enforce that precondition.
public static class AmegoAllowanceErrorCodes
{
    public const int ExistingIssuedAllowanceNumber = 4040161;
    public const int ExistingVoidedAllowanceNumber = 4040162;
    public const int IncorrectAllowanceType = 4040163;

    // Both an issued and a subsequently voided allowance permanently occupy
    // the same number. This only classifies a confirmed API rejection;
    // it does NOT authorize a new submission after an ambiguous prior attempt.
    public static bool IsAllowanceNumberOccupied(int code) =>
        code is ExistingIssuedAllowanceNumber or ExistingVoidedAllowanceNumber;
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

