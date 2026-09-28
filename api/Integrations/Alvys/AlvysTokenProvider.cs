using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace Portfolio.Ltl.Api.Integrations.Alvys;

public sealed class AlvysTokenProvider(IHttpClientFactory clients, IOptions<AlvysOptions> options)
{
    public const string AuthClient = "alvys-auth";
    private readonly SemaphoreSlim gate = new(1, 1);
    private string? token;
    private DateTimeOffset expiresAt;

    public async Task<string> GetAsync(CancellationToken ct)
    {
        if (token is not null && expiresAt > DateTimeOffset.UtcNow.AddMinutes(2)) return token;
        await gate.WaitAsync(ct);
        try
        {
            if (token is not null && expiresAt > DateTimeOffset.UtcNow.AddMinutes(2)) return token;
            var cfg = options.Value;
            if (!cfg.LiveConfigured) throw new InvalidOperationException("Alvys live mode is not configured.");
            using var response = await clients.CreateClient(AuthClient).PostAsJsonAsync(cfg.TokenUrl, new
            {
                client_id = cfg.ClientId,
                client_secret = cfg.ClientSecret,
                audience = cfg.Audience,
                grant_type = "client_credentials"
            }, ct);
            response.EnsureSuccessStatusCode();
            var payload = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: ct)
                ?? throw new InvalidOperationException("Alvys token response was empty.");
            token = payload.access_token;
            var lifetimeSeconds = payload.expires_in > 0 ? payload.expires_in : 300;
            expiresAt = DateTimeOffset.UtcNow.AddSeconds(lifetimeSeconds);
            return token;
        }
        finally { gate.Release(); }
    }

    private sealed record TokenResponse(string access_token, int expires_in, string? token_type);
}
