using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Net.Http;
using System.Text.Json;
using CYInvoice.Core.Amego;

namespace CYInvoice.Core.Invoicing;

public enum LocalAllowanceSendOutcome
{
    OfficiallyConfirmed,
    PendingOfficialReadback,
    ConfirmedNumberCollision,
    ReservedNotSent,
    Problem
}

public sealed record LocalAllowanceSendResult(
    LocalAllowanceSendOutcome Outcome, string AllowanceNumber, string Message);

// Single Local g0401 transport owner. The approved workflow supplies fresh
// authority/official preflight; restart and unknown outcomes use readback only.
public sealed class LocalAllowanceOneShotService
{
    private readonly LocalAllowanceNumberStore ledger;
    private readonly IAmegoAllowanceGateway amego;
    private readonly Func<LocalAllowanceReservation, CancellationToken, Task> requireFreshPreflight;
    private readonly Action<string, int?, Exception?>? diagnostic;

    public LocalAllowanceOneShotService(LocalAllowanceNumberStore ledger,
        IAmegoAllowanceGateway amego,
        Func<LocalAllowanceReservation, CancellationToken, Task> requireFreshPreflight,
        Action<string, int?, Exception?>? diagnostic = null)
    {
        this.diagnostic = diagnostic;
        this.ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
        this.amego = amego ?? throw new ArgumentNullException(nameof(amego));
        this.requireFreshPreflight = requireFreshPreflight ??
            throw new ArgumentNullException(nameof(requireFreshPreflight));
    }

    public async Task<LocalAllowanceSendResult> SendFirstAsync(
        string sellerInvoice, string environment, LocalAllowanceReservation reservation,
        CancellationToken cancellationToken = default)
    {
        var saved = RequireCurrent(sellerInvoice, environment, reservation);
        if (saved.Status != "reserved")
            throw new InvalidOperationException(
                "折讓已送出、遭占號或等待查回，不能再發送 g0401");
        // A successful earlier on-screen preview is NOT sufficient. The
        // future caller supplies account-bound, manager-authorized live
        // AMEGO verification for this exact frozen request.
        await requireFreshPreflight(saved, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        saved = RequireCurrent(sellerInvoice, environment, reservation);
        if (saved.Status != "reserved")
            throw new InvalidOperationException("折讓狀態已改變，不能重複發送");
        var frozen = ReadFrozen(saved);
        // Full-sync SQLite CAS commits BEFORE a single externally visible
        // HTTP call. If anything fails after this point, including a crash,
        // the original number remains 'sending'; re-entry cannot POST again.
        ledger.MarkSending(sellerInvoice, environment, saved);
        diagnostic?.Invoke("send_started", null, null);
        try
        {
            var accepted = await amego.IssueAllowanceOnceAsync(frozen, cancellationToken)
                .ConfigureAwait(false);
            diagnostic?.Invoke("response_received", accepted.Code, null);
            if (AmegoAllowanceErrorCodes.IsAllowanceNumberOccupied(accepted.Code))
            {
                ledger.MarkExternalCollision(sellerInvoice, environment, saved, accepted.Code);
                return new(LocalAllowanceSendOutcome.ConfirmedNumberCollision, saved.AllowanceNumber,
                    "光貿明確拒絕號碼，舊號已保存；允許同原申請自動改配下一號");
            }
            if (accepted.Code != 0)
                return await ReconcileAsync(sellerInvoice, environment, saved, cancellationToken).ConfigureAwait(false);
        }
        catch (AmegoApiException error) when (
            AmegoAllowanceErrorCodes.IsAllowanceNumberOccupied(error.Code))
        {
            diagnostic?.Invoke("response_received", error.Code, error);
            // The automatic execution owner advances only after this rejection.
            ledger.MarkExternalCollision(sellerInvoice, environment, saved, error.Code);
            return new(LocalAllowanceSendOutcome.ConfirmedNumberCollision,
                saved.AllowanceNumber, "光貿明確拒絕重複折讓號碼；保留原號，尚未自動加號");
        }
        catch (Exception error) when (
            error is HttpRequestException or TaskCanceledException or IOException or
            JsonException or InvalidDataException or AmegoApiException)
        {
            diagnostic?.Invoke("response_unknown", error is AmegoApiException api ? api.Code : null, error);
            return await ReconcileAsync(sellerInvoice, environment, saved, cancellationToken).ConfigureAwait(false);
        }

        // ACK alone is insufficient. Full original-number content proof may
        // complete the approved operation without asserting POST attribution.
        return await ReconcileAsync(sellerInvoice, environment, saved, cancellationToken)
            .ConfigureAwait(false);
    }

    // The only automatic number-advance owner. An acknowledged 4040161 or
    // 4040162 transitions the original persisted attempt to occupied_external;
    // only that explicit rejection allows a new number. No artificial limit:
    // the user may cancel, and the number generator enforces legal capacity.
    // A timeout, crash, unknown code or successful POST NEVER loops to g0401.
    public async Task<LocalAllowanceSendResult> SendWithAutomaticNumberAdvanceAsync(
        string sellerInvoice, string environment, LocalAllowanceReservation reservation,
        Action<string>? reportProgress = null, CancellationToken cancellationToken = default)
    {
        var current = RequireCurrent(sellerInvoice, environment, reservation);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (current.Status == "occupied_external")
            {
                current = await ReserveAfterConfirmedRejectionAsync(
                    sellerInvoice, environment, current, cancellationToken).ConfigureAwait(false);
                reportProgress?.Invoke("光貿明確拒絕上一折讓號，已安全記錄並改配新號：" +
                    current.AllowanceNumber);
            }
            if (current.Status != "reserved")
                return await ReconcileAsync(sellerInvoice, environment, current,
                    cancellationToken).ConfigureAwait(false);
            var outcome = await SendFirstAsync(sellerInvoice, environment, current,
                cancellationToken).ConfigureAwait(false);
            reportProgress?.Invoke(current.AllowanceNumber + "：" + outcome.Outcome);
            if (outcome.Outcome != LocalAllowanceSendOutcome.ConfirmedNumberCollision)
                return outcome;
            current = RequireCurrent(sellerInvoice, environment, current);
        }
    }

    // A persisted occupied_external state is reachable only after an explicit
    // provider number-rejection response. Do not require a second manager
    // click, textual acknowledgement, different buyer or another invoice.
    // Fresh authorization/unchanged approved payload remains mandatory.
    public async Task<LocalAllowanceReservation> ReserveAfterConfirmedRejectionAsync(
        string sellerInvoice, string environment, LocalAllowanceReservation previous,
        CancellationToken cancellationToken = default)
    {
        var saved = RequireCurrent(sellerInvoice, environment, previous);
        if (saved.Status != "occupied_external")
            throw new InvalidOperationException("Only an explicitly rejected number may advance");
        await requireFreshPreflight(saved, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        saved = RequireCurrent(sellerInvoice, environment, previous);
        if (saved.Status != "occupied_external")
            throw new InvalidOperationException("The prior attempt changed while advancing number");
        var frozen = ReadFrozen(saved);
        var template = new AllowanceIssueRequest {
            AllowanceDate = frozen.AllowanceDate, AllowanceNumber = string.Empty,
            AllowanceType = frozen.AllowanceType, BuyerIdentifier = frozen.BuyerIdentifier,
            BuyerName = frozen.BuyerName, BuyerAddress = frozen.BuyerAddress,
            BuyerTelephoneNumber = frozen.BuyerTelephoneNumber,
            BuyerEmailAddress = frozen.BuyerEmailAddress,
            ProductItem = frozen.ProductItem, TaxAmount = frozen.TaxAmount,
            TotalAmount = frozen.TotalAmount
        };
        var day = DateOnly.ParseExact(frozen.AllowanceDate, "yyyyMMdd",
            CultureInfo.InvariantCulture);
        return ledger.ReserveAfterDefinitiveRejection(sellerInvoice, environment,
            saved.WorkItemId, day, template, saved.ApproverEmployeeNo,
            saved.ApprovedAtUtc, saved);
    }

    public async Task<LocalAllowanceSendResult> ReconcileAsync(
        string sellerInvoice, string environment, LocalAllowanceReservation reservation,
        CancellationToken cancellationToken = default)
    {
        var saved = RequireCurrent(sellerInvoice, environment, reservation);
        if (saved.Status == "confirmed")
            return new(LocalAllowanceSendOutcome.OfficiallyConfirmed, saved.AllowanceNumber,
                "已由光貿官方資料核對完成");
        if (saved.Status == "reserved")
            return new(LocalAllowanceSendOutcome.ReservedNotSent, saved.AllowanceNumber,
                "僅預留折讓號碼，尚未送出");
        if (saved.Status == "occupied_external")
            return new(LocalAllowanceSendOutcome.ConfirmedNumberCollision, saved.AllowanceNumber,
                "原號已收到明確拒絕，只有原申請受控流程可以繼續配號");
        if (saved.Status != "sending")
            throw new InvalidOperationException("未知折讓執行狀態，停止操作");
        try
        {
            var official = await amego.QueryAllowanceAsync(saved.AllowanceNumber, cancellationToken)
                .ConfigureAwait(false);
            diagnostic?.Invoke("readback_received", official.Code, null);
            if (official.Code == 0 && official.Data is { } waiting &&
                waiting.AllowanceNumber == saved.AllowanceNumber &&
                InvoiceWorkQueue.IsPendingUpload(waiting.InvoiceStatus))
                return Pending(saved, "光貿正在處理原折讓，系統會自動追蹤");
            var mismatch = official.Code == 0 ? FrozenMismatch(ReadFrozen(saved), official.Data) : "provider_rejected";
            if (mismatch is not null)
            {
                diagnostic?.Invoke("readback_" + mismatch, official.Code, null);
                return new(LocalAllowanceSendOutcome.Problem, saved.AllowanceNumber,
                    "原號查證尚無法安全確認，已保存上傳問題；系統會繼續查回，不會重送或換號");
            }
            // Hash exact official evidence but retain no raw buyer or item PII.
            // The readback and matching number are never origin evidence.
            var digest = Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes(JsonSerializer.Serialize(official.Data))))
                .ToLowerInvariant();
            ledger.RecordUnattributedFullMatch(sellerInvoice,environment,saved,digest);
            ledger.MarkOfficiallyConfirmed(sellerInvoice, environment, saved);
            return new(LocalAllowanceSendOutcome.OfficiallyConfirmed, saved.AllowanceNumber,
                "原號、原發票與全部折讓明細已由官方確認完成");
        }
        catch (Exception error) when (
            error is AmegoApiException or HttpRequestException or TaskCanceledException or
            IOException or JsonException or InvalidDataException)
        {
            diagnostic?.Invoke("readback_failed", error is AmegoApiException api ? api.Code : null, error);
            return new(LocalAllowanceSendOutcome.Problem, saved.AllowanceNumber,
                "官方查回失敗，原號保留且不重送；詳細紀錄已保存至上傳問題");
        }
    }

    private LocalAllowanceReservation RequireCurrent(string seller, string environment,
        LocalAllowanceReservation reservation)
    {
        ArgumentNullException.ThrowIfNull(reservation);
        var current = ledger.FindLatest(seller, environment, reservation.WorkItemId)
            ?? throw new InvalidOperationException("找不到本機原折讓核准與配號紀錄");
        if (current.AllowanceNumber != reservation.AllowanceNumber ||
            current.PayloadJson != reservation.PayloadJson ||
            current.ApprovedAtUtc != reservation.ApprovedAtUtc ||
            current.ApproverEmployeeNo != reservation.ApproverEmployeeNo)
            throw new InvalidDataException("折讓執行資料與已鎖定的配號紀錄不符");
        return current;
    }

    private static AllowanceIssueRequest ReadFrozen(LocalAllowanceReservation saved)
    {
        var frozen = JsonSerializer.Deserialize<AllowanceIssueRequest>(saved.PayloadJson)
            ?? throw new InvalidDataException("凍結的折讓 API 內容無法讀取");
        if (frozen.AllowanceNumber != saved.AllowanceNumber ||
            frozen.AllowanceType != 2 || frozen.ProductItem is not { Count: > 0 } ||
            JsonSerializer.Serialize(frozen) != saved.PayloadJson)
            throw new InvalidDataException("折讓送出內容與已持久化的原始版本不符");
        return frozen;
    }

    private static string? FrozenMismatch(AllowanceIssueRequest frozen, AllowanceQueryItem official)
    {
        if (official.AllowanceNumber != frozen.AllowanceNumber) return "number_mismatch";
        if (official.InvoiceType is not ("D0401" or "B0401") ||
            official.InvoiceStatus != UploadStatuses.Complete ||
            official.AllowanceType != 2 || official.CancelDate != 0) return "state_mismatch";
        if (official.AllowanceDate != frozen.AllowanceDate) return "date_mismatch";
        if (!string.Equals(official.BuyerIdentifier, frozen.BuyerIdentifier, StringComparison.Ordinal) ||
            !string.Equals(official.BuyerName, frozen.BuyerName, StringComparison.Ordinal)) return "buyer_mismatch";
        if (!MatchDecimal(official.TotalAmount, frozen.TotalAmount) ||
            !MatchDecimal(official.TaxAmount, frozen.TaxAmount)) return "amount_mismatch";
        if (official.ProductItems.ValueKind != JsonValueKind.Array ||
            official.ProductItems.GetArrayLength() != frozen.ProductItem.Count) return "items_mismatch";
        for (var i = 0; i < frozen.ProductItem.Count; i++)
        {
            var source = frozen.ProductItem[i];
            var candidate = official.ProductItems[i];
            if (candidate.ValueKind != JsonValueKind.Object ||
                !MatchText(candidate, "original_invoice_number", source.OriginalInvoiceNumber) ||
                !MatchText(candidate, "original_invoice_date", source.OriginalInvoiceDate.ToString(CultureInfo.InvariantCulture)) ||
                !MatchText(candidate, "original_description", source.OriginalDescription) ||
                !MatchDecimal(candidate, "quantity", source.Quantity) ||
                !MatchDecimal(candidate, "unit_price", source.UnitPrice) ||
                !MatchDecimal(candidate, "amount", source.Amount) ||
                !MatchDecimal(candidate, "tax", source.Tax) ||
                !MatchDecimal(candidate, "tax_type", source.TaxType))
                return "items_mismatch";
        }
        return null;
    }

    private static bool MatchText(JsonElement source, string property, string expected) =>
        source.TryGetProperty(property, out var value) &&
        value.ValueKind is JsonValueKind.String or JsonValueKind.Number &&
        string.Equals(value.ToString().Trim(), expected, StringComparison.Ordinal);
    private static bool MatchDecimal(JsonElement source, string property, decimal expected) =>
        source.TryGetProperty(property, out var value) && MatchDecimal(value.ToString(), expected);
    private static bool MatchDecimal(string text, decimal expected) =>
        decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) &&
        parsed == expected;
    private static LocalAllowanceSendResult Pending(LocalAllowanceReservation reservation, string text) =>
        new(LocalAllowanceSendOutcome.PendingOfficialReadback, reservation.AllowanceNumber, text);
}
