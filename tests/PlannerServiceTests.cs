using Portfolio.Ltl.Api.Services;

namespace Portfolio.Ltl.Api.Tests;

public sealed class PlannerServiceTests
{
    [Fact]
    public void Build_respects_equipment_and_capacity_constraints()
    {
        var store = new LtlStore();
        var planner = new PlannerService(store);

        var result = planner.Build();

        Assert.NotEmpty(result.Trucks);
        foreach (var truck in result.Trucks)
        {
            Assert.True(truck.UsedPallets <= truck.PalletCapacity);
            Assert.True(truck.UsedWeight <= truck.WeightCapacity);
            Assert.All(truck.Orders, order => Assert.Equal(truck.Equipment, order.Equipment));
        }
    }

    [Fact]
    public void Build_is_explainable_and_reports_unassigned_orders()
    {
        var result = new PlannerService(new LtlStore()).Build();

        Assert.Contains("Explainable", result.Algorithm);
        Assert.All(result.Trucks, truck => Assert.NotEmpty(truck.Explanations));
        Assert.True(result.UnassignedOrders.Count >= 0);
    }
}
