using Portfolio.Ltl.Api.Integrations.Yard;

namespace Portfolio.Ltl.Api.Tests;

public sealed class SignatureTests
{
    [Fact]
    public void Signature_round_trip_is_valid_and_tamper_evident()
    {
        const string key = "portfolio-test-key";
        const string body = "{\"eventId\":\"11111111-1111-1111-1111-111111111111\"}";
        var signature = Signature.Create(body, key);

        Assert.True(Signature.Verify(body, key, signature));
        Assert.False(Signature.Verify(body + "x", key, signature));
    }

    [Fact]
    public void Timestamped_signature_matches_the_vector_shared_with_Yard()
    {
        const string body = "{\"eventId\":\"11111111-1111-1111-1111-111111111111\",\"eventType\":\"TrailerReadyForPlanning\"}";
        Assert.Equal("da4660db7d592f6bae2ea6d6005378be19c54fd3ae990b08368c721af8b1c3bc", Signature.CreateTimestamped(1790000000, body, "portfolio-test-key"));
    }

    [Fact]
    public void Timestamped_verification_enforces_the_window_and_the_exact_timestamp()
    {
        const string key = "portfolio-test-key";
        const string body = "{\"eventId\":\"11111111-1111-1111-1111-111111111111\"}";
        var signedAt = DateTimeOffset.FromUnixTimeSeconds(1_790_000_000);
        var signature = Signature.CreateTimestamped(1_790_000_000, body, key);

        Assert.True(Signature.VerifyTimestamped(body, key, signature, "1790000000", signedAt.AddMinutes(4)));
        Assert.False(Signature.VerifyTimestamped(body, key, signature, "1790000000", signedAt.AddMinutes(6)));
        Assert.False(Signature.VerifyTimestamped(body, key, signature, "1790000001", signedAt));
        Assert.False(Signature.VerifyTimestamped(body, key, signature, null, signedAt));
        Assert.False(Signature.VerifyTimestamped(body, key, signature, "-1", signedAt));
        Assert.False(Signature.VerifyTimestamped(body, key, signature, "99999999999999999999", signedAt));
    }
}
