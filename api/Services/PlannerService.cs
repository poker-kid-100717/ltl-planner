using Portfolio.Ltl.Api.Models;

namespace Portfolio.Ltl.Api.Services;

public sealed class PlannerService
{
    public PlanResult Build(IReadOnlyList<ShipmentOrder> orders, IReadOnlyList<TruckProfile> trucks)
    {
        var requestedOrders = orders
            .Where(x => x.Status == "Open")
            .OrderByDescending(x => x.Priority)
            .ThenByDescending(x => x.Pallets)
            .ThenByDescending(x => x.Weight)
            .ToList();
        var requestedTrucks = trucks.Where(x => x.Active).ToList();

        var mutable = requestedTrucks.ToDictionary(
            t => t.Id, t => new TruckBuild(t, [], 0, 0), StringComparer.OrdinalIgnoreCase);
        var unassigned = new List<UnassignedOrder>();

        foreach (var order in requestedOrders)
        {
            var best = mutable.Values.Where(x => Compatible(x, order))
                .OrderByDescending(x => Score(x, order))
                .ThenBy(x => x.Truck.Id, StringComparer.Ordinal)
                .FirstOrDefault();

            if (best is null)
            {
                unassigned.Add(new(order, ExplainUnassigned(order, mutable.Values)));
                continue;
            }

            best.Orders.Add(order);
            best.UsedPallets += order.Pallets;
            best.UsedWeight += order.Weight;
        }

        var planned = mutable.Values.Where(x => x.Orders.Count > 0)
            .Select(x => new PlannedTruck(
                x.Truck.Id, x.Truck.Equipment, x.Orders.ToArray(), x.UsedPallets, x.Truck.PalletCapacity,
                x.UsedWeight, x.Truck.WeightCapacity,
                Math.Round(Math.Max((decimal)x.UsedPallets / x.Truck.PalletCapacity,
                    (decimal)x.UsedWeight / x.Truck.WeightCapacity) * 100m, 1),
                Explain(x)))
            .OrderByDescending(x => x.Utilization).ToArray();

        return new(Guid.NewGuid(), DateTimeOffset.UtcNow, "Draft", planned, unassigned,
            "Explainable best-fit heuristic: equipment + hard pallet/weight constraints + origin affinity + remaining-capacity fit.");
    }

    private static bool Compatible(TruckBuild truck, ShipmentOrder order) =>
        truck.Truck.Equipment.Equals(order.Equipment, StringComparison.OrdinalIgnoreCase) &&
        truck.UsedPallets + order.Pallets <= truck.Truck.PalletCapacity &&
        truck.UsedWeight + order.Weight <= truck.Truck.WeightCapacity;

    private static string ExplainUnassigned(ShipmentOrder order, IEnumerable<TruckBuild> trucks)
    {
        var compatibleEquipment = trucks.Where(x => x.Truck.Equipment.Equals(order.Equipment, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (compatibleEquipment.Length == 0) return $"No active {order.Equipment} truck.";
        if (compatibleEquipment.All(x => x.UsedPallets + order.Pallets > x.Truck.PalletCapacity))
            return "Exceeds remaining pallet capacity on all compatible trucks.";
        if (compatibleEquipment.All(x => x.UsedWeight + order.Weight > x.Truck.WeightCapacity))
            return "Exceeds remaining weight capacity on all compatible trucks.";
        return "No compatible truck has enough remaining pallet and weight capacity.";
    }

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
        return [
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
