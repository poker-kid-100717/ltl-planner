using Portfolio.Ltl.Api.Models;
using Portfolio.Ltl.Api.Services;

namespace Portfolio.Ltl.Api.Tests;

public sealed class PlannerServiceTests
{
    private static readonly PlannerService Planner = new(TimeProvider.System);

    private static ShipmentOrder Order(string id, int pallets, int weight, string equipment = "Dry Van", int priority = 50, string origin = "Albuquerque, NM") =>
        new(id, "Example Co", origin, "Phoenix, AZ", pallets, weight, equipment, priority);

    private static TruckProfile Truck(string id, int pallets, int weight, string equipment = "Dry Van", bool active = true) =>
        new(id, equipment, pallets, weight, "Albuquerque, NM", active);

    [Fact]
    public void Build_respects_equipment_and_capacity_constraints()
    {
        var result = Planner.Build(
            [Order("A", 10, 9000), Order("B", 12, 12000), Order("C", 8, 30000, "Flatbed"), Order("D", 20, 20000)],
            [Truck("T1", 26, 44000), Truck("T2", 18, 46000, "Flatbed")]);

        Assert.NotEmpty(result.Trucks);
        foreach (var truck in result.Trucks)
        {
            Assert.True(truck.UsedPallets <= truck.PalletCapacity);
            Assert.True(truck.UsedWeight <= truck.WeightCapacity);
            Assert.All(truck.Orders, order => Assert.Equal(truck.Equipment, order.Equipment));
            Assert.NotEmpty(truck.Explanations);
        }
    }

    [Fact]
    public void Higher_priority_orders_are_placed_first()
    {
        var result = Planner.Build([Order("LOW", 20, 1000, priority: 10), Order("HIGH", 20, 1000, priority: 90)], [Truck("T1", 26, 44000)]);

        Assert.Equal("HIGH", Assert.Single(result.Trucks).Orders.Single().Id);
        Assert.Equal("LOW", Assert.Single(result.UnassignedOrders).Id);
    }

    [Fact]
    public void Every_unassigned_order_says_why()
    {
        var result = Planner.Build(
            [Order("NO-REEFER", 5, 5000, "Reefer"), Order("TOO-BIG", 29, 5000), Order("FULL", 20, 5000, priority: 1), Order("FIRST", 20, 5000, priority: 99)],
            [Truck("T1", 26, 44000), Truck("R1", 24, 42000, "Reefer", active: false)]);

        var reasons = result.Unassigned!.ToDictionary(u => u.Order.Id, u => u.Reason);
        Assert.Contains("No active Reefer truck", reasons["NO-REEFER"]);
        Assert.Contains("Too large", reasons["TOO-BIG"]);
        Assert.Contains("already full", reasons["FULL"]);
        Assert.Equal(3, result.UnassignedOrders.Count);
    }

    [Fact]
    public void Build_is_deterministic()
    {
        ShipmentOrder[] orders = [Order("A", 10, 9000), Order("B", 10, 9000), Order("C", 6, 4000)];
        TruckProfile[] trucks = [Truck("T1", 26, 44000), Truck("T2", 26, 44000)];

        var first = Planner.Build(orders, trucks);
        var second = Planner.Build(orders.Reverse().ToArray(), trucks.Reverse().ToArray());

        Assert.Equal(
            first.Trucks.Select(t => t.TruckId + ":" + string.Join(",", t.Orders.Select(o => o.Id))),
            second.Trucks.Select(t => t.TruckId + ":" + string.Join(",", t.Orders.Select(o => o.Id))));
    }
}
