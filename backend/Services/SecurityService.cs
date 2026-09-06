using System.Security.Cryptography;
using System.Text;
using Norgerman.Cryptography.Scrypt;

namespace Phonebook.Api.Services;

public sealed class SecurityService
{
    private const int ScryptN = 16384;
    private const int ScryptR = 8;
    private const int ScryptP = 1;
    private const int DigestLength = 64;

    public string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var digest = Derive(password, salt);
        return $"scrypt${Base64Url(salt)}${Base64Url(digest)}";
    }

    public bool VerifyPassword(string password, string storedHash)
    {
        var parts = storedHash.Split('$');
        if (parts.Length != 3 || parts[0] != "scrypt") return false;
        try
        {
            var salt = Base64UrlDecode(parts[1]);
            var expected = Base64UrlDecode(parts[2]);
            var actual = Derive(password, salt);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    public string CreateSessionToken() => Base64Url(RandomNumberGenerator.GetBytes(32));

    public string HashSessionToken(string token)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
    }

    private static byte[] Derive(string password, byte[] salt)
    {
        return ScryptUtil.Scrypt(Encoding.UTF8.GetBytes(password), salt, ScryptN, ScryptR, ScryptP, DigestLength);
    }

    private static string Base64Url(byte[] value) => Convert.ToBase64String(value)
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += new string('=', (4 - padded.Length % 4) % 4);
        return Convert.FromBase64String(padded);
    }
}
