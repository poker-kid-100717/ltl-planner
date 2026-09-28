using System.Security.Cryptography;
using System.Text;

namespace Portfolio.Ltl.Api.Integrations.Yard;

public static class Signature
{
    public static string Create(string body, string key)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
    }

    public static bool Verify(string body, string key, string presented)
    {
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(presented)) return false;
        var expected = Encoding.UTF8.GetBytes(Create(body, key));
        var actual = Encoding.UTF8.GetBytes(presented.Trim().ToLowerInvariant());
        return expected.Length == actual.Length && CryptographicOperations.FixedTimeEquals(expected, actual);
    }
}
