namespace CYERPAutoInput;

internal enum ErpMode { Unknown, Browse, Input }

internal enum ErpDocumentState
{
    NotFound,
    MultipleWindows,
    Unknown,
    Browse,
    New,
    Modify,
    InputUnconfirmed
}

/// <summary>Whether the 交易資料 部門代號 / 業務人員 pair carries document data.</summary>
internal enum ErpOwnerFields { Unreadable, BothEmpty, BothFilled, Mixed }

/// <summary>One read of COPI08 used to classify the document state.</summary>
internal sealed record ErpStatusProbe(int WindowCount, nint Window, ErpMode Mode, ErpOwnerFields OwnerFields);

/// <summary>
/// Classifies COPI08 as 檢視 / 新增 / 修改 for display only.
/// ERP enters input mode for both 新增 and 修改, and 新增 already fills today's date and
/// the next sales order number, so the number cannot tell them apart. 部門代號 and
/// 業務人員 can: a fresh 新增 leaves both empty, an existing document always has both.
/// The classification is taken at the Browse → Input transition (before anyone types a
/// customer that may auto-fill them) and held until ERP leaves input mode.
/// </summary>
internal sealed class ErpDocumentStateTracker
{
    private nint _window;
    private ErpMode _lastDefiniteMode = ErpMode.Unknown;
    private ErpDocumentState? _inputState;

    public ErpDocumentState Update(ErpStatusProbe probe)
    {
        if (probe.WindowCount == 0) { Reset(0); return ErpDocumentState.NotFound; }
        if (probe.WindowCount > 1) { Reset(0); return ErpDocumentState.MultipleWindows; }
        if (probe.Window != _window) Reset(probe.Window);

        switch (probe.Mode)
        {
            case ErpMode.Browse:
                _lastDefiniteMode = ErpMode.Browse;
                _inputState = null;
                return ErpDocumentState.Browse;

            case ErpMode.Input:
                if (_inputState is null or ErpDocumentState.InputUnconfirmed)
                {
                    _inputState = Classify(probe.OwnerFields, _lastDefiniteMode == ErpMode.Browse);
                    // An unconfirmed read keeps the Browse context so the next read can still classify.
                    if (_inputState == ErpDocumentState.InputUnconfirmed) return _inputState.Value;
                }
                _lastDefiniteMode = ErpMode.Input;
                return _inputState.Value;

            default:
                // Transient unknown (dialog, tab switch) keeps the last definite mode
                // so a later Browse → Input transition is still recognised.
                return ErpDocumentState.Unknown;
        }
    }

    /// <summary>
    /// Pure classification. Without the Browse → Input transition, filled fields may be a
    /// 新增 whose customer already auto-filled them, so only "both empty" is conclusive.
    /// </summary>
    internal static ErpDocumentState Classify(ErpOwnerFields fields, bool cameFromBrowse) => fields switch
    {
        ErpOwnerFields.BothEmpty => ErpDocumentState.New,
        ErpOwnerFields.BothFilled when cameFromBrowse => ErpDocumentState.Modify,
        _ => ErpDocumentState.InputUnconfirmed
    };

    internal static ErpOwnerFields Combine(string? department, string? salesperson)
    {
        if (department is null || salesperson is null) return ErpOwnerFields.Unreadable;
        var d = department.Trim().Length > 0;
        var s = salesperson.Trim().Length > 0;
        return d && s ? ErpOwnerFields.BothFilled : !d && !s ? ErpOwnerFields.BothEmpty : ErpOwnerFields.Mixed;
    }

    private void Reset(nint window)
    {
        _window = window;
        _lastDefiniteMode = ErpMode.Unknown;
        _inputState = null;
    }
}
