using CYInvoice.Core.Storage;

internal static class IdentityProviderFoundationTests
{
    public static async Task RunAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "CYInvoice.IdentityProvider.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            Throws<InvalidDataException>(() => AppRoles.Parse("EMPLOYEE"),
                "legacy EMPLOYEE role must not remain a compatibility alias");
            Equal(EmployeeRoles.User, AppRoles.ToValue(AppRole.User), "canonical USER role value");

            var protector = new TestProtector();
            var localStore = new EmployeeStore(directory);
            localStore.CreateFirstSuperAdmin("0001", "Local Admin", "local-admin@example.test", "LocalPass1");
            localStore.CreateEmployee("0001", "0002", "Local User", "local-user@example.test", "LocalPass2", EmployeeRoles.User);

            var local = new LocalIdentityProvider(localStore);
            Equal(IdentityProviderKind.Local, local.Kind, "local provider kind");
            True(local.OwnsAccountManagement, "Local provider must own CYInvoice account management");

            var localUser = await local.AuthenticateAsync(new IdentityAuthenticationRequest("0002", "LocalPass2"));
            NotNull(localUser, "valid Local USER authentication");
            Equal("0002", localUser!.StableEmployeeId, "Local stable employee ID");
            Equal(AppRole.User, localUser.Role, "Local USER normalization");
            True(!AppRoles.CanManageAccounts(localUser.Role), "USER must not gain manager authority");
            True(await local.AuthenticateAsync(new IdentityAuthenticationRequest("0002", "WrongPass2")) is null,
                "Local invalid credentials must be rejected");

            var settingsStore = new SettingsStore(directory, protector);
            var runtime = new IdentityProviderRuntime(settingsStore, local);
            Equal(IdentityProviderKind.Local, runtime.Current.Kind, "runtime starts with Local authority");

            var settings = settingsStore.LoadOrCreate();
            settings.CloudBaseUrl = "https://cloud.example.test/";
            settings.CloudWorkspaceId = "ws_identity_test";
            settings.CloudDeviceId = "dev_identity_test";
            settingsStore.SetCloudDeviceToken(settings,
                "cydev_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
            settings.CloudEmployeeAuthorityReady = true;
            settings.CloudMode = CloudModes.CloudPreferred;
            settingsStore.Save(settings);

            Throws<InvalidOperationException>(() => { _ = runtime.Current; },
                "Retired Built-in profiles must not construct authority or fall back to Local credentials");
            True(AppRoles.CanManageAccounts(AppRole.Admin), "ADMIN remains manager authority");
            True(AppRoles.CanManageAccounts(AppRole.SuperAdmin), "SUPER_ADMIN remains manager authority");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }

        await CloudDeviceLifecycleClientTests.RunAsync();
        await CloudDeviceSelfStatusClientTests.RunAsync();
        await LocalResetCoordinatorTests.RunAsync();
    }

    private static void Equal<T>(T expected, T actual, string description)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{description}: expected '{expected}', got '{actual}'");
    }

    private static void True(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
    }

    private static void NotNull(object? value, string description)
    {
        if (value is null) throw new InvalidOperationException(description);
    }

    private static void Throws<T>(Action action, string description) where T : Exception
    {
        try
        {
            action();
        }
        catch (T)
        {
            return;
        }
        throw new InvalidOperationException(description);
    }
}
