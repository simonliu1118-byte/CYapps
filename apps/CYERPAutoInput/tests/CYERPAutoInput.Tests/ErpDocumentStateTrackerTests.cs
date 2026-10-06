using Xunit;

namespace CYERPAutoInput.Tests;

public class ErpDocumentStateTrackerTests
{
    private static readonly nint Window = 0x1234;

    private static ErpStatusProbe Probe(ErpMode mode, bool? numberPresent = null, int windows = 1, nint? hwnd = null) =>
        new(windows, windows == 1 ? hwnd ?? Window : 0, mode, numberPresent);

    [Fact]
    public void Browse_then_input_with_cleared_number_is_new()
    {
        var tracker = new ErpDocumentStateTracker();
        Assert.Equal(ErpDocumentState.Browse, tracker.Update(Probe(ErpMode.Browse)));
        Assert.Equal(ErpDocumentState.New, tracker.Update(Probe(ErpMode.Input, numberPresent: false)));
    }

    [Fact]
    public void Browse_then_input_with_existing_number_is_modify()
    {
        var tracker = new ErpDocumentStateTracker();
        tracker.Update(Probe(ErpMode.Browse));
        Assert.Equal(ErpDocumentState.Modify, tracker.Update(Probe(ErpMode.Input, numberPresent: true)));
    }

    [Fact]
    public void New_stays_new_after_erp_generates_the_number()
    {
        var tracker = new ErpDocumentStateTracker();
        tracker.Update(Probe(ErpMode.Browse));
        tracker.Update(Probe(ErpMode.Input, numberPresent: false));
        Assert.Equal(ErpDocumentState.New, tracker.Update(Probe(ErpMode.Input, numberPresent: true)));
    }

    [Fact]
    public void Transient_unknown_between_browse_and_input_keeps_the_transition()
    {
        var tracker = new ErpDocumentStateTracker();
        tracker.Update(Probe(ErpMode.Browse));
        Assert.Equal(ErpDocumentState.Unknown, tracker.Update(Probe(ErpMode.Unknown)));
        Assert.Equal(ErpDocumentState.Modify, tracker.Update(Probe(ErpMode.Input, numberPresent: true)));
    }

    [Fact]
    public void Unreadable_number_waits_for_the_next_read_without_losing_the_transition()
    {
        var tracker = new ErpDocumentStateTracker();
        tracker.Update(Probe(ErpMode.Browse));
        Assert.Equal(ErpDocumentState.InputUnconfirmed, tracker.Update(Probe(ErpMode.Input, numberPresent: null)));
        Assert.Equal(ErpDocumentState.Modify, tracker.Update(Probe(ErpMode.Input, numberPresent: true)));
    }

    [Fact]
    public void Started_in_input_mode_with_empty_number_is_new()
    {
        var tracker = new ErpDocumentStateTracker();
        Assert.Equal(ErpDocumentState.New, tracker.Update(Probe(ErpMode.Input, numberPresent: false)));
    }

    [Fact]
    public void Started_in_input_mode_with_number_is_unconfirmed()
    {
        var tracker = new ErpDocumentStateTracker();
        Assert.Equal(ErpDocumentState.InputUnconfirmed, tracker.Update(Probe(ErpMode.Input, numberPresent: true)));
    }

    [Fact]
    public void Returning_to_browse_clears_the_input_classification()
    {
        var tracker = new ErpDocumentStateTracker();
        tracker.Update(Probe(ErpMode.Browse));
        tracker.Update(Probe(ErpMode.Input, numberPresent: true));
        Assert.Equal(ErpDocumentState.Browse, tracker.Update(Probe(ErpMode.Browse)));
        Assert.Equal(ErpDocumentState.New, tracker.Update(Probe(ErpMode.Input, numberPresent: false)));
    }

    [Fact]
    public void Window_count_overrides_mode()
    {
        var tracker = new ErpDocumentStateTracker();
        Assert.Equal(ErpDocumentState.NotFound, tracker.Update(Probe(ErpMode.Unknown, windows: 0)));
        Assert.Equal(ErpDocumentState.MultipleWindows, tracker.Update(Probe(ErpMode.Unknown, windows: 2)));
    }

    [Fact]
    public void A_different_window_starts_a_fresh_classification()
    {
        var tracker = new ErpDocumentStateTracker();
        tracker.Update(Probe(ErpMode.Browse));
        Assert.Equal(ErpDocumentState.InputUnconfirmed, tracker.Update(Probe(ErpMode.Input, numberPresent: true, hwnd: 0x9999)));
    }
}
