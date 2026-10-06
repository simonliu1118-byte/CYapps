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

/// <summary>One read of COPI08 used to classify the document state.</summary>
/// <param name="SalesOrderNumberPresent">null when the number field could not be read.</param>
internal sealed record ErpStatusProbe(int WindowCount, nint Window, ErpMode Mode, bool? SalesOrderNumberPresent);

/// <summary>
/// Classifies COPI08 as 檢視 / 新增 / 修改 for display only.
/// ERP enters input mode for both 新增 and 修改; the difference is visible at the
/// Browse → Input transition: 新增 clears the sales order number, 修改 keeps the
/// existing one. Once classified, the state holds until ERP leaves input mode.
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
                    _inputState = ClassifyInput(probe.SalesOrderNumberPresent, _lastDefiniteMode == ErpMode.Browse);
                    // An unreadable number keeps the Browse context so the next read can still classify.
                    if (probe.SalesOrderNumberPresent is null) return _inputState.Value;
                }
                _lastDefiniteMode = ErpMode.Input;
                return _inputState.Value;

            default:
                // Transient unknown (dialog, tab switch) keeps the last definite mode
                // so a later Browse → Input transition is still recognised.
                return ErpDocumentState.Unknown;
        }
    }

    private static ErpDocumentState ClassifyInput(bool? numberPresent, bool cameFromBrowse)
    {
        if (numberPresent is null) return ErpDocumentState.InputUnconfirmed;
        if (cameFromBrowse) return numberPresent.Value ? ErpDocumentState.Modify : ErpDocumentState.New;
        // Without the transition, an empty number can only be a new document;
        // a filled number may be 修改 or a 新增 whose number ERP already generated.
        return numberPresent.Value ? ErpDocumentState.InputUnconfirmed : ErpDocumentState.New;
    }

    private void Reset(nint window)
    {
        _window = window;
        _lastDefiniteMode = ErpMode.Unknown;
        _inputState = null;
    }
}
