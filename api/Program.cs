using System.Text.Json;
using Microsoft.Extensions.Http.Resilience;
using Portfolio.Ltl.Api.Integrations.Alvys;
using Portfolio.Ltl.Api.Integrations.Yard;
using Portfolio.Ltl.Api.Models;
using Portfolio.Ltl.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.Configure<AlvysOptions>(builder.Configuration.GetSection(AlvysOptions.Section));
builder.Services.AddSingleton<LtlStore>();
builder.Services.AddSingleton<PlannerService>();
builder.Services.AddSingleton<YardEventInbox>();
builder.Services.AddSingleton<AlvysTokenProvider>();
builder.Services.AddSingleton<IExternalLoadReader, AlvysLoadReader>();

builder.Services.AddHttpClient(AlvysTokenProvider.AuthClient)
    .AddStandardResilienceHandler(options =>
    {
        options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(8);
        options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(20);
    });

builder.Services.AddHttpClient(AlvysLoadReader.ApiClient)
    .AddStandardResilienceHandler(options =>
    {
        options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(12);
        options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(30);
    });

var app = builder.Build();
app.UseExceptionHandler();
if (app.Environment.IsDevelopment()) app.MapOpenApi();

app.MapGet("/health", () => Results.Ok(new { status = "Healthy", service = "ltl-planner" }));
app.MapGet("/api/orders", (LtlStore store) => Results.Ok(store.Orders));
app.MapGet("/api/trucks", (LtlStore store) => Results.Ok(store.Trucks));
app.MapGet("/api/plans", (LtlStore store) => Results.Ok(store.Plans.OrderByDescending(x => x.CreatedAt)));

app.MapPost("/api/plans/build", (PlanBuildRequest request, PlannerService planner, LtlStore store) =>
{
    var plan = planner.Build(request.OrderIds, request.TruckIds);
    store.AddPlan(plan);
    return Results.Ok(plan);
});

app.MapGet("/api/integrations/v1/yard/candidates", (
    string trailerNumber,
    string? equipment,
    int? maxPallets,
    LtlStore store) =>
{
    var capacity = Math.Clamp(maxPallets ?? 26, 1, 60);
    var items = store.Orders
        .Where(o => !o.Assigned)
        .Where(o => string.IsNullOrWhiteSpace(equipment) || o.Equipment.Equals(equipment, StringComparison.OrdinalIgnoreCase))
        .Where(o => o.Pallets <= capacity)
        .OrderByDescending(o => o.Priority)
        .ThenByDescending(o => o.Pallets)
        .Take(8)
        .Select(o => new YardCandidate(o.Id, o.Customer, o.Origin, o.Destination, o.Pallets, o.Weight, o.Equipment,
            $"Fits {trailerNumber} by declared equipment and pallet capacity; final truck/route validation stays in LTL."));
    return Results.Ok(items);
});

app.MapPost("/api/integrations/v1/yard/events", async (
    HttpRequest request,
    IConfiguration configuration,
    YardEventInbox inbox,
    CancellationToken ct) =>
{
    using var reader = new StreamReader(request.Body);
    var body = await reader.ReadToEndAsync();
    var signature = request.Headers["X-Portfolio-Signature"].ToString();
    var key = configuration["Integration:YardSigningKey"] ?? "";

    if (!Signature.Verify(body, key, signature))
        return Results.Unauthorized();

    var evt = JsonSerializer.Deserialize<YardIntegrationEvent>(body, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    if (evt is null || evt.EventId == Guid.Empty || string.IsNullOrWhiteSpace(evt.TrailerNumber))
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["event"] = ["A valid eventId and trailerNumber are required."] });
    if (evt.SchemaVersion != 1)
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["schemaVersion"] = ["Only Yard integration schema version 1 is supported."] });

    var accepted = inbox.TryAccept(evt);
    return Results.Ok(new { accepted, duplicate = !accepted, evt.EventId });
});

app.MapGet("/api/integrations/v1/yard/events", (YardEventInbox inbox) =>
    Results.Ok(inbox.Events.OrderByDescending(x => x.OccurredAt)));

app.MapGet("/api/alvys/loads", async (IExternalLoadReader loads, CancellationToken ct) =>
    Results.Ok(await loads.GetVisibleLoadsAsync(ct)));

app.Run();
