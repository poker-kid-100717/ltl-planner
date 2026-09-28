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
}
