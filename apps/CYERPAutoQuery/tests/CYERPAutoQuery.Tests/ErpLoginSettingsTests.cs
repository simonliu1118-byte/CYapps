using Xunit;

namespace CYERPAutoQuery.Tests;

/// <summary>ERP 自動登入與閒置關閉 settings (user, 2026-10-19): forms after 10 minutes, ERP after 30.</summary>
public class ErpLoginSettingsTests
{
    private static readonly DateTime Now = new(2026, 10, 19, 17, 0, 0);

    [Fact]
    public void DefaultsCloseFormsAfterTenAndErpAfterThirtyMinutes()
    {
        var settings = new ErpLoginSettings();
        Assert.Equal((10, 30, "志遠醫療器材行"), (settings.FormIdleMinutes, settings.IdleCloseMinutes, settings.Company));
        Assert.False(settings.FormsIdleDue(Now.AddMinutes(-9), Now));
        Assert.True(settings.FormsIdleDue(Now.AddMinutes(-10), Now));
        Assert.False(settings.IdleDue(Now.AddMinutes(-29), Now));
        Assert.True(settings.IdleDue(Now.AddMinutes(-30), Now));
    }

    [Fact]
    public void ZeroMinutesNeverCloses()
    {
        var settings = new ErpLoginSettings { FormIdleMinutes = 0, IdleCloseMinutes = 0 };
        Assert.False(settings.FormsIdleDue(Now.AddDays(-3), Now));
        Assert.False(settings.IdleDue(Now.AddDays(-3), Now));
    }

    [Fact]
    public void AutoLoginNeedsProgramAccountPasswordAndCompany()
    {
        var settings = new ErpLoginSettings { ErpPath = @"C:\ERP\fake.exe", Account = "user", ProtectedPassword = "protected" };
        Assert.True(settings.CanAutoLogin);
        Assert.False(new ErpLoginSettings { Account = "user", ProtectedPassword = "p" }.CanAutoLogin);
        Assert.False(new ErpLoginSettings { ErpPath = "x.exe", ProtectedPassword = "p" }.CanAutoLogin);
        Assert.False(new ErpLoginSettings { ErpPath = "x.exe", Account = "user" }.CanAutoLogin);
        Assert.False(new ErpLoginSettings { ErpPath = "x.exe", Account = "user", ProtectedPassword = "p", Company = " " }.CanAutoLogin);
    }
}
