using Xunit;

namespace CYERPAutoInput.Tests;

public class ErpDocumentStateTrackerTests
{
    private static readonly nint Window = 0x1234;
    private const ErpOwnerFields Empty = ErpOwnerFields.BothEmpty;
    private const ErpOwnerFields Filled = ErpOwnerFields.BothFilled;

    private static ErpStatusProbe Probe(ErpMode mode, ErpOwnerFields fields = ErpOwnerFields.Unreadable, int windows = 1, nint? hwnd = null) =>
        new(windows, windows == 1 ? hwnd ?? Window : 0, mode, fields);

    [Fact]
    public void Browse_then_input_with_empty_owner_fields_is_new()
    {
        var tracker = new ErpDocumentStateTracker();
        Assert.Equal(ErpDocumentState.Browse, tracker.Update(Probe(ErpMode.Browse)));
        Assert.Equal(ErpDocumentState.New, tracker.Update(Probe(ErpMode.Input, Empty)));
    }

    [Fact]
    public void Browse_then_input_with_filled_owner_fields_is_modify()
    {
        var tracker = new ErpDocumentStateTracker();
        tracker.Update(Probe(ErpMode.Browse));
        Assert.Equal(ErpDocumentState.Modify, tracker.Update(Probe(ErpMode.Input, Filled)));
    }

    [Fact]
    public void New_stays_new_after_the_customer_fills_the_owner_fields()
    {
        var tracker = new ErpDocumentStateTracker();
        tracker.Update(Probe(ErpMode.Browse));
        tracker.Update(Probe(ErpMode.Input, Empty));
        Assert.Equal(ErpDocumentState.New, tracker.Update(Probe(ErpMode.Input, Filled)));
    }

    [Fact]
    public void Transient_unknown_between_browse_and_input_keeps_the_transition()
    {
        var tracker = new ErpDocumentStateTracker();
        tracker.Update(Probe(ErpMode.Browse));
        Assert.Equal(ErpDocumentState.Unknown, tracker.Update(Probe(ErpMode.Unknown)));
        Assert.Equal(ErpDocumentState.Modify, tracker.Update(Probe(ErpMode.Input, Filled)));
    }

    [Theory]
    [InlineData(nameof(ErpOwnerFields.Unreadable))]
    [InlineData(nameof(ErpOwnerFields.Mixed))]
    public void Inconclusive_read_waits_for_the_next_read_without_losing_the_transition(string firstRead)
    {
        var first = Enum.Parse<ErpOwnerFields>(firstRead);
        var tracker = new ErpDocumentStateTracker();
        tracker.Update(Probe(ErpMode.Browse));
        Assert.Equal(ErpDocumentState.InputUnconfirmed, tracker.Update(Probe(ErpMode.Input, first)));
        Assert.Equal(ErpDocumentState.Modify, tracker.Update(Probe(ErpMode.Input, Filled)));
    }

    [Fact]
    public void Started_in_input_mode_with_empty_owner_fields_is_new()
    {
        var tracker = new ErpDocumentStateTracker();
        Assert.Equal(ErpDocumentState.New, tracker.Update(Probe(ErpMode.Input, Empty)));
    }

    [Fact]
    public void Started_in_input_mode_with_filled_owner_fields_is_unconfirmed()
    {
        // A 新增 whose customer was already typed may auto-fill both fields.
        var tracker = new ErpDocumentStateTracker();
        Assert.Equal(ErpDocumentState.InputUnconfirmed, tracker.Update(Probe(ErpMode.Input, Filled)));
    }

    [Fact]
    public void Returning_to_browse_clears_the_input_classification()
    {
        var tracker = new ErpDocumentStateTracker();
        tracker.Update(Probe(ErpMode.Browse));
        tracker.Update(Probe(ErpMode.Input, Filled));
        Assert.Equal(ErpDocumentState.Browse, tracker.Update(Probe(ErpMode.Browse)));
        Assert.Equal(ErpDocumentState.New, tracker.Update(Probe(ErpMode.Input, Empty)));
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
        Assert.Equal(ErpDocumentState.InputUnconfirmed, tracker.Update(Probe(ErpMode.Input, Filled, hwnd: 0x9999)));
    }

    [Theory]
    [InlineData("D01", "E001", nameof(ErpOwnerFields.BothFilled))]
    [InlineData("", " ", nameof(ErpOwnerFields.BothEmpty))]
    [InlineData("D01", "", nameof(ErpOwnerFields.Mixed))]
    [InlineData("", "E001", nameof(ErpOwnerFields.Mixed))]
    [InlineData(null, "E001", nameof(ErpOwnerFields.Unreadable))]
    [InlineData("D01", null, nameof(ErpOwnerFields.Unreadable))]
    public void Combine_reads_the_owner_field_pair(string? department, string? salesperson, string expected) =>
        Assert.Equal(Enum.Parse<ErpOwnerFields>(expected), ErpDocumentStateTracker.Combine(department, salesperson));
}
