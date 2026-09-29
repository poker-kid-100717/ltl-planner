using Portfolio.Ltl.Api.Models;

namespace Portfolio.Ltl.Api.Services;

/// <summary>
/// Deterministic, explainable best-fit planner. Hard constraints (equipment, pallets, weight) filter
/// trucks before any scoring; every planned truck and every unplaced order carries a plain-language reason.
/// </summary>
public sealed class PlannerService(TimeProvider clock)
{
    public const string Algorithm =
        "Explainable best-fit heuristic: equipment + hard pallet/weight constraints + origin affinity + remaining-capacity fit.";

    public PlanResult Build(IReadOnlyList<ShipmentOrder> orders, IReadOnlyList<TruckProfile> trucks)
    {
        var queue = orders
            .OrderByDescending(x => x.Priority)
            .ThenByDescending(x => x.Pallets)
            .ThenByDescending(x => x.Weight)
            .ThenBy(x => x.Id, StringComparer.Ordinal)
            .ToList();

        var builds = trucks.Where(t => t.Active).OrderBy(t => t.Id, StringComparer.Ordinal).Select(t => new TruckBuild(t)).ToList();
        var unassigned = new List<UnassignedOrder>();

        foreach (var order in queue)
        {
            var best = builds
                .Where(x => Compatible(x, order))
                .OrderByDescending(x => Score(x, order))
                .ThenBy(x => x.Truck.Id, StringComparer.Ordinal)
                .FirstOrDefault();

            if (best is null)
            {
                unassigned.Add(new UnassignedOrder(order, WhyUnassigned(order, builds)));
                continue;
            }

            best.Orders.Add(order);
            best.UsedPallets += order.Pallets;
            best.UsedWeight += order.Weight;
        }

        var planned = builds
            .Where(x => x.Orders.Count > 0)
            .Select(x => new PlannedTruck(
                x.Truck.Id,
                x.Truck.Equipment,
                x.Orders.ToArray(),
                x.UsedPallets,
                x.Truck.PalletCapacity,
                x.UsedWeight,
                x.Truck.WeightCapacity,
                Math.Round(Math.Max(
                    (decimal)x.UsedPallets / x.Truck.PalletCapacity,
                    (decimal)x.UsedWeight / x.Truck.WeightCapacity) * 100m, 1),
                Explain(x)))
            .OrderByDescending(x => x.Utilization)
            .ThenBy(x => x.TruckId, StringComparer.Ordinal)
            .ToArray();

        return new(Guid.NewGuid(), clock.GetUtcNow(), planned, unassigned.Select(u => u.Order).ToArray(), Algorithm, unassigned);
    }

    private static bool Compatible(TruckBuild truck, ShipmentOrder order) =>
        truck.Truck.Equipment.Equals(order.Equipment, StringComparison.OrdinalIgnoreCase) &&
        truck.UsedPallets + order.Pallets <= truck.Truck.PalletCapacity &&
        truck.UsedWeight + order.Weight <= truck.Truck.WeightCapacity;

    private static decimal Score(TruckBuild truck, ShipmentOrder order)
    {
        var remainingPalletsAfter = truck.Truck.PalletCapacity - truck.UsedPallets - order.Pallets;
        var remainingWeightAfter = truck.Truck.WeightCapacity - truck.UsedWeight - order.Weight;
        var originAffinity = order.Origin.Equals(truck.Truck.CurrentLocation, StringComparison.OrdinalIgnoreCase) ? 20m : 0m;
        var palletFit = 20m - Math.Min(Math.Abs(remainingPalletsAfter), 20);
        var weightFit = 20m - Math.Min(Math.Abs(remainingWeightAfter) / 2000m, 20m);
        return order.Priority + originAffinity + palletFit + weightFit;
    }

    private static string WhyUnassigned(ShipmentOrder order, IReadOnlyList<TruckBuild> builds)
    {
        var sameEquipment = builds.Where(b => b.Truck.Equipment.Equals(order.Equipment, StringComparison.OrdinalIgnoreCase)).ToList();
        if (sameEquipment.Count == 0)
            return $"No active {order.Equipment} truck was selected for this plan.";

        var largest = sameEquipment.OrderByDescending(b => b.Truck.PalletCapacity).ThenByDescending(b => b.Truck.WeightCapacity).First().Truck;
        if (sameEquipment.All(b => order.Pallets > b.Truck.PalletCapacity || order.Weight > b.Truck.WeightCapacity))
            return $"Too large for any {order.Equipment} truck: needs {order.Pallets} pallets / {order.Weight:n0} lb; " +
                   $"the largest takes {largest.PalletCapacity} / {largest.WeightCapacity:n0} lb.";

        return $"Every {order.Equipment} truck was already full with higher-priority orders " +
               $"(needs {order.Pallets} pallets / {order.Weight:n0} lb).";
    }

    private static IReadOnlyList<string> Explain(TruckBuild build)
    {
        var origins = build.Orders.Select(x => x.Origin).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return
        [
            $"Equipment match: all {build.Orders.Count} orders require {build.Truck.Equipment}.",
            $"Pallet use: {build.UsedPallets}/{build.Truck.PalletCapacity}; weight: {build.UsedWeight:n0}/{build.Truck.WeightCapacity:n0} lb.",
            origins.Length == 1 && origins[0].Equals(build.Truck.CurrentLocation, StringComparison.OrdinalIgnoreCase)
                ? "Origin affinity: truck starts in the same market as all planned freight."
                : "Origin affinity is mixed; the heuristic favored capacity fit after hard constraints."
        ];
    }

    private sealed class TruckBuild(TruckProfile truck)
    {
        public TruckProfile Truck { get; } = truck;
        public List<ShipmentOrder> Orders { get; } = [];
        public int UsedPallets { get; set; }
        public int UsedWeight { get; set; }
    }
}
