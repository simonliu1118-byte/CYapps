using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace CYInvoice.Core.Cloud;

public static class CloudEmployeeCredentialVerifier
{
    private const int PasswordIterations = 210_000;
    private const string PasswordAlgorithm = "pbkdf2-sha256";

    public static string Create(string password)
    {
        ArgumentNullException.ThrowIfNull(password);
        if (password.Length < 8 || !password.All(char.IsAsciiLetterOrDigit))
            throw new InvalidOperationException("密碼至少 8 碼，且只能使用英文字母或數字");

        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            PasswordIterations,
            HashAlgorithmName.SHA256,
            32);
        return string.Join(
            '$',
            PasswordAlgorithm,
            PasswordIterations.ToString(CultureInfo.InvariantCulture),
            Convert.ToHexString(salt).ToLowerInvariant(),
            Convert.ToHexString(hash).ToLowerInvariant());
    }
}
