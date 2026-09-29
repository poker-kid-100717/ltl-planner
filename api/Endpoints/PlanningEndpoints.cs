using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Portfolio.Ltl.Api.Data;
using Portfolio.Ltl.Api.Models;
using Portfolio.Ltl.Api.Services;

namespace Portfolio.Ltl.Api.Endpoints;

public sealed record OrderRequest(string? Customer, string? Origin, string? Destination, int Pallets, int Weight,
    string? Equipment, int Priority, DateOnly? ReadyOn);
public sealed record TruckRequest(string? Equipment, int PalletCapacity, int WeightCapacity, string? CurrentLocation, bool Active);
public sealed record PlanSummary(Guid Id, DateTime CreatedAt, string Status, DateTime? DecidedAt, int TruckCount, int PlannedOrders, int UnassignedOrders);

public static class PlanningEndpoints
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static void MapPlanningEndpoints(this RouteGroupBuilder api)
    {
        var orders = api.MapGroup("/orders").WithTags("Orders");
        orders.MapGet("/", ListOrders);
        orders.MapGet("/{id}", async (string id, LtlDbContext db, CancellationToken ct) =>
            await db.Orders.AsNoTracking().SingleOrDefaultAsync(o => o.Id == id, ct) is { } o ? Results.Ok(ToDto(o)) : Http.NotFound("Order"));
        orders.MapPost("/", CreateOrder);
        orders.MapPut("/{id}", UpdateOrder);
        orders.MapPost("/{id}/cancel", (string id, LtlDbContext db, TimeProvider clock, CancellationToken ct) =>
            Move(id, OrderStatuses.Open, OrderStatuses.Cancelled, db, clock, ct));
        orders.MapPost("/{id}/dispatch", (string id, LtlDbContext db, TimeProvider clock, CancellationToken ct) =>
            Move(id, OrderStatuses.Planned, OrderStatuses.Dispatched, db, clock, ct));

        var trucks = api.MapGroup("/trucks").WithTags("Trucks");
        trucks.MapGet("/", async (LtlDbContext db, bool? active, CancellationToken ct) =>
        {
            var query = db.Trucks.AsNoTracking();
            if (active is { } a) query = query.Where(t => t.Active == a);
            return Results.Ok((await query.OrderBy(t => t.Id).ToListAsync(ct)).Select(ToDto));
        });
        trucks.MapPost("/", CreateTruck);
        trucks.MapPut("/{id}", UpdateTruck);

        var plans = api.MapGroup("/plans").WithTags("Plans");
        plans.MapGet("/", ListPlans);
        plans.MapGet("/{id:guid}", GetPlan);
        plans.MapPost("/build", BuildPlan);
        plans.MapPost("/{id:guid}/commit", CommitPlan);
        plans.MapPost("/{id:guid}/discard", DiscardPlan);
    }

    internal static ShipmentOrder ToDto(Order o) => new(o.Id, o.Customer, o.Origin, o.Destination, o.Pallets, o.Weight, o.Equipment,
        o.Priority, o.Status != OrderStatuses.Open, o.Status, o.ReadyOn, o.PlanId, o.TruckId);

    internal static TruckProfile ToDto(Truck t) => new(t.Id, t.Equipment, t.PalletCapacity, t.WeightCapacity, t.CurrentLocation, t.Active);

    // ---------- orders ----------

    private static async Task<IResult> ListOrders(LtlDbContext db, string? status, string? equipment, string? search,
        int? page, int? pageSize, CancellationToken ct)
    {
        IQueryable<Order> query = db.Orders.AsNoTracking();
        if (Http.Clean(status) is { } s) query = query.Where(o => o.Status == s);
        if (Http.Clean(equipment) is { } e) query = query.Where(o => o.Equipment == e);
        if (Http.Clean(search) is { } term)
        {
            var t = term.ToLower();
            query = query.Where(o => o.Id.ToLower().Contains(t) || o.Customer.ToLower().Contains(t) ||
                                     o.Origin.ToLower().Contains(t) || o.Destination.ToLower().Contains(t));
        }
        var paged = await query.OrderByDescending(o => o.Priority).ThenBy(o => o.Id).ToPagedAsync(page, pageSize, ct);
        return Results.Ok(new Paged<ShipmentOrder>(paged.Items.Select(ToDto).ToList(), paged.Total, paged.Page, paged.PageSize));
    }

    private static Checks Validate(OrderRequest r)
    {
        var checks = new Checks()
            .Required("customer", r.Customer, Limits.Name)
            .Required("origin", r.Origin, Limits.Place)
            .Required("destination", r.Destination, Limits.Place)
            .Range("pallets", r.Pallets, 1, Limits.MaxPallets)
            .Range("weight", r.Weight, 1, Limits.MaxWeight)
            .OneOf("equipment", r.Equipment, EquipmentTypes.All)
            .Range("priority", r.Priority, 1, 100);
        if (r.ReadyOn is null) checks.Add("readyOn", "Required.");
        return checks;
    }

    private static void Apply(Order o, OrderRequest r, DateTime now)
    {
        o.Customer = r.Customer!.Trim();
        o.Origin = r.Origin!.Trim();
        o.Destination = r.Destination!.Trim();
        o.Pallets = r.Pallets;
        o.Weight = r.Weight;
        o.Equipment = r.Equipment!;
        o.Priority = r.Priority;
        o.ReadyOn = r.ReadyOn!.Value;
        o.UpdatedAt = now;
    }

    private static async Task<IResult> CreateOrder(OrderRequest request, LtlDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var checks = Validate(request);
        if (!checks.Ok) return checks.Problem();
        var ids = await db.Orders.Select(o => o.Id).ToListAsync(ct);
        var next = ids.Select(id => int.TryParse(id.AsSpan(4), out var n) ? n : 1000).DefaultIfEmpty(1000).Max() + 1;
        var now = clock.GetUtcNow().UtcDateTime;
        var order = new Order { Id = $"ORD-{next}", CreatedAt = now };
        Apply(order, request, now);
        db.Orders.Add(order);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/orders/{order.Id}", ToDto(order));
    }

    private static async Task<IResult> UpdateOrder(string id, OrderRequest request, LtlDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var checks = Validate(request);
        if (!checks.Ok) return checks.Problem();
        var order = await db.Orders.FindAsync([id], ct);
        if (order is null) return Http.NotFound("Order");
        if (order.Status != OrderStatuses.Open) return Http.Conflict($"{order.Id} is {order.Status}; only open orders can be edited.");
        Apply(order, request, clock.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> Move(string id, string from, string to, LtlDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var order = await db.Orders.FindAsync([id], ct);
        if (order is null) return Http.NotFound("Order");
        if (order.Status != from) return Http.Conflict($"{order.Id} is {order.Status}; only {from.ToLowerInvariant()} orders can be {to.ToLowerInvariant()}.");
        order.Status = to;
        order.UpdatedAt = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    // ---------- trucks ----------

    private static Checks Validate(TruckRequest r) => new Checks()
        .OneOf("equipment", r.Equipment, EquipmentTypes.All)
        .Range("palletCapacity", r.PalletCapacity, 1, Limits.MaxPallets)
        .Range("weightCapacity", r.WeightCapacity, 1_000, Limits.MaxWeight)
        .Required("currentLocation", r.CurrentLocation, Limits.Place);

    private static async Task<IResult> CreateTruck(TruckRequest request, LtlDbContext db, CancellationToken ct)
    {
        var checks = Validate(request);
        if (!checks.Ok) return checks.Problem();
        var prefix = request.Equipment switch { EquipmentTypes.Reefer => 3, EquipmentTypes.Flatbed => 4, _ => 2 };
        var ids = await db.Trucks.Select(t => t.Id).ToListAsync(ct);
        var next = ids.Select(id => int.TryParse(id.AsSpan(4), out var n) ? n : 0).Where(n => n / 100 == prefix)
            .DefaultIfEmpty(prefix * 100).Max() + 1;
        var truck = new Truck { Id = $"TRK-{next}" };
        Apply(truck, request);
        db.Trucks.Add(truck);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/trucks/{truck.Id}", ToDto(truck));
    }

    private static async Task<IResult> UpdateTruck(string id, TruckRequest request, LtlDbContext db, CancellationToken ct)
    {
        var checks = Validate(request);
        if (!checks.Ok) return checks.Problem();
        var truck = await db.Trucks.FindAsync([id], ct);
        if (truck is null) return Http.NotFound("Truck");
        Apply(truck, request);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static void Apply(Truck t, TruckRequest r)
    {
        t.Equipment = r.Equipment!;
        t.PalletCapacity = r.PalletCapacity;
        t.WeightCapacity = r.WeightCapacity;
        t.CurrentLocation = r.CurrentLocation!.Trim();
        t.Active = r.Active;
    }

    // ---------- plans ----------

    private static async Task<IResult> ListPlans(LtlDbContext db, string? status, int? page, int? pageSize, CancellationToken ct)
    {
        IQueryable<Plan> query = db.Plans.AsNoTracking();
        if (Http.Clean(status) is { } s) query = query.Where(p => p.Status == s);
        return Results.Ok(await query.OrderByDescending(p => p.CreatedAt)
            .Select(p => new PlanSummary(p.Id, p.CreatedAt, p.Status, p.DecidedAt, p.TruckCount, p.PlannedOrders, p.UnassignedOrders))
            .ToPagedAsync(page, pageSize, ct));
    }

    internal static PlanResult Read(Plan plan)
    {
        var result = JsonSerializer.Deserialize<PlanResult>(plan.ResultJson, Json)!;
        return result with
        {
            Status = plan.Status,
            DecidedAt = plan.DecidedAt is { } d ? new DateTimeOffset(DateTime.SpecifyKind(d, DateTimeKind.Utc)) : null
        };
    }

    private static async Task<IResult> GetPlan(Guid id, LtlDbContext db, CancellationToken ct) =>
        await db.Plans.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, ct) is { } plan ? Results.Ok(Read(plan)) : Http.NotFound("Plan");

    /// <summary>Plans open orders onto active trucks and saves the result as a draft to review.</summary>
    private static async Task<IResult> BuildPlan(PlanBuildRequest request, LtlDbContext db, PlannerService planner, CancellationToken ct)
    {
        IQueryable<Order> orders = db.Orders.AsNoTracking().Where(o => o.Status == OrderStatuses.Open);
        IQueryable<Truck> trucks = db.Trucks.AsNoTracking().Where(t => t.Active);
        if (request.OrderIds is { Count: > 0 } orderIds) orders = orders.Where(o => orderIds.Contains(o.Id));
        if (request.TruckIds is { Count: > 0 } truckIds) trucks = trucks.Where(t => truckIds.Contains(t.Id));

        var orderList = (await orders.ToListAsync(ct)).Select(ToDto).ToList();
        var truckList = (await trucks.ToListAsync(ct)).Select(ToDto).ToList();
        if (orderList.Count == 0)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["orderIds"] = ["There are no open orders to plan."] });

        var result = planner.Build(orderList, truckList);
        db.Plans.Add(new Plan
        {
            Id = result.Id, CreatedAt = result.CreatedAt.UtcDateTime, Status = PlanStatuses.Draft,
            TruckCount = result.Trucks.Count, PlannedOrders = result.Trucks.Sum(t => t.Orders.Count),
            UnassignedOrders = result.UnassignedOrders.Count, ResultJson = JsonSerializer.Serialize(result, Json)
        });
        await db.SaveChangesAsync(ct);
        return Results.Ok(result);
    }

    /// <summary>
    /// Commits a draft in one transaction. Every order must still be open and unchanged since the draft
    /// was built, and every truck still active with the same capacity; otherwise nothing changes (409).
    /// </summary>
    private static async Task<IResult> CommitPlan(Guid id, LtlDbContext db, TimeProvider clock, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var plan = await db.Plans.SingleOrDefaultAsync(p => p.Id == id, ct);
        if (plan is null) return Http.NotFound("Plan");
        if (plan.Status != PlanStatuses.Draft) return Http.Conflict($"This plan is already {plan.Status.ToLowerInvariant()}.");

        var result = Read(plan);
        var planned = result.Trucks.SelectMany(t => t.Orders.Select(o => (Truck: t, Order: o))).ToList();
        var orderIds = planned.Select(p => p.Order.Id).ToList();
        var truckIds = result.Trucks.Select(t => t.TruckId).ToList();
        var orders = await db.Orders.Where(o => orderIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, ct);
        var trucks = await db.Trucks.Where(t => truckIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, ct);

        var stale = new List<string>();
        foreach (var (truck, snapshot) in planned)
        {
            if (!orders.TryGetValue(snapshot.Id, out var current)) stale.Add($"{snapshot.Id} no longer exists");
            else if (current.Status != OrderStatuses.Open) stale.Add($"{snapshot.Id} is now {current.Status.ToLowerInvariant()}");
            else if (current.Pallets != snapshot.Pallets || current.Weight != snapshot.Weight || current.Equipment != snapshot.Equipment)
                stale.Add($"{snapshot.Id} was edited");
        }
        foreach (var truck in result.Trucks)
        {
            if (!trucks.TryGetValue(truck.TruckId, out var current) || !current.Active) stale.Add($"{truck.TruckId} is no longer active");
            else if (current.PalletCapacity != truck.PalletCapacity || current.WeightCapacity != truck.WeightCapacity || current.Equipment != truck.Equipment)
                stale.Add($"{truck.TruckId} was changed");
        }
        if (stale.Count > 0)
            return Http.Conflict($"The plan is out of date ({string.Join("; ", stale)}). Build a new plan.");

        var now = clock.GetUtcNow().UtcDateTime;
        foreach (var (truck, snapshot) in planned)
        {
            var order = orders[snapshot.Id];
            order.Status = OrderStatuses.Planned;
            order.PlanId = plan.Id;
            order.TruckId = truck.TruckId;
            order.UpdatedAt = now;
        }
        plan.Status = PlanStatuses.Committed;
        plan.DecidedAt = now;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Results.Ok(Read(plan));
    }

    private static async Task<IResult> DiscardPlan(Guid id, LtlDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var plan = await db.Plans.SingleOrDefaultAsync(p => p.Id == id, ct);
        if (plan is null) return Http.NotFound("Plan");
        if (plan.Status != PlanStatuses.Draft) return Http.Conflict($"This plan is already {plan.Status.ToLowerInvariant()}.");
        plan.Status = PlanStatuses.Discarded;
        plan.DecidedAt = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }
}
