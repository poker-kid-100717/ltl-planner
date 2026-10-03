using System.Text.Json;
using Portfolio.Ltl.Api.Data;
using Portfolio.Ltl.Api.Models;
using Portfolio.Ltl.Api.Services;

namespace Portfolio.Ltl.Api.Endpoints;

public sealed record OrderRequest(string? Customer, string? Origin, string? Destination, int Pallets, int Weight,
    string? Equipment, int Priority, DateOnly? ReadyOn);
public sealed record TruckRequest(string? Equipment, int PalletCapacity, int WeightCapacity, string? CurrentLocation, bool Active);

public static class PlanningEndpoints
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static void MapPlanningEndpoints(this RouteGroupBuilder api)
    {
        var orders = api.MapGroup("/orders").WithTags("Orders");
        orders.MapGet("/", ListOrders);
        orders.MapGet("/{id}", async (string id, ILtlStore store, CancellationToken ct) =>
            await store.GetOrderAsync(id, ct) is { } o ? Results.Ok(ToDto(o)) : Http.NotFound("Order"));
        orders.MapPost("/", CreateOrder);
        orders.MapPut("/{id}", UpdateOrder);
        orders.MapPost("/{id}/cancel", (string id, ILtlStore store, TimeProvider clock, CancellationToken ct) =>
            Move(id, OrderStatuses.Open, OrderStatuses.Cancelled, store, clock, ct));
        orders.MapPost("/{id}/dispatch", (string id, ILtlStore store, TimeProvider clock, CancellationToken ct) =>
            Move(id, OrderStatuses.Planned, OrderStatuses.Dispatched, store, clock, ct));

        var trucks = api.MapGroup("/trucks").WithTags("Trucks");
        trucks.MapGet("/", async (ILtlStore store, bool? active, CancellationToken ct) =>
            Results.Ok((await store.ListTrucksAsync(active, ct)).Select(ToDto)));
        trucks.MapPost("/", CreateTruck);
        trucks.MapPut("/{id}", UpdateTruck);

        var plans = api.MapGroup("/plans").WithTags("Plans");
        plans.MapGet("/", ListPlans);
        plans.MapGet("/{id:guid}", GetPlan);
        plans.MapPost("/build", BuildPlan);
        plans.MapPost("/{id:guid}/commit", CommitPlan);
        plans.MapPost("/{id:guid}/discard", DiscardPlan);

        api.MapGet("/lanes", async (ILtlStore store, CancellationToken ct) => Results.Ok(await store.LanesAsync(ct))).WithTags("Home");
    }

    internal static ShipmentOrder ToDto(Order o) => new(o.Id, o.Customer, o.Origin, o.Destination, o.Pallets, o.Weight, o.Equipment,
        o.Priority, o.Status != OrderStatuses.Open, o.Status, o.ReadyOn, o.PlanId, o.TruckId);

    internal static TruckProfile ToDto(Truck t) => new(t.Id, t.Equipment, t.PalletCapacity, t.WeightCapacity, t.CurrentLocation, t.Active);

    // ---------- orders ----------

    private static async Task<IResult> ListOrders(ILtlStore store, string? status, string? equipment, string? search,
        int? page, int? pageSize, CancellationToken ct)
    {
        var (p, size) = Paging.Normalize(page, pageSize);
        var paged = await store.ListOrdersAsync(new OrderQuery(Http.Clean(status), Http.Clean(equipment), Http.Clean(search), p, size), ct);
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

    private static async Task<IResult> CreateOrder(OrderRequest request, ILtlStore store, TimeProvider clock, CancellationToken ct)
    {
        var checks = Validate(request);
        if (!checks.Ok) return checks.Problem();
        var ids = await store.OrderIdsAsync(ct);
        var next = ids.Select(id => int.TryParse(id.AsSpan(4), out var n) ? n : 1000).DefaultIfEmpty(1000).Max() + 1;
        var now = clock.GetUtcNow().UtcDateTime;
        var order = new Order { Id = $"ORD-{next}", CreatedAt = now };
        Apply(order, request, now);
        await store.AddOrderAsync(order, ct);
        return Results.Created($"/api/orders/{order.Id}", ToDto(order));
    }

    private static async Task<IResult> UpdateOrder(string id, OrderRequest request, ILtlStore store, TimeProvider clock, CancellationToken ct)
    {
        var checks = Validate(request);
        if (!checks.Ok) return checks.Problem();
        var order = await store.GetOrderAsync(id, ct);
        if (order is null) return Http.NotFound("Order");
        if (order.Status != OrderStatuses.Open) return Http.Conflict($"{order.Id} is {order.Status}; only open orders can be edited.");
        Apply(order, request, clock.GetUtcNow().UtcDateTime);
        await store.UpdateOrderAsync(order, ct);
        return Results.NoContent();
    }

    private static async Task<IResult> Move(string id, string from, string to, ILtlStore store, TimeProvider clock, CancellationToken ct)
    {
        var moved = await store.TransitionOrderAsync(id, from, to, clock.GetUtcNow().UtcDateTime, ct);
        if (!moved.Found) return Http.NotFound("Order");
        if (!moved.Moved) return Http.Conflict($"{id} is {moved.Status}; only {from.ToLowerInvariant()} orders can be {to.ToLowerInvariant()}.");
        return Results.NoContent();
    }

    // ---------- trucks ----------

    private static Checks Validate(TruckRequest r) => new Checks()
        .OneOf("equipment", r.Equipment, EquipmentTypes.All)
        .Range("palletCapacity", r.PalletCapacity, 1, Limits.MaxPallets)
        .Range("weightCapacity", r.WeightCapacity, 1_000, Limits.MaxWeight)
        .Required("currentLocation", r.CurrentLocation, Limits.Place);

    private static async Task<IResult> CreateTruck(TruckRequest request, ILtlStore store, CancellationToken ct)
    {
        var checks = Validate(request);
        if (!checks.Ok) return checks.Problem();
        var prefix = request.Equipment switch { EquipmentTypes.Reefer => 3, EquipmentTypes.Flatbed => 4, _ => 2 };
        var ids = await store.TruckIdsAsync(ct);
        var next = ids.Select(id => int.TryParse(id.AsSpan(4), out var n) ? n : 0).Where(n => n / 100 == prefix)
            .DefaultIfEmpty(prefix * 100).Max() + 1;
        var truck = new Truck { Id = $"TRK-{next}" };
        Apply(truck, request);
        await store.AddTruckAsync(truck, ct);
        return Results.Created($"/api/trucks/{truck.Id}", ToDto(truck));
    }

    private static async Task<IResult> UpdateTruck(string id, TruckRequest request, ILtlStore store, CancellationToken ct)
    {
        var checks = Validate(request);
        if (!checks.Ok) return checks.Problem();
        var truck = await store.GetTruckAsync(id, ct);
        if (truck is null) return Http.NotFound("Truck");
        Apply(truck, request);
        await store.UpdateTruckAsync(truck, ct);
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

    private static async Task<IResult> ListPlans(ILtlStore store, string? status, int? page, int? pageSize, CancellationToken ct)
    {
        var (p, size) = Paging.Normalize(page, pageSize);
        return Results.Ok(await store.ListPlansAsync(Http.Clean(status), p, size, ct));
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

    private static async Task<IResult> GetPlan(Guid id, ILtlStore store, CancellationToken ct) =>
        await store.GetPlanAsync(id, ct) is { } plan ? Results.Ok(Read(plan)) : Http.NotFound("Plan");

    /// <summary>Plans open orders onto active trucks and saves the result as a draft to review.</summary>
    private static async Task<IResult> BuildPlan(PlanBuildRequest request, ILtlStore store, PlannerService planner, CancellationToken ct)
    {
        var (orders, trucks) = await store.PlanningInputAsync(request.OrderIds, request.TruckIds, ct);
        if (orders.Count == 0)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["orderIds"] = ["There are no open orders to plan."] });

        var result = planner.Build(orders.Select(ToDto).ToList(), trucks.Select(ToDto).ToList());
        await store.AddPlanAsync(new Plan
        {
            Id = result.Id, CreatedAt = result.CreatedAt.UtcDateTime, Status = PlanStatuses.Draft,
            TruckCount = result.Trucks.Count, PlannedOrders = result.Trucks.Sum(t => t.Orders.Count),
            UnassignedOrders = result.UnassignedOrders.Count, ResultJson = JsonSerializer.Serialize(result, Json)
        }, ct);
        return Results.Ok(result);
    }

    /// <summary>
    /// Commits a draft in one transaction. Every order must still be open and unchanged since the draft
    /// was built, and every truck still active with the same capacity; otherwise nothing changes (409).
    /// </summary>
    private static async Task<IResult> CommitPlan(Guid id, ILtlStore store, TimeProvider clock, CancellationToken ct) =>
        await store.CommitPlanAsync(id, clock.GetUtcNow().UtcDateTime, ct) switch
        {
            { Outcome: PlanDecision.NotFound } => Http.NotFound("Plan"),
            { Outcome: PlanDecision.AlreadyDecided, Plan: { } p } => Http.Conflict($"This plan is already {p.Status.ToLowerInvariant()}."),
            { Outcome: PlanDecision.Stale, Detail: var detail } => Http.Conflict($"The plan is out of date ({detail}). Build a new plan."),
            { Plan: { } p } => Results.Ok(Read(p)),
            _ => throw new InvalidOperationException("Unexpected commit result.")
        };

    private static async Task<IResult> DiscardPlan(Guid id, ILtlStore store, TimeProvider clock, CancellationToken ct) =>
        await store.DiscardPlanAsync(id, clock.GetUtcNow().UtcDateTime, ct) switch
        {
            { Outcome: PlanDecision.NotFound } => Http.NotFound("Plan"),
            { Outcome: PlanDecision.AlreadyDecided, Plan: { } p } => Http.Conflict($"This plan is already {p.Status.ToLowerInvariant()}."),
            _ => Results.NoContent()
        };
}
