using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Portfolio.Ltl.Api.Integrations.Yard;

public static class Signature
{
    /// <summary>Unix seconds at signing; requests outside <see cref="MaxSkew"/> are rejected as stale or replayed.</summary>
    public const string TimestampHeader = "X-Portfolio-Timestamp";
    public static readonly TimeSpan MaxSkew = TimeSpan.FromMinutes(5);

    public static string CreateTimestamped(long unixSeconds, string body, string key) => Create(unixSeconds.ToString(CultureInfo.InvariantCulture) + "." + body, key);

    /// <summary>
    /// Verifies a signature over "{timestamp}.{body}" and that the timestamp is within five minutes of now.
    /// Together with eventId idempotency this makes a captured request useless for replay.
    /// </summary>
    public static bool VerifyTimestamped(string body, string key, string presented, string? timestamp, DateTimeOffset now)
    {
        if (!long.TryParse(timestamp, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)) return false;
        if (seconds > DateTimeOffset.MaxValue.ToUnixTimeSeconds()) return false;
        if ((now - DateTimeOffset.FromUnixTimeSeconds(seconds)).Duration() > MaxSkew) return false;
        return Verify($"{timestamp}.{body}", key, presented);
    }

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
