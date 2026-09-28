using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CYInvoice.Core.Storage;

internal static class IdentityProviderFoundationTests
{
    public static async Task RunAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "CYInvoice.IdentityProvider.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
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

            var cloudCache = new CloudEmployeeCacheStore(directory, protector);
            cloudCache.ReplaceSnapshot(
                "ws_identity_test",
                7,
                new[]
                {
                    Seed("emp_super", "0001", "Cloud Admin", "cloud-admin@example.test", EmployeeRoles.SuperAdmin, "CloudPass1", 4, 11),
                    Seed("emp_user", "0002", "Cloud User", "cloud-user@example.test", EmployeeRoles.User, "CloudPass2", 2, 8),
                    Seed("emp_disabled", "0003", "Disabled User", "disabled@example.test", EmployeeRoles.User, "CloudPass3", 1, 3, enabled: false),
                });

            var cloud = new BuiltInCloudIdentityProvider(cloudCache);
            Equal(IdentityProviderKind.BuiltInCloud, cloud.Kind, "Built-in Cloud provider kind");
            True(cloud.OwnsAccountManagement, "Built-in Cloud provider must own CYInvoice account management");

            var cloudUser = await cloud.AuthenticateAsync(new IdentityAuthenticationRequest("0002", "CloudPass2"));
            NotNull(cloudUser, "valid Built-in Cloud USER authentication");
            Equal("emp_user", cloudUser!.StableEmployeeId, "Cloud stable employee ID");
            Equal(AppRole.User, cloudUser.Role, "Cloud USER normalization");
            Equal(8, cloudUser.AuthorityRevision, "Cloud authority revision");
            True(await cloud.AuthenticateAsync(new IdentityAuthenticationRequest("0002", "WrongPass2")) is null,
                "Cloud invalid credentials must be rejected");
            True(await cloud.AuthenticateAsync(new IdentityAuthenticationRequest("0003", "CloudPass3")) is null,
                "disabled Cloud authority must be rejected");
            var disabledPrincipal = await cloud.RefreshPrincipalAsync("0003");
            NotNull(disabledPrincipal, "refresh should still expose disabled authority metadata");
            True(!disabledPrincipal!.Enabled, "refreshed disabled authority must stay disabled");

            var settingsStore = new SettingsStore(directory, protector);
            var runtime = new IdentityProviderRuntime(settingsStore, local, cloud);
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

            Equal(IdentityProviderKind.BuiltInCloud, runtime.Current.Kind,
                "runtime switches centrally to Built-in Cloud authority");
            True(AppRoles.CanManageAccounts(AppRole.Admin), "ADMIN remains manager authority");
            True(AppRoles.CanManageAccounts(AppRole.SuperAdmin), "SUPER_ADMIN remains manager authority");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static CloudEmployeeCacheSeed Seed(
        string employeeId,
        string employeeNo,
        string name,
        string email,
        string role,
        string password,
        int credentialVersion,
        int revision,
        bool enabled = true) =>
        new(
            employeeId,
            employeeNo,
            name,
            email,
            role,
            enabled,
            EmailVerified: true,
            Verifier(password),
            credentialVersion,
            revision);

    private static string Verifier(string password)
    {
        var salt = Enumerable.Range(1, 16).Select(value => (byte)value).ToArray();
        const int iterations = 100_000;
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            32);
        return string.Join(
            '$',
            "pbkdf2-sha256",
            iterations.ToString(CultureInfo.InvariantCulture),
            Convert.ToHexString(salt).ToLowerInvariant(),
            Convert.ToHexString(hash).ToLowerInvariant());
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
}
