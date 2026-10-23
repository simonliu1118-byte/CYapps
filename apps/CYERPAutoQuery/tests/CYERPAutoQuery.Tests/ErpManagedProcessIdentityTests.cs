using Xunit;

namespace CYERPAutoQuery.Tests;

public sealed class ErpManagedProcessIdentityTests
{
    private readonly DateTime _started = new(2026, 10, 22, 9, 24, 16, DateTimeKind.Utc);
    private const string Path = @"C:\ERP\MainMenu.exe";

    [Fact]
    public void Exact_identity_in_same_session_is_accepted()
    {
        var owned = new ErpManagedProcessIdentity(18124, _started, 1, Path);
        Assert.True(owned.Matches(18124, _started, 1, @"c:\erp\mainmenu.EXE", 1));
    }

    [Fact]
    public void Same_executable_in_another_session_is_not_owned()
    {
        var owned = new ErpManagedProcessIdentity(18124, _started, 1, Path);
        Assert.False(owned.Matches(18124, _started, 2, Path, 1));
        Assert.False(owned.Matches(18124, _started, 1, Path, 2));
    }

    [Fact]
    public void Reused_pid_or_different_executable_is_not_owned()
    {
        var owned = new ErpManagedProcessIdentity(18124, _started, 1, Path);
        Assert.False(owned.Matches(18124, _started.AddMinutes(1), 1, Path, 1));
        Assert.False(owned.Matches(18125, _started, 1, Path, 1));
        Assert.False(owned.Matches(18124, _started, 1, @"C:\Other\MainMenu.exe", 1));
    }

    [Fact]
    public void Existing_singleton_process_must_not_be_claimed_as_fresh_launch()
    {
        Assert.False(ErpManagedProcessIdentity.IsFreshLaunch(_started.AddMinutes(-10), _started));
        Assert.True(ErpManagedProcessIdentity.IsFreshLaunch(_started.AddSeconds(-1), _started));
        Assert.True(ErpManagedProcessIdentity.IsFreshLaunch(_started.AddSeconds(1), _started));
    }

    [Fact]
    public void Missing_path_fails_closed()
    {
        var owned = new ErpManagedProcessIdentity(18124, _started, 1, Path);
        Assert.False(owned.Matches(18124, _started, 1, string.Empty, 1));
        Assert.False(new ErpManagedProcessIdentity(18124, _started, 1, "").Matches(18124, _started, 1, Path, 1));
    }
}
