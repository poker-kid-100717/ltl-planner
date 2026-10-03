using Microsoft.EntityFrameworkCore;
using Portfolio.Ltl.Api.Endpoints;

namespace Portfolio.Ltl.Api.Data;

/// <summary>The no-configuration demo store: a throwaway SQLite file rebuilt from the EF Core model on every start.</summary>
public sealed class SqliteLtlStore(LtlDbContext db, DemoSeeder seeder) : ILtlStore
{
    public async Task InitializeAsync(CancellationToken ct)
    {
        await db.Database.EnsureDeletedAsync(ct);
        await db.Database.EnsureCreatedAsync(ct);
        if (!await db.Trucks.AnyAsync(ct)) await SeedAsync(ct);
    }

    public async Task ResetDemoAsync(CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Plans.ExecuteDeleteAsync(ct);
        await db.Orders.ExecuteDeleteAsync(ct);
        await db.Trucks.ExecuteDeleteAsync(ct);
        db.Trucks.AddRange(seeder.Trucks());
        db.Orders.AddRange(seeder.Orders());
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        db.ChangeTracker.Clear();
    }

    private async Task SeedAsync(CancellationToken ct)
    {
        db.Trucks.AddRange(seeder.Trucks());
        db.Orders.AddRange(seeder.Orders());
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
    }

    // ---------- orders ----------

    public async Task<Paged<Order>> ListOrdersAsync(OrderQuery q, CancellationToken ct)
    {
        IQueryable<Order> query = db.Orders.AsNoTracking();
        if (q.Status is { } s) query = query.Where(o => o.Status == s);
        if (q.Equipment is { } e) query = query.Where(o => o.Equipment == e);
        if (q.Search is { } term)
        {
            var t = term.ToLower();
            query = query.Where(o => o.Id.ToLower().Contains(t) || o.Customer.ToLower().Contains(t) ||
                                     o.Origin.ToLower().Contains(t) || o.Destination.ToLower().Contains(t));
        }
        return await query.OrderByDescending(o => o.Priority).ThenBy(o => o.Id).ToPagedAsync(q.Page, q.PageSize, ct);
    }

    public Task<Order?> GetOrderAsync(string id, CancellationToken ct) =>
        db.Orders.AsNoTracking().SingleOrDefaultAsync(o => o.Id == id, ct);

    public async Task<IReadOnlyList<string>> OrderIdsAsync(CancellationToken ct) => await db.Orders.Select(o => o.Id).ToListAsync(ct);

    public async Task AddOrderAsync(Order order, CancellationToken ct)
    {
        db.Orders.Add(order);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateOrderAsync(Order order, CancellationToken ct)
    {
        db.Orders.Update(order);
        await db.SaveChangesAsync(ct);
    }

    public async Task<TransitionResult> TransitionOrderAsync(string id, string from, string to, DateTime now, CancellationToken ct)
    {
        var order = await db.Orders.FindAsync([id], ct);
        if (order is null) return new TransitionResult(false, false, null);
        if (order.Status != from) return new TransitionResult(true, false, order.Status);
        order.Status = to;
        order.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return new TransitionResult(true, true, to);
    }

    // ---------- trucks ----------

    public async Task<IReadOnlyList<Truck>> ListTrucksAsync(bool? active, CancellationToken ct)
    {
        var query = db.Trucks.AsNoTracking();
        if (active is { } a) query = query.Where(t => t.Active == a);
        return await query.OrderBy(t => t.Id).ToListAsync(ct);
    }

    public Task<Truck?> GetTruckAsync(string id, CancellationToken ct) =>
        db.Trucks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id, ct);

    public async Task<IReadOnlyList<string>> TruckIdsAsync(CancellationToken ct) => await db.Trucks.Select(t => t.Id).ToListAsync(ct);

    public async Task AddTruckAsync(Truck truck, CancellationToken ct)
    {
        db.Trucks.Add(truck);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateTruckAsync(Truck truck, CancellationToken ct)
    {
        db.Trucks.Update(truck);
        await db.SaveChangesAsync(ct);
    }

    // ---------- plans ----------

    public async Task<(IReadOnlyList<Order> Orders, IReadOnlyList<Truck> Trucks)> PlanningInputAsync(
        IReadOnlyCollection<string>? orderIds, IReadOnlyCollection<string>? truckIds, CancellationToken ct)
    {
        IQueryable<Order> orders = db.Orders.AsNoTracking().Where(o => o.Status == OrderStatuses.Open);
        IQueryable<Truck> trucks = db.Trucks.AsNoTracking().Where(t => t.Active);
        if (orderIds is { Count: > 0 }) orders = orders.Where(o => orderIds.Contains(o.Id));
        if (truckIds is { Count: > 0 }) trucks = trucks.Where(t => truckIds.Contains(t.Id));
        return (await orders.ToListAsync(ct), await trucks.ToListAsync(ct));
    }

    public async Task<Paged<PlanSummary>> ListPlansAsync(string? status, int page, int pageSize, CancellationToken ct)
    {
        IQueryable<Plan> query = db.Plans.AsNoTracking();
        if (status is { } s) query = query.Where(p => p.Status == s);
        return await query.OrderByDescending(p => p.CreatedAt)
            .Select(p => new PlanSummary(p.Id, p.CreatedAt, p.Status, p.DecidedAt, p.TruckCount, p.PlannedOrders, p.UnassignedOrders))
            .ToPagedAsync(page, pageSize, ct);
    }

    public Task<Plan?> GetPlanAsync(Guid id, CancellationToken ct) => db.Plans.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, ct);

    public async Task AddPlanAsync(Plan plan, CancellationToken ct)
    {
        db.Plans.Add(plan);
        await db.SaveChangesAsync(ct);
    }

    public async Task<PlanDecisionResult> CommitPlanAsync(Guid id, DateTime now, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var plan = await db.Plans.SingleOrDefaultAsync(p => p.Id == id, ct);
        if (plan is null) return new PlanDecisionResult(PlanDecision.NotFound);
        if (plan.Status != PlanStatuses.Draft) return new PlanDecisionResult(PlanDecision.AlreadyDecided, plan);

        var result = PlanningEndpoints.Read(plan);
        var orderIds = result.Trucks.SelectMany(t => t.Orders.Select(o => o.Id)).ToList();
        var truckIds = result.Trucks.Select(t => t.TruckId).ToList();
        var orders = await db.Orders.Where(o => orderIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, ct);
        var trucks = await db.Trucks.Where(t => truckIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, ct);

        var stale = PlanRules.FindStale(result, orders, trucks);
        if (stale.Count > 0) return new PlanDecisionResult(PlanDecision.Stale, plan, string.Join("; ", stale));

        foreach (var truck in result.Trucks)
        foreach (var snapshot in truck.Orders)
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
        return new PlanDecisionResult(PlanDecision.Done, plan);
    }

    public async Task<PlanDecisionResult> DiscardPlanAsync(Guid id, DateTime now, CancellationToken ct)
    {
        var plan = await db.Plans.SingleOrDefaultAsync(p => p.Id == id, ct);
        if (plan is null) return new PlanDecisionResult(PlanDecision.NotFound);
        if (plan.Status != PlanStatuses.Draft) return new PlanDecisionResult(PlanDecision.AlreadyDecided, plan);
        plan.Status = PlanStatuses.Discarded;
        plan.DecidedAt = now;
        await db.SaveChangesAsync(ct);
        return new PlanDecisionResult(PlanDecision.Done, plan);
    }

    // ---------- yard ----------

    public async Task<IReadOnlyList<Order>> YardCandidatesAsync(int maxPallets, string? equipment, int take, CancellationToken ct)
    {
        var query = db.Orders.AsNoTracking().Where(o => o.Status == OrderStatuses.Open && o.Pallets <= maxPallets);
        if (equipment is not null)
        {
            var e = equipment.ToLower();
            query = query.Where(o => o.Equipment.ToLower() == e);
        }
        return await query.OrderByDescending(o => o.Priority).ThenByDescending(o => o.Pallets).ThenBy(o => o.Id).Take(take).ToListAsync(ct);
    }

    public async Task<bool> AcceptYardEventAsync(YardEvent evt, string trailerStatus, CancellationToken ct)
    {
        if (await db.YardEvents.AnyAsync(e => e.EventId == evt.EventId, ct)) return false;

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        db.YardEvents.Add(evt);
        var trailer = await db.YardTrailers.FindAsync([evt.TrailerNumber], ct);
        if (trailer is null)
        {
            trailer = new YardTrailer { TrailerNumber = evt.TrailerNumber };
            db.YardTrailers.Add(trailer);
        }
        if (trailer.LastEventType.Length == 0 || evt.OccurredAt >= trailer.LastEventAt)
        {
            trailer.Status = trailerStatus;
            trailer.LastEventType = evt.EventType;
            trailer.LastEventAt = evt.OccurredAt;
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

    public async Task<IReadOnlyList<YardEvent>> RecentYardEventsAsync(int take, CancellationToken ct) =>
        await db.YardEvents.AsNoTracking().OrderByDescending(e => e.OccurredAt).Take(take).ToListAsync(ct);

    public async Task<IReadOnlyList<YardTrailer>> YardTrailersAsync(CancellationToken ct) =>
        await db.YardTrailers.AsNoTracking().OrderByDescending(t => t.LastEventAt).ToListAsync(ct);

    // ---------- overview ----------

    public async Task<DashboardData> DashboardAsync(CancellationToken ct) => new(
        await db.Orders.AsNoTracking().ToListAsync(ct),
        await db.Trucks.AsNoTracking().Where(t => t.Active).ToListAsync(ct),
        await db.Plans.AsNoTracking().Select(p => p.Status).ToListAsync(ct),
        await db.YardTrailers.AsNoTracking().Select(t => t.Status).ToListAsync(ct));

    public async Task<IReadOnlyList<LaneSummary>> LanesAsync(CancellationToken ct) => PlanRules.Lanes(
        await db.Orders.AsNoTracking().Where(o => o.Status == OrderStatuses.Open).ToListAsync(ct),
        await db.Trucks.AsNoTracking().Where(t => t.Active).ToListAsync(ct));
}
