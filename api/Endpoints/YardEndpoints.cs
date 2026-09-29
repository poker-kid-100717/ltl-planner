using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Portfolio.Ltl.Api.Data;
using Portfolio.Ltl.Api.Integrations.Alvys;
using Portfolio.Ltl.Api.Integrations.Yard;
using Portfolio.Ltl.Api.Models;

namespace Portfolio.Ltl.Api.Endpoints;

public sealed record YardTrailerDto(string TrailerNumber, string Status, string LastEventType, DateTime LastEventAt);

public static class YardEndpoints
{
    /// <summary>
    /// The contract Yard Ops depends on. Paths, query parameters, the signature header, validation messages
    /// and response shapes are unchanged from v1; only the storage behind them is new.
    /// </summary>
    public static void MapYardContract(this RouteGroupBuilder api)
    {
        var yard = api.MapGroup("/integrations/v1/yard").WithTags("Yard integration v1");

        yard.MapGet("/candidates", async (string trailerNumber, string? equipment, int? maxPallets, LtlDbContext db, CancellationToken ct) =>
        {
            var capacity = Math.Clamp(maxPallets ?? 26, 1, 60);
            var query = db.Orders.AsNoTracking().Where(o => o.Status == OrderStatuses.Open && o.Pallets <= capacity);
            if (!string.IsNullOrWhiteSpace(equipment))
            {
                var e = equipment.Trim().ToLower();
                query = query.Where(o => o.Equipment.ToLower() == e);
            }
            var orders = await query.OrderByDescending(o => o.Priority).ThenByDescending(o => o.Pallets).ThenBy(o => o.Id).Take(8).ToListAsync(ct);
            return Results.Ok(orders.Select(o => new YardCandidate(o.Id, o.Customer, o.Origin, o.Destination, o.Pallets, o.Weight, o.Equipment,
                $"Fits {trailerNumber} by declared equipment and pallet capacity; final truck/route validation stays in LTL.")));
        });

        yard.MapPost("/events", async (HttpRequest request, IConfiguration configuration, LtlDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            using var reader = new StreamReader(request.Body);
            var body = await reader.ReadToEndAsync(ct);
            var signature = request.Headers["X-Portfolio-Signature"].ToString();
            var key = configuration["Integration:YardSigningKey"] ?? "";

            if (!Signature.Verify(body, key, signature))
                return Results.Unauthorized();

            YardIntegrationEvent? evt;
            try { evt = JsonSerializer.Deserialize<YardIntegrationEvent>(body, PlanningEndpoints.Json); }
            catch (JsonException) { evt = null; }
            if (evt is null || evt.EventId == Guid.Empty || string.IsNullOrWhiteSpace(evt.TrailerNumber))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["event"] = ["A valid eventId and trailerNumber are required."] });
            if (evt.SchemaVersion != 1)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["schemaVersion"] = ["Only Yard integration schema version 1 is supported."] });

            var accepted = await AcceptAsync(evt, db, clock, ct);
            return Results.Ok(new { accepted, duplicate = !accepted, evt.EventId });
        });

        yard.MapGet("/events", async (LtlDbContext db, CancellationToken ct) =>
            Results.Ok((await db.YardEvents.AsNoTracking().OrderByDescending(e => e.OccurredAt).Take(100).ToListAsync(ct))
                .Select(e => new YardIntegrationEvent(e.EventId, e.EventType, e.TrailerNumber,
                    new DateTimeOffset(DateTime.SpecifyKind(e.OccurredAt, DateTimeKind.Utc)), e.Details, e.SchemaVersion))));
    }

    /// <summary>
    /// Stores the event once (the event id is the primary key) and updates the trailer view in the same
    /// transaction. Yard's outbox retries can deliver events late, so an older event never overwrites a newer status.
    /// </summary>
    internal static async Task<bool> AcceptAsync(YardIntegrationEvent evt, LtlDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (await db.YardEvents.AnyAsync(e => e.EventId == evt.EventId, ct)) return false;

        var occurredAt = evt.OccurredAt.UtcDateTime;
        var trailerNumber = evt.TrailerNumber.Trim();
        if (trailerNumber.Length > 40) trailerNumber = trailerNumber[..40];
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        db.YardEvents.Add(new YardEvent
        {
            EventId = evt.EventId, EventType = Truncate(evt.EventType, 60), TrailerNumber = trailerNumber, OccurredAt = occurredAt,
            Details = evt.Details is null ? null : Truncate(evt.Details, 1000), SchemaVersion = evt.SchemaVersion,
            ReceivedAt = clock.GetUtcNow().UtcDateTime
        });

        var trailer = await db.YardTrailers.FindAsync([trailerNumber], ct);
        if (trailer is null)
        {
            trailer = new YardTrailer { TrailerNumber = trailerNumber };
            db.YardTrailers.Add(trailer);
        }
        if (trailer.LastEventType.Length == 0 || occurredAt >= trailer.LastEventAt)
        {
            trailer.Status = StatusFor(evt.EventType);
            trailer.LastEventType = Truncate(evt.EventType, 60);
            trailer.LastEventAt = occurredAt;
        }

        try
        {
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return true;
        }
        catch (DbUpdateException)
        {
            // Two deliveries of the same event raced; the other one won.
            return false;
        }
    }

    public static string StatusFor(string eventType) => eventType switch
    {
        "TrailerGateIn" => "On yard",
        "TrailerGateOut" => "Departed",
        "TrailerReadyForPlanning" => "Ready for loading",
        _ => eventType
    };

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];

    // ---------- read models for the UI ----------

    public static void MapYardViews(this RouteGroupBuilder api)
    {
        api.MapGet("/yard/trailers", async (LtlDbContext db, CancellationToken ct) =>
            Results.Ok(await db.YardTrailers.AsNoTracking().OrderByDescending(t => t.LastEventAt)
                .Select(t => new YardTrailerDto(t.TrailerNumber, t.Status, t.LastEventType, t.LastEventAt)).ToListAsync(ct)))
            .WithTags("Yard");

        api.MapGet("/dashboard", async (LtlDbContext db, CancellationToken ct) =>
        {
            var orders = await db.Orders.AsNoTracking().Select(o => new { o.Status, o.Equipment, o.Pallets, o.Weight }).ToListAsync(ct);
            var trucks = await db.Trucks.AsNoTracking().Where(t => t.Active).ToListAsync(ct);
            var plans = await db.Plans.AsNoTracking().Select(p => p.Status).ToListAsync(ct);
            var trailers = await db.YardTrailers.AsNoTracking().Select(t => t.Status).ToListAsync(ct);
            var open = orders.Where(o => o.Status == OrderStatuses.Open).ToList();
            return Results.Ok(new
            {
                openOrders = open.Count,
                openPallets = open.Sum(o => o.Pallets),
                openWeight = open.Sum(o => o.Weight),
                plannedOrders = orders.Count(o => o.Status == OrderStatuses.Planned),
                dispatchedOrders = orders.Count(o => o.Status == OrderStatuses.Dispatched),
                activeTrucks = trucks.Count,
                fleetPallets = trucks.Sum(t => t.PalletCapacity),
                draftPlans = plans.Count(s => s == PlanStatuses.Draft),
                committedPlans = plans.Count(s => s == PlanStatuses.Committed),
                trailersOnYard = trailers.Count(s => s != "Departed"),
                trailersReady = trailers.Count(s => s == "Ready for loading"),
                byEquipment = EquipmentTypes.All.Select(e => new
                {
                    equipment = e,
                    openOrders = open.Count(o => o.Equipment == e),
                    openPallets = open.Where(o => o.Equipment == e).Sum(o => o.Pallets),
                    trucks = trucks.Count(t => t.Equipment == e),
                    palletCapacity = trucks.Where(t => t.Equipment == e).Sum(t => t.PalletCapacity)
                })
            });
        }).WithTags("Home");
    }

    // ---------- platform ----------

    public static void MapPlatformEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/meta", (DatabaseStatus database, IOptions<AlvysOptions> alvys, IConfiguration config) => Results.Ok(new
        {
            equipmentTypes = EquipmentTypes.All,
            orderStatuses = OrderStatuses.All,
            planStatuses = PlanStatuses.All,
            storage = new { mode = database.Mode, persistent = database.Persistent, ready = database.Ready },
            demoReset = new { scheduled = !string.IsNullOrEmpty(ResetToken(config)), schedule = "Daily at 08:23 UTC" },
            yardIntegration = new { signingConfigured = !string.IsNullOrEmpty(config["Integration:YardSigningKey"]) },
            integration = new { provider = "Alvys Public API", mode = alvys.Value.LiveConfigured ? "Live" : "Demo", configured = alvys.Value.LiveConfigured }
        })).WithTags("Settings");

        api.MapPost("/admin/reset-demo", async (HttpRequest request, IConfiguration config, LtlDbContext db, DemoSeeder seeder,
            DatabaseGate gate, CancellationToken ct) =>
        {
            var expected = ResetToken(config);
            if (string.IsNullOrEmpty(expected)) return Results.NotFound();
            var supplied = request.Headers["X-Demo-Reset-Token"].ToString();
            if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(supplied), Encoding.UTF8.GetBytes(expected)))
                return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Invalid reset token.");
            if (!await gate.EnsureReadyAsync(ct))
                return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "The database is unavailable.");
            await seeder.ResetAsync(db, ct);
            return Results.Ok(new { reset = true });
        }).ExcludeFromDescription();
    }

    private static string? ResetToken(IConfiguration config) => config["Demo:ResetToken"] ?? config["DEMO_RESET_TOKEN"];
}
