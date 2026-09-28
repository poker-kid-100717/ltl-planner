using Portfolio.Ltl.Api.Models;

namespace Portfolio.Ltl.Api.Services;

public sealed class PlannerService(LtlStore store)
{
    public PlanResult Build(IReadOnlyList<string>? orderIds = null, IReadOnlyList<string>? truckIds = null)
    {
        var requestedOrders = Select(store.Orders, orderIds, x => x.Id)
            .Where(x => !x.Assigned)
            .OrderByDescending(x => x.Priority)
            .ThenByDescending(x => x.Pallets)
            .ThenByDescending(x => x.Weight)
            .ToList();
        var requestedTrucks = Select(store.Trucks, truckIds, x => x.Id).ToList();

        var mutable = requestedTrucks.ToDictionary(
            t => t.Id,
            t => new TruckBuild(t, new List<ShipmentOrder>(), 0, 0));
        var unassigned = new List<ShipmentOrder>();

        foreach (var order in requestedOrders)
        {
            var best = mutable.Values
                .Where(x => Compatible(x, order))
                .OrderByDescending(x => Score(x, order))
                .ThenBy(x => x.Truck.Id, StringComparer.Ordinal)
                .FirstOrDefault();

            if (best is null)
            {
                unassigned.Add(order);
                continue;
            }

            best.Orders.Add(order);
            best.UsedPallets += order.Pallets;
            best.UsedWeight += order.Weight;
        }

        var trucks = mutable.Values
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
            .ToArray();

        return new(Guid.NewGuid(), DateTimeOffset.UtcNow, trucks, unassigned,
            "Explainable best-fit heuristic: equipment + hard pallet/weight constraints + origin affinity + remaining-capacity fit.");
    }

    private static IEnumerable<T> Select<T>(IReadOnlyList<T> values, IReadOnlyList<string>? ids, Func<T, string> id)
    {
        if (ids is null || ids.Count == 0) return values;
        var selected = ids.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return values.Where(x => selected.Contains(id(x)));
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

    private sealed class TruckBuild(TruckProfile truck, List<ShipmentOrder> orders, int usedPallets, int usedWeight)
    {
        public TruckProfile Truck { get; } = truck;
        public List<ShipmentOrder> Orders { get; } = orders;
        public int UsedPallets { get; set; } = usedPallets;
        public int UsedWeight { get; set; } = usedWeight;
    }
}
