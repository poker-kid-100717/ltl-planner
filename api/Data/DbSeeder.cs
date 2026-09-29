namespace Portfolio.Ltl.Api.Data;

public static class DbSeeder
{
    public static async Task SeedAsync(AppDbContext db, CancellationToken ct = default)
    {
        if (!db.Orders.Any())
        {
            db.Orders.AddRange(
                new ShipmentOrderEntity { Id="ORD-1001", Customer="Mesa Solar Components", Origin="Albuquerque, NM", Destination="Phoenix, AZ", Pallets=8, Weight=7200, Equipment="Dry Van", Priority=92, ReadyDate=DateTimeOffset.UtcNow.Date, Status="Open" },
                new ShipmentOrderEntity { Id="ORD-1002", Customer="Canyon Packaging", Origin="Albuquerque, NM", Destination="Phoenix, AZ", Pallets=6, Weight=5800, Equipment="Dry Van", Priority=84, ReadyDate=DateTimeOffset.UtcNow.Date, Status="Open" },
                new ShipmentOrderEntity { Id="ORD-1003", Customer="Sandstone Home Goods", Origin="Albuquerque, NM", Destination="Tucson, AZ", Pallets=10, Weight=9400, Equipment="Dry Van", Priority=79, ReadyDate=DateTimeOffset.UtcNow.Date, Status="Open" },
                new ShipmentOrderEntity { Id="ORD-1004", Customer="High Desert Foods", Origin="Santa Fe, NM", Destination="Denver, CO", Pallets=12, Weight=14200, Equipment="Reefer", Priority=95, ReadyDate=DateTimeOffset.UtcNow.Date, Status="Open" },
                new ShipmentOrderEntity { Id="ORD-1005", Customer="Rio Valley Medical Supply", Origin="Las Cruces, NM", Destination="Phoenix, AZ", Pallets=5, Weight=4100, Equipment="Dry Van", Priority=88, ReadyDate=DateTimeOffset.UtcNow.Date, Status="Open" },
                new ShipmentOrderEntity { Id="ORD-1006", Customer="Mesa Solar Components", Origin="Albuquerque, NM", Destination="Denver, CO", Pallets=14, Weight=15600, Equipment="Dry Van", Priority=72, ReadyDate=DateTimeOffset.UtcNow.Date, Status="Open" }
            );
        }

        if (!db.Trucks.Any())
        {
            db.Trucks.AddRange(
                new TruckEntity { Id="TRK-201", Equipment="Dry Van", PalletCapacity=26, WeightCapacity=44000, CurrentLocation="Albuquerque, NM", Active=true },
                new TruckEntity { Id="TRK-202", Equipment="Dry Van", PalletCapacity=26, WeightCapacity=44000, CurrentLocation="Albuquerque, NM", Active=true },
                new TruckEntity { Id="TRK-301", Equipment="Reefer", PalletCapacity=24, WeightCapacity=42000, CurrentLocation="Santa Fe, NM", Active=true }
            );
        }

        await db.SaveChangesAsync(ct);
    }
}
