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
    FullMatchUnattributed,
    PendingOfficialReadback,
    ConfirmedNumberCollision,
    ReservedNotSent
}

public sealed record LocalAllowanceSendResult(
    LocalAllowanceSendOutcome Outcome, string AllowanceNumber, string Message);

// Core-only B2c boundary. There is deliberately NO WinForms caller yet.
// A later UI/service must supply a fresh manager-and-AMEGO preflight and
// independently confirm live approval before invoking the first send.
public sealed class LocalAllowanceOneShotService
{
    private readonly LocalAllowanceNumberStore ledger;
    private readonly IAmegoAllowanceGateway amego;
    private readonly Func<LocalAllowanceReservation, CancellationToken, Task> requireFreshPreflight;

    public LocalAllowanceOneShotService(LocalAllowanceNumberStore ledger,
        IAmegoAllowanceGateway amego,
        Func<LocalAllowanceReservation, CancellationToken, Task> requireFreshPreflight)
    {
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
        try
        {
            var accepted = await amego.IssueAllowanceOnceAsync(frozen, cancellationToken)
                .ConfigureAwait(false);
            if (AmegoAllowanceErrorCodes.IsAllowanceNumberOccupied(accepted.Code))
            {
                ledger.MarkExternalCollision(sellerInvoice, environment, saved, accepted.Code);
                return new(LocalAllowanceSendOutcome.ConfirmedNumberCollision, saved.AllowanceNumber,
                    "光貿明確拒絕號碼，舊號已保存；允許同原申請自動改配下一號");
            }
            if (accepted.Code != 0)
                return Pending(saved, "光貿未明確接受折讓，須依原號查回，不得重新送出");
        }
        catch (AmegoApiException error) when (
            AmegoAllowanceErrorCodes.IsAllowanceNumberOccupied(error.Code))
        {
            // Only a definitive rejection from the FIRST g0401 attempt may
            // record a collision. This version NEVER allocates a new number.
            ledger.MarkExternalCollision(sellerInvoice, environment, saved, error.Code);
            return new(LocalAllowanceSendOutcome.ConfirmedNumberCollision,
                saved.AllowanceNumber, "光貿明確拒絕重複折讓號碼；保留原號，尚未自動加號");
        }
        catch (Exception error) when (
            error is HttpRequestException or TaskCanceledException or IOException or
            JsonException or InvalidDataException or AmegoApiException)
        {
            return Pending(saved, "g0401 回覆不明或遭拒絕，鎖定原號供唯讀查回；禁止重新送出");
        }

        // Even code=0 and an exact official readback do NOT attribute this
        // transaction to the present POST, so no automatic confirmed state.
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
        // Legacy audit column official_snapshot_sha256 stores a HASH of
        // collision evidence for this new path, NOT an official query snapshot.
        // This identity is safe: no buyer, invoice or App Key is logged.
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            "amego:confirmed_number_rejection:" + sellerInvoice + ":" +
            environment + ":" + saved.AllowanceNumber))).ToLowerInvariant();
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
        return ledger.ReserveAfterVerifiedExternalCollision(sellerInvoice, environment,
            saved.WorkItemId, day, template, saved.ApproverEmployeeNo,
            saved.ApprovedAtUtc, saved, digest);
    }

    // Called only after a definitive first-POST duplicate-number error was
    // committed as occupied_external. A fresh official *different* allowance
    // and renewed manager approval are needed before reserving a new number.
    // This method performs ZERO g0401 POSTs.
    public async Task<LocalAllowanceReservation> ReserveAfterExternalCollisionAsync(
        string sellerInvoice,string environment,LocalAllowanceReservation previous,
        string typedManagerConfirmation,CancellationToken cancellationToken=default)
    {
        if(typedManagerConfirmation!="確認外部占號並重新配號")
            throw new InvalidOperationException("Manager must explicitly acknowledge reallocation");
        var saved=RequireCurrent(sellerInvoice,environment,previous);
        if(saved.Status!="occupied_external")
            throw new InvalidOperationException("Only a definitive first-POST collision may reallocate");
        // The same fresh manager and official original-invoice preflight used
        // before the first attempt must succeed for the immutable approval.
        await requireFreshPreflight(saved,cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var frozen=ReadFrozen(saved);
        AllowanceQueryResponse queried;
        try
        {
            queried=await amego.QueryAllowanceAsync(saved.AllowanceNumber,cancellationToken)
                .ConfigureAwait(false);
        }
        catch(Exception ex) when(ex is AmegoApiException or HttpRequestException
            or TaskCanceledException or IOException or JsonException)
        {
            throw new InvalidOperationException(
                "Official collision evidence unavailable; retain the original number",ex);
        }
        var official=queried.Data;
        // Reject not-found, ambiguous matches, different number, voided or
        // incompletely identified rows. Require BOTH another buyer AND other
        // original invoice(s), not just a mismatching field or error code.
        if(queried.Code!=0 || official is null ||
           official.AllowanceNumber!=saved.AllowanceNumber ||
           official.InvoiceType is not ("D0401" or "B0401") ||
           official.InvoiceStatus!=UploadStatuses.Complete ||
           official.CancelDate!=0 ||
           string.IsNullOrWhiteSpace(official.BuyerIdentifier) ||
           string.Equals(official.BuyerIdentifier,frozen.BuyerIdentifier,StringComparison.Ordinal) ||
           official.ProductItems.ValueKind!=JsonValueKind.Array ||
           official.ProductItems.GetArrayLength()==0)
            throw new InvalidOperationException(
                "Official number is not proven to be a separate completed external allowance");
        var originals=frozen.ProductItem.Select(p=>p.OriginalInvoiceNumber)
            .ToHashSet(StringComparer.Ordinal);
        foreach(var item in official.ProductItems.EnumerateArray())
        {
            if(item.ValueKind!=JsonValueKind.Object ||
               !item.TryGetProperty("original_invoice_number",out var number) ||
               number.ValueKind!=JsonValueKind.String ||
               string.IsNullOrWhiteSpace(number.GetString()) ||
               originals.Contains(number.GetString()!))
                throw new InvalidOperationException(
                    "Official collision may be the original invoice; prohibit reallocation");
        }
        // Another actor may have changed this Work Item while the query ran.
        await requireFreshPreflight(saved,cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        saved=RequireCurrent(sellerInvoice,environment,previous);
        if(saved.Status!="occupied_external")
            throw new InvalidOperationException("Collision Ledger changed while verifying");
        var fingerprint=Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(official)))).ToLowerInvariant();
        // Preserve all original approved amounts and lines exactly; the Store
        // atomically appends attempt N+1 and an immutable collision review.
        var template=new AllowanceIssueRequest {
            AllowanceDate=frozen.AllowanceDate,AllowanceNumber=string.Empty,
            AllowanceType=frozen.AllowanceType,BuyerIdentifier=frozen.BuyerIdentifier,
            BuyerName=frozen.BuyerName,BuyerAddress=frozen.BuyerAddress,
            BuyerTelephoneNumber=frozen.BuyerTelephoneNumber,
            BuyerEmailAddress=frozen.BuyerEmailAddress,
            ProductItem=frozen.ProductItem,TaxAmount=frozen.TaxAmount,
            TotalAmount=frozen.TotalAmount
        };
        var day=DateOnly.ParseExact(frozen.AllowanceDate,"yyyyMMdd",CultureInfo.InvariantCulture);
        return ledger.ReserveAfterVerifiedExternalCollision(sellerInvoice,environment,
            saved.WorkItemId,day,template,saved.ApproverEmployeeNo,
            saved.ApprovedAtUtc,saved,fingerprint);
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
                "號碼已遭光貿明確拒絕，不會自動重配");
        if (saved.Status != "sending")
            throw new InvalidOperationException("未知折讓執行狀態，停止操作");
        try
        {
            var official = await amego.QueryAllowanceAsync(saved.AllowanceNumber, cancellationToken)
                .ConfigureAwait(false);
            if (official.Code != 0 || !MatchesFrozen(ReadFrozen(saved), official.Data))
                return Pending(saved, "光貿尚未提供可唯一比對的已完成折讓，保持原號等待查回");
            // Hash exact official evidence but retain no raw buyer or item PII.
            // The readback and matching number are never origin evidence.
            var digest = Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes(JsonSerializer.Serialize(official.Data))))
                .ToLowerInvariant();
            ledger.RecordUnattributedFullMatch(sellerInvoice,environment,saved,digest);
            return new(LocalAllowanceSendOutcome.FullMatchUnattributed,saved.AllowanceNumber,
                "原號及全部官方明細吻合，但無法證明為本次送出；保留 sending，須人工查證，不得重送");
        }
        catch (Exception error) when (
            error is AmegoApiException or HttpRequestException or TaskCanceledException or
            IOException or JsonException or InvalidDataException)
        {
            return Pending(saved, "唯讀官方查回未完成，原號維持 sending，不得重送或換號");
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

    private static bool MatchesFrozen(AllowanceIssueRequest frozen, AllowanceQueryItem official)
    {
        if (official.AllowanceNumber != frozen.AllowanceNumber ||
            official.InvoiceType is not ("D0401" or "B0401") ||
            official.InvoiceStatus != UploadStatuses.Complete ||
            official.AllowanceType != 2 || official.CancelDate != 0 ||
            official.AllowanceDate != frozen.AllowanceDate ||
            !string.Equals(official.BuyerIdentifier, frozen.BuyerIdentifier, StringComparison.Ordinal) ||
            !MatchDecimal(official.TotalAmount, frozen.TotalAmount) ||
            !MatchDecimal(official.TaxAmount, frozen.TaxAmount) ||
            official.ProductItems.ValueKind != JsonValueKind.Array ||
            official.ProductItems.GetArrayLength() != frozen.ProductItem.Count)
            return false;
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
                return false;
        }
        return true;
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
