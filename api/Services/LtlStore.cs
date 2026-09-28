using Portfolio.Ltl.Api.Models;

namespace Portfolio.Ltl.Api.Services;

public sealed class LtlStore
{
    private readonly object sync = new();
    private readonly List<PlanResult> plans = [];

    public IReadOnlyList<ShipmentOrder> Orders { get; } =
    [
        new("ORD-1001", "Mesa Solar Components", "Albuquerque, NM", "Phoenix, AZ", 8, 7_200, "Dry Van", 92),
        new("ORD-1002", "Canyon Packaging", "Albuquerque, NM", "Phoenix, AZ", 6, 5_800, "Dry Van", 84),
        new("ORD-1003", "Sandstone Home Goods", "Albuquerque, NM", "Tucson, AZ", 10, 9_400, "Dry Van", 79),
        new("ORD-1004", "High Desert Foods", "Santa Fe, NM", "Denver, CO", 12, 14_200, "Reefer", 95),
        new("ORD-1005", "Rio Valley Medical Supply", "Las Cruces, NM", "Phoenix, AZ", 5, 4_100, "Dry Van", 88),
        new("ORD-1006", "Mesa Solar Components", "Albuquerque, NM", "Denver, CO", 14, 15_600, "Dry Van", 72)
    ];

    public IReadOnlyList<TruckProfile> Trucks { get; } =
    [
        new("TRK-201", "Dry Van", 26, 44_000, "Albuquerque, NM"),
        new("TRK-202", "Dry Van", 26, 44_000, "Albuquerque, NM"),
        new("TRK-301", "Reefer", 24, 42_000, "Santa Fe, NM")
    ];

    public IReadOnlyList<PlanResult> Plans
    {
        get { lock (sync) return plans.ToArray(); }
    }

    public void AddPlan(PlanResult plan)
    {
        lock (sync)
        {
            plans.Add(plan);
            if (plans.Count > 20) plans.RemoveAt(0);
        }
    }
}
