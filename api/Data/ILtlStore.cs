using Portfolio.Ltl.Api.Models;

namespace Portfolio.Ltl.Api.Data;

public sealed record OrderQuery(string? Status, string? Equipment, string? Search, int Page, int PageSize);
public sealed record PlanSummary(Guid Id, DateTime CreatedAt, string Status, DateTime? DecidedAt, int TruckCount, int PlannedOrders, int UnassignedOrders);
public sealed record Paged<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);
public sealed record TransitionResult(bool Found, bool Moved, string? Status);
public sealed record DashboardData(IReadOnlyList<Order> Orders, IReadOnlyList<Truck> ActiveTrucks, IReadOnlyList<string> PlanStatuses, IReadOnlyList<string> TrailerStatuses);

/// <summary>Open freight on one origin -> destination lane, and the active trucks positioned to serve it.</summary>
public sealed record LaneSummary(string Origin, string Destination, int OpenOrders, int OpenPallets, int OpenWeight,
    IReadOnlyList<string> Equipment, IReadOnlyList<string> TrucksAtOrigin);

public enum PlanDecision { Done, NotFound, AlreadyDecided, Stale }
public sealed record PlanDecisionResult(PlanDecision Outcome, Plan? Plan = null, string? Detail = null);

/// <summary>
/// Everything the API reads and writes. Two implementations: Neo4j (the graph database used when
/// DATABASE_URL is set) and a throwaway SQLite demo store (EF Core) used otherwise.
/// </summary>
public interface ILtlStore
{
    /// <summary>Creates the schema (constraints or tables) and seeds demo data into an empty store.</summary>
    Task InitializeAsync(CancellationToken ct);

    /// <summary>Replaces planning data with the demo seed. Yard events are real integration history and are kept.</summary>
    Task ResetDemoAsync(CancellationToken ct);

    Task<Paged<Order>> ListOrdersAsync(OrderQuery query, CancellationToken ct);
    Task<Order?> GetOrderAsync(string id, CancellationToken ct);
    Task<IReadOnlyList<string>> OrderIdsAsync(CancellationToken ct);
    Task AddOrderAsync(Order order, CancellationToken ct);
    Task UpdateOrderAsync(Order order, CancellationToken ct);
    /// <summary>Moves an order from one status to another only if it is currently in <paramref name="from"/>.</summary>
    Task<TransitionResult> TransitionOrderAsync(string id, string from, string to, DateTime now, CancellationToken ct);

    Task<IReadOnlyList<Truck>> ListTrucksAsync(bool? active, CancellationToken ct);
    Task<Truck?> GetTruckAsync(string id, CancellationToken ct);
    Task<IReadOnlyList<string>> TruckIdsAsync(CancellationToken ct);
    Task AddTruckAsync(Truck truck, CancellationToken ct);
    Task UpdateTruckAsync(Truck truck, CancellationToken ct);

    /// <summary>Open orders and active trucks, optionally narrowed to the given ids.</summary>
    Task<(IReadOnlyList<Order> Orders, IReadOnlyList<Truck> Trucks)> PlanningInputAsync(
        IReadOnlyCollection<string>? orderIds, IReadOnlyCollection<string>? truckIds, CancellationToken ct);
    Task<Paged<PlanSummary>> ListPlansAsync(string? status, int page, int pageSize, CancellationToken ct);
    Task<Plan?> GetPlanAsync(Guid id, CancellationToken ct);
    Task AddPlanAsync(Plan plan, CancellationToken ct);
    /// <summary>Commits a draft atomically, or changes nothing if any order or truck changed since it was built.</summary>
    Task<PlanDecisionResult> CommitPlanAsync(Guid id, DateTime now, CancellationToken ct);
    Task<PlanDecisionResult> DiscardPlanAsync(Guid id, DateTime now, CancellationToken ct);

    /// <summary>Top open orders that fit a trailer, by priority.</summary>
    Task<IReadOnlyList<Order>> YardCandidatesAsync(int maxPallets, string? equipment, int take, CancellationToken ct);
    /// <summary>Stores the event once and updates the trailer view; false when the event id was already stored.</summary>
    Task<bool> AcceptYardEventAsync(YardEvent evt, string trailerStatus, CancellationToken ct);
    Task<IReadOnlyList<YardEvent>> RecentYardEventsAsync(int take, CancellationToken ct);
    Task<IReadOnlyList<YardTrailer>> YardTrailersAsync(CancellationToken ct);

    Task<DashboardData> DashboardAsync(CancellationToken ct);
    Task<IReadOnlyList<LaneSummary>> LanesAsync(CancellationToken ct);
}

/// <summary>Store-independent rules shared by both implementations.</summary>
public static class PlanRules
{
    /// <summary>
    /// Why a draft can no longer be committed: an order that is gone, no longer open, or edited, or a truck that
    /// is inactive or changed. Empty when the draft still matches the current data.
    /// </summary>
    public static List<string> FindStale(PlanResult result, IReadOnlyDictionary<string, Order> orders, IReadOnlyDictionary<string, Truck> trucks)
    {
        var stale = new List<string>();
        foreach (var truck in result.Trucks)
        foreach (var snapshot in truck.Orders)
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
        return stale;
    }

    public static IReadOnlyList<LaneSummary> Lanes(IEnumerable<Order> openOrders, IEnumerable<Truck> activeTrucks)
    {
        var trucks = activeTrucks.ToList();
        return openOrders
            .GroupBy(o => (o.Origin, o.Destination))
            .Select(g =>
            {
                var equipment = g.Select(o => o.Equipment).Distinct().Order(StringComparer.Ordinal).ToList();
                return new LaneSummary(g.Key.Origin, g.Key.Destination, g.Count(), g.Sum(o => o.Pallets), g.Sum(o => o.Weight), equipment,
                    trucks.Where(t => t.CurrentLocation == g.Key.Origin && equipment.Contains(t.Equipment))
                        .Select(t => t.Id).Order(StringComparer.Ordinal).ToList());
            })
            .OrderByDescending(l => l.OpenPallets).ThenBy(l => l.Origin, StringComparer.Ordinal).ThenBy(l => l.Destination, StringComparer.Ordinal)
            .ToList();
    }
}
