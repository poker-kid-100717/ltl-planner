using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using Portfolio.Ltl.Api.Models;

namespace Portfolio.Ltl.Api.Integrations.Alvys;

public interface IExternalLoadReader
{
    Task<ExternalLoadResult> GetVisibleLoadsAsync(CancellationToken ct);
}

public sealed class AlvysLoadReader(
    IHttpClientFactory clients,
    IOptions<AlvysOptions> options,
    AlvysTokenProvider tokens,
    ILogger<AlvysLoadReader> logger) : IExternalLoadReader
{
    public const string ApiClient = "alvys-api";
    private readonly AlvysOptions cfg = options.Value;

    public async Task<ExternalLoadResult> GetVisibleLoadsAsync(CancellationToken ct)
    {
        if (!cfg.LiveConfigured) return Demo();
        try
        {
            var token = await tokens.GetAsync(ct);
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{cfg.ApiBaseUrl.TrimEnd('/')}/{cfg.LoadApiVersion}/loads/search");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Content = JsonContent.Create(new { Page = 0, PageSize = 25, Status = new[] { "Open", "Quoted", "Reserved", "Covered" }, IncludeDeleted = false });
            using var response = await clients.CreateClient(ApiClient).SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            var payload = await response.Content.ReadFromJsonAsync<SearchResponse>(cancellationToken: ct);
            var mapped = (payload?.Items ?? []).Select(x => new ExternalLoad(
                x.LoadNumber ?? x.Id ?? "unknown", x.CustomerName ?? "Customer not supplied", x.Status ?? "Unknown",
                x.ScheduledPickupAt, x.ScheduledDeliveryAt, x.RequiredEquipment ?? [], x.Weight?.Value, "Alvys Public API")).ToArray();
            return new("Alvys Public API", true, false, null, mapped);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            logger.LogWarning(ex, "Alvys LTL read degraded.");
            return Demo() with { Degraded = true, DegradedReason = "Live Alvys read unavailable; using synthetic portfolio freight." };
        }
    }

    private static ExternalLoadResult Demo() => new("Synthetic portfolio provider", false, false, null,
    [
        new("EXT-701", "Mesa Solar Components", "Open", DateTimeOffset.UtcNow.AddHours(4), DateTimeOffset.UtcNow.AddDays(1), ["Dry Van"], 16_500m, "Synthetic"),
        new("EXT-702", "High Desert Foods", "Quoted", DateTimeOffset.UtcNow.AddHours(7), DateTimeOffset.UtcNow.AddDays(2), ["Reefer"], 28_700m, "Synthetic")
    ]);

    private sealed record SearchResponse(int Page, int PageSize, int Total, List<LoadItem>? Items);
    private sealed record LoadItem(string? Id, string? LoadNumber, string? CustomerName, string? Status, DateTimeOffset? ScheduledPickupAt, DateTimeOffset? ScheduledDeliveryAt, List<string>? RequiredEquipment, Quantity? Weight);
    private sealed record Quantity(decimal? Value, string? UnitOfMeasure);
}
