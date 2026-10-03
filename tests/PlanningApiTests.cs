using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Portfolio.Ltl.Api.Integrations.Yard;

namespace Portfolio.Ltl.Api.Tests;

public sealed class PlanningApiTests(ApiFactory factory) : ApiTest(factory)
{
    private static object NewOrder(string customer = "Portfolio Test Co", int pallets = 4, string equipment = "Dry Van") => new
    {
        customer, origin = "Albuquerque, NM", destination = "Phoenix, AZ", pallets, weight = 3000, equipment, priority = 50,
        readyOn = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd")
    };

    [Fact]
    public async Task OrdersCanBeCreatedEditedAndCancelled()
    {
        var created = await PostJson("/api/orders", NewOrder());
        var id = created.GetProperty("id").GetString()!;
        Assert.Equal("ORD-1015", id);

        var edit = await Client.PutAsJsonAsync($"/api/orders/{id}", NewOrder(pallets: 6), ApiFactory.Json);
        Assert.Equal(HttpStatusCode.NoContent, edit.StatusCode);
        Assert.Equal(6, (await GetJson($"/api/orders/{id}")).GetProperty("pallets").GetInt32());

        Assert.Equal(HttpStatusCode.NoContent, (await Post($"/api/orders/{id}/cancel")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Post($"/api/orders/{id}/cancel")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Client.PutAsJsonAsync($"/api/orders/{id}", NewOrder(), ApiFactory.Json)).StatusCode);
    }

    [Fact]
    public async Task InvalidOrdersReturnFieldErrors()
    {
        var problem = await PostJson("/api/orders", new { customer = "", pallets = 40, weight = 0, equipment = "Boat", priority = 0 }, 400);
        foreach (var field in new[] { "customer", "pallets", "weight", "equipment", "priority", "readyOn" })
            Assert.True(problem.GetProperty("errors").TryGetProperty(field, out _), field);
    }

    [Fact]
    public async Task BuildSavesADraftWithReasonsForUnplacedOrders()
    {
        var plan = await PostJson("/api/plans/build", new { orderIds = Array.Empty<string>(), truckIds = Array.Empty<string>() }, 200);

        Assert.Equal("Draft", plan.GetProperty("status").GetString());
        Assert.True(plan.GetProperty("trucks").GetArrayLength() >= 3);
        Assert.All(plan.GetProperty("unassigned").EnumerateArray(), u => Assert.False(string.IsNullOrWhiteSpace(u.GetProperty("reason").GetString())));
        Assert.Contains(plan.GetProperty("unassigned").EnumerateArray(), u => u.GetProperty("order").GetProperty("equipment").GetString() == "Reefer");

        var history = await GetJson("/api/plans");
        Assert.Equal(1, history.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task CommitPlansOrdersAtomicallyAndOnlyOnce()
    {
        var plan = await PostJson("/api/plans/build", new { orderIds = new[] { "ORD-1001", "ORD-1002" } }, 200);
        var id = plan.GetProperty("id").GetGuid();

        var committed = await PostJson($"/api/plans/{id}/commit", new { }, 200);
        Assert.Equal("Committed", committed.GetProperty("status").GetString());
        var order = await GetJson("/api/orders/ORD-1001");
        Assert.Equal("Planned", order.GetProperty("status").GetString());
        Assert.True(order.GetProperty("assigned").GetBoolean());
        Assert.False(string.IsNullOrEmpty(order.GetProperty("truckId").GetString()));

        Assert.Equal(HttpStatusCode.Conflict, (await Post($"/api/plans/{id}/commit")).StatusCode);

        // Planned orders can be dispatched but no longer cancelled.
        Assert.Equal(HttpStatusCode.Conflict, (await Post("/api/orders/ORD-1001/cancel")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Post("/api/orders/ORD-1001/dispatch")).StatusCode);
    }

    [Fact]
    public async Task CommittingAStaleDraftChangesNothing()
    {
        var plan = await PostJson("/api/plans/build", new { orderIds = new[] { "ORD-1001", "ORD-1005" } }, 200);
        var edit = await Client.PutAsJsonAsync("/api/orders/ORD-1005", new
        {
            customer = "Rio Valley Medical Supply", origin = "Las Cruces, NM", destination = "Phoenix, AZ", pallets = 9, weight = 4100,
            equipment = "Dry Van", priority = 88, readyOn = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd")
        }, ApiFactory.Json);
        Assert.Equal(HttpStatusCode.NoContent, edit.StatusCode);

        var response = await Post($"/api/plans/{plan.GetProperty("id").GetGuid()}/commit");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("ORD-1005 was edited", await response.Content.ReadAsStringAsync());
        Assert.Equal("Open", (await GetJson("/api/orders/ORD-1001")).GetProperty("status").GetString());
    }

    [Fact]
    public async Task DraftsCanBeDiscarded()
    {
        var plan = await PostJson("/api/plans/build", new { }, 200);
        var id = plan.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NoContent, (await Post($"/api/plans/{id}/discard")).StatusCode);
        Assert.Equal("Discarded", (await GetJson($"/api/plans/{id}")).GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await Post($"/api/plans/{id}/commit")).StatusCode);
    }

    [Fact]
    public async Task TrucksCanBeAddedAndDeactivated()
    {
        var created = await PostJson("/api/trucks", new { equipment = "Reefer", palletCapacity = 24, weightCapacity = 42000, currentLocation = "Denver, CO", active = true });
        var id = created.GetProperty("id").GetString()!;
        Assert.Equal("TRK-303", id);
        var off = await Client.PutAsJsonAsync($"/api/trucks/{id}", new { equipment = "Reefer", palletCapacity = 24, weightCapacity = 42000, currentLocation = "Denver, CO", active = false }, ApiFactory.Json);
        Assert.Equal(HttpStatusCode.NoContent, off.StatusCode);
        var active = await GetJson("/api/trucks?active=true");
        Assert.DoesNotContain(active.EnumerateArray(), t => t.GetProperty("id").GetString() == id);
    }

    [Fact]
    public async Task DashboardMetaAndHealth()
    {
        var dashboard = await GetJson("/api/dashboard");
        Assert.Equal(14, dashboard.GetProperty("openOrders").GetInt32());
        Assert.Equal(5, dashboard.GetProperty("activeTrucks").GetInt32());
        Assert.Equal(3, dashboard.GetProperty("byEquipment").GetArrayLength());

        var meta = await GetJson("/api/meta");
        Assert.True(meta.GetProperty("storage").GetProperty("ready").GetBoolean());
        Assert.True(meta.GetProperty("yardIntegration").GetProperty("signingConfigured").GetBoolean());
        Assert.Equal("Ready", (await GetJson("/health/ready")).GetProperty("status").GetString());
    }

    [Fact]
    public async Task LanesShowOpenFreightAndTheTrucksPositionedToServeIt()
    {
        var lanes = await GetJson("/api/lanes");
        var denver = lanes.EnumerateArray().Single(l =>
            l.GetProperty("origin").GetString() == "Albuquerque, NM" && l.GetProperty("destination").GetString() == "Denver, CO");
        Assert.Equal(3, denver.GetProperty("openOrders").GetInt32());
        Assert.Equal(24, denver.GetProperty("openPallets").GetInt32());
        Assert.Equal(["Dry Van", "Reefer"], denver.GetProperty("equipment").EnumerateArray().Select(e => e.GetString()));
        // TRK-302 (Reefer) is in Albuquerque but out of service; TRK-401 is there but a flatbed.
        Assert.Equal(["TRK-201", "TRK-202"], denver.GetProperty("trucksAtOrigin").EnumerateArray().Select(e => e.GetString()));

        // Lanes only count open freight.
        await Post("/api/orders/ORD-1011/cancel");
        var after = (await GetJson("/api/lanes")).EnumerateArray().Single(l =>
            l.GetProperty("origin").GetString() == "Albuquerque, NM" && l.GetProperty("destination").GetString() == "Denver, CO");
        Assert.Equal(2, after.GetProperty("openOrders").GetInt32());
    }

    [Fact]
    public async Task DemoResetRequiresTheToken()
    {
        await PostJson("/api/orders", NewOrder("Temporary Co"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post("/api/admin/reset-demo")).StatusCode);
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/admin/reset-demo");
        request.Headers.Add("X-Demo-Reset-Token", "test-reset-token");
        Assert.Equal(HttpStatusCode.OK, (await Client.SendAsync(request)).StatusCode);
        Assert.Equal(14, (await GetJson("/api/orders?pageSize=100")).GetProperty("total").GetInt32());
    }
}

/// <summary>The v1 contract Yard Ops depends on: same routes, header, validation and response shapes.</summary>
public sealed class YardContractTests(ApiFactory factory) : ApiTest(factory)
{
    private static string Event(Guid id, string type = "TrailerReadyForPlanning", string trailer = "TRL-4207", int schemaVersion = 1, DateTimeOffset? at = null) =>
        JsonSerializer.Serialize(new { eventId = id, eventType = type, trailerNumber = trailer, occurredAt = at ?? DateTimeOffset.UtcNow, details = "Inspection passed.", schemaVersion });

    private Task<HttpResponseMessage> Send(string body, string? signature = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/integrations/v1/yard/events")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-Portfolio-Signature", signature ?? Signature.Create(body, ApiFactory.SigningKey));
        return Client.SendAsync(request);
    }

    [Fact]
    public async Task CandidatesKeepTheirShapeAndOnlyIncludeOpenOrders()
    {
        var before = await GetJson("/api/integrations/v1/yard/candidates?trailerNumber=TRL-4101&equipment=Dry%20Van&maxPallets=26");
        var first = before[0];
        foreach (var field in new[] { "orderId", "customer", "origin", "destination", "pallets", "weight", "equipment", "reason" })
            Assert.True(first.TryGetProperty(field, out _), field);
        Assert.All(before.EnumerateArray(), c => Assert.Equal("Dry Van", c.GetProperty("equipment").GetString()));
        Assert.Contains("TRL-4101", first.GetProperty("reason").GetString());

        var plan = await PostJson("/api/plans/build", new { orderIds = new[] { first.GetProperty("orderId").GetString() } }, 200);
        await PostJson($"/api/plans/{plan.GetProperty("id").GetGuid()}/commit", new { }, 200);

        var after = await GetJson("/api/integrations/v1/yard/candidates?trailerNumber=TRL-4101&equipment=dry%20van");
        Assert.DoesNotContain(after.EnumerateArray(), c => c.GetProperty("orderId").GetString() == first.GetProperty("orderId").GetString());
    }

    [Fact]
    public async Task SignedEventsAreStoredOnceAndUpdateTheTrailerView()
    {
        var id = Guid.NewGuid();
        var body = Event(id);

        var first = await (await Send(body)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(first.GetProperty("accepted").GetBoolean());
        Assert.False(first.GetProperty("duplicate").GetBoolean());
        Assert.Equal(id, first.GetProperty("eventId").GetGuid());

        var second = await (await Send(body)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(second.GetProperty("accepted").GetBoolean());
        Assert.True(second.GetProperty("duplicate").GetBoolean());

        var events = await GetJson("/api/integrations/v1/yard/events");
        Assert.Single(events.EnumerateArray(), e => e.GetProperty("eventId").GetGuid() == id);

        var trailers = await GetJson("/api/yard/trailers");
        Assert.Contains(trailers.EnumerateArray(), t => t.GetProperty("trailerNumber").GetString() == "TRL-4207" && t.GetProperty("status").GetString() == "Ready for loading");
    }

    [Fact]
    public async Task LateEventsDoNotOverwriteNewerTrailerStatus()
    {
        var now = DateTimeOffset.UtcNow;
        await Send(Event(Guid.NewGuid(), "TrailerGateOut", "TRL-9001", at: now));
        await Send(Event(Guid.NewGuid(), "TrailerGateIn", "TRL-9001", at: now.AddHours(-2)));

        var trailers = await GetJson("/api/yard/trailers");
        Assert.Equal("Departed", trailers.EnumerateArray().Single(t => t.GetProperty("trailerNumber").GetString() == "TRL-9001").GetProperty("status").GetString());
    }

    [Fact]
    public async Task UnsignedOrTamperedEventsAreRejected()
    {
        var body = Event(Guid.NewGuid());
        Assert.Equal(HttpStatusCode.Unauthorized, (await Send(body, "not-a-signature")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Send(body + " ", Signature.Create(body, ApiFactory.SigningKey))).StatusCode);
    }

    [Fact]
    public async Task UnknownSchemaVersionsAndMissingFieldsAreRejected()
    {
        var v2 = await Send(Event(Guid.NewGuid(), schemaVersion: 2));
        Assert.Equal(HttpStatusCode.BadRequest, v2.StatusCode);
        Assert.Contains("schemaVersion", await v2.Content.ReadAsStringAsync());

        var empty = await Send(Event(Guid.Empty));
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
    }
}

public sealed class RateLimitedFactory : ApiFactory
{
    protected override int WritesPerMinute => 2;
    protected override string? ResetToken => null;
}

public sealed class RateLimitTests(RateLimitedFactory factory) : IClassFixture<RateLimitedFactory>
{
    [Fact]
    public async Task UserWritesAreLimitedButYardIntegrationIsNot()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("CF-Connecting-IP", "203.0.113.9");
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++) statuses.Add((await client.PostAsync("/api/admin/reset-demo", null)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[2]);

        // Signed service-to-service calls are not counted against the demo limit.
        var body = JsonSerializer.Serialize(new { eventId = Guid.NewGuid(), eventType = "TrailerGateIn", trailerNumber = "TRL-1", occurredAt = DateTimeOffset.UtcNow, schemaVersion = 1 });
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/integrations/v1/yard/events") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        request.Headers.Add("X-Portfolio-Signature", Signature.Create(body, ApiFactory.SigningKey));
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(request)).StatusCode);
    }
}
