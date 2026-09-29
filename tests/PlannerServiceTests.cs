using Portfolio.Ltl.Api.Models;
using Portfolio.Ltl.Api.Services;

namespace Portfolio.Ltl.Api.Tests;

public sealed class PlannerServiceTests
{
    [Fact]
    public void Build_respects_equipment_and_capacity_constraints()
    {
        var result = new PlannerService().Build(DemoOrders(), DemoTrucks());

        Assert.NotEmpty(result.Trucks);
        foreach (var truck in result.Trucks)
        {
            Assert.True(truck.UsedPallets <= truck.PalletCapacity);
            Assert.True(truck.UsedWeight <= truck.WeightCapacity);
            Assert.All(truck.Orders, order => Assert.Equal(truck.Equipment, order.Equipment));
        }
    }

    [Fact]
    public void Build_is_explainable_and_reports_a_reason_for_every_unassigned_order()
    {
        var orders = DemoOrders().Append(new ShipmentOrder("ORD-X", "Demo", "A", "B", 60, 90000, "Flatbed", 100)).ToArray();
        var result = new PlannerService().Build(orders, DemoTrucks());

        Assert.Equal("Draft", result.Status);
        Assert.Contains("Explainable", result.Algorithm);
        Assert.All(result.Trucks, truck => Assert.NotEmpty(truck.Explanations));
        Assert.All(result.UnassignedOrders, item => Assert.False(string.IsNullOrWhiteSpace(item.Reason)));
        Assert.Contains(result.UnassignedOrders, item => item.Order.Id == "ORD-X" && item.Reason.Contains("Flatbed"));
    }

    private static ShipmentOrder[] DemoOrders() =>
    [
        new("ORD-1", "A", "Albuquerque, NM", "Phoenix, AZ", 8, 7200, "Dry Van", 92),
        new("ORD-2", "B", "Albuquerque, NM", "Phoenix, AZ", 6, 5800, "Dry Van", 84),
        new("ORD-3", "C", "Santa Fe, NM", "Denver, CO", 12, 14200, "Reefer", 95)
    ];

    private static TruckProfile[] DemoTrucks() =>
    [
        new("TRK-1", "Dry Van", 26, 44000, "Albuquerque, NM"),
        new("TRK-2", "Reefer", 24, 42000, "Santa Fe, NM")
    ];
}
