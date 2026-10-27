using Xunit;

namespace CYERPAutoQuery.Tests;

public sealed class ErpRestartTargetsTests
{
    [Fact]
    public void Ends_the_window_owners_in_this_session_with_the_main_window_last()
    {
        var owners = new[]
        {
            new ErpWindowOwner(9656, 1, OwnsMainWindow: true),
            new ErpWindowOwner(12100, 1, OwnsMainWindow: false),
            new ErpWindowOwner(12100, 1, OwnsMainWindow: false),
        };
        Assert.Equal([12100, 9656], ErpRestartTargets.Select(owners, sessionId: 1, ownPid: 500));
    }

    [Fact]
    public void Never_touches_another_session_an_unknown_session_or_this_program()
    {
        var owners = new[]
        {
            new ErpWindowOwner(7000, 2, OwnsMainWindow: true),
            new ErpWindowOwner(7001, null, OwnsMainWindow: false),
            new ErpWindowOwner(500, 1, OwnsMainWindow: false),
        };
        Assert.Empty(ErpRestartTargets.Select(owners, sessionId: 1, ownPid: 500));
    }

    [Fact]
    public void A_process_owning_both_kinds_of_window_counts_as_the_main()
    {
        var owners = new[]
        {
            new ErpWindowOwner(9656, 1, OwnsMainWindow: false),
            new ErpWindowOwner(9656, 1, OwnsMainWindow: true),
            new ErpWindowOwner(12100, 1, OwnsMainWindow: false),
        };
        Assert.Equal([12100, 9656], ErpRestartTargets.Select(owners, sessionId: 1, ownPid: 500));
    }
}
