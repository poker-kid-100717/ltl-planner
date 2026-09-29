using Portfolio.Ltl.Api.Models;
using Portfolio.Ltl.Api.Services;

namespace Portfolio.Ltl.Api.Tests;

public sealed class PlannerServiceTests
{
    private static readonly ShipmentOrder[] Orders =
    [
        new("ORD-1","Demo Customer","Albuquerque, NM","Phoenix, AZ",8,7200,"Dry Van",90),
        new("ORD-2","Cold Foods","Santa Fe, NM","Denver, CO",12,14000,"Reefer",95),
        new("ORD-3","Oversize Demo","Albuquerque, NM","Phoenix, AZ",30,20000,"Dry Van",80)
    ];

    private static readonly TruckProfile[] Trucks =
    [
        new("TRK-1","Dry Van",26,44000,"Albuquerque, NM"),
        new("TRK-2","Reefer",24,42000,"Santa Fe, NM")
    ];

    [Fact]
    public void Build_respects_equipment_and_capacity_constraints()
    {
        var result = new PlannerService().Build(Orders, Trucks);
        Assert.NotEmpty(result.Trucks);
        foreach (var truck in result.Trucks)
        {
            Assert.True(truck.UsedPallets <= truck.PalletCapacity);
            Assert.True(truck.UsedWeight <= truck.WeightCapacity);
            Assert.All(truck.Orders, order => Assert.Equal(truck.Equipment, order.Equipment));
        }
    }

    [Fact]
    public void Build_is_explainable_and_reports_specific_unassigned_reason()
    {
        var result = new PlannerService().Build(Orders, Trucks);
        Assert.Contains("Explainable", result.Algorithm);
        Assert.All(result.Trucks, truck => Assert.NotEmpty(truck.Explanations));
        var unassigned = Assert.Single(result.UnassignedOrders);
        Assert.Equal("ORD-3", unassigned.Order.Id);
        Assert.Contains("pallet capacity", unassigned.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_ignores_non_open_orders_and_inactive_trucks()
    {
        var orders = Orders.Append(new ShipmentOrder("ORD-X","Done","A","B",1,100,"Dry Van",100,"Planned")).ToArray();
        var trucks = Trucks.Append(new TruckProfile("TRK-X","Dry Van",60,100000,"A",false)).ToArray();
        var result = new PlannerService().Build(orders, trucks);
        Assert.DoesNotContain(result.Trucks.SelectMany(x=>x.Orders), x=>x.Id=="ORD-X");
        Assert.DoesNotContain(result.Trucks, x=>x.TruckId=="TRK-X");
    }
}
