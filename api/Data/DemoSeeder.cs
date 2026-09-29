using Microsoft.EntityFrameworkCore;

namespace Portfolio.Ltl.Api.Data;

/// <summary>
/// Fictional demo orders and trucks, dated relative to "now". A reset clears planning data only:
/// events received from Yard Ops are real integration history and are kept.
/// </summary>
public sealed class DemoSeeder(TimeProvider clock)
{
    public async Task ResetAsync(LtlDbContext db, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Plans.ExecuteDeleteAsync(ct);
        await db.Orders.ExecuteDeleteAsync(ct);
        await db.Trucks.ExecuteDeleteAsync(ct);
        await SeedCoreAsync(db, ct);
        await tx.CommitAsync(ct);
    }

    public async Task SeedAsync(LtlDbContext db, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await SeedCoreAsync(db, ct);
        await tx.CommitAsync(ct);
    }

    private async Task SeedCoreAsync(LtlDbContext db, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var today = DateOnly.FromDateTime(now);

        db.Trucks.AddRange(
            new Truck { Id = "TRK-201", Equipment = EquipmentTypes.DryVan, PalletCapacity = 26, WeightCapacity = 44_000, CurrentLocation = "Albuquerque, NM" },
            new Truck { Id = "TRK-202", Equipment = EquipmentTypes.DryVan, PalletCapacity = 26, WeightCapacity = 44_000, CurrentLocation = "Albuquerque, NM" },
            new Truck { Id = "TRK-203", Equipment = EquipmentTypes.DryVan, PalletCapacity = 24, WeightCapacity = 42_000, CurrentLocation = "Las Cruces, NM" },
            new Truck { Id = "TRK-301", Equipment = EquipmentTypes.Reefer, PalletCapacity = 24, WeightCapacity = 42_000, CurrentLocation = "Santa Fe, NM" },
            new Truck { Id = "TRK-302", Equipment = EquipmentTypes.Reefer, PalletCapacity = 22, WeightCapacity = 40_000, CurrentLocation = "Albuquerque, NM", Active = false },
            new Truck { Id = "TRK-401", Equipment = EquipmentTypes.Flatbed, PalletCapacity = 18, WeightCapacity = 46_000, CurrentLocation = "Albuquerque, NM" });

        Order O(int n, string customer, string from, string to, int pallets, int weight, string equipment, int priority, int readyInDays) => new()
        {
            Id = $"ORD-{n}", Customer = customer, Origin = from, Destination = to, Pallets = pallets, Weight = weight,
            Equipment = equipment, Priority = priority, ReadyOn = today.AddDays(readyInDays), CreatedAt = now, UpdatedAt = now
        };

        db.Orders.AddRange(
            O(1001, "Mesa Solar Components", "Albuquerque, NM", "Phoenix, AZ", 8, 7_200, EquipmentTypes.DryVan, 92, 0),
            O(1002, "Canyon Packaging", "Albuquerque, NM", "Phoenix, AZ", 6, 5_800, EquipmentTypes.DryVan, 84, 0),
            O(1003, "Sandstone Home Goods", "Albuquerque, NM", "Tucson, AZ", 10, 9_400, EquipmentTypes.DryVan, 79, 1),
            O(1004, "High Desert Foods", "Santa Fe, NM", "Denver, CO", 12, 14_200, EquipmentTypes.Reefer, 95, 0),
            O(1005, "Rio Valley Medical Supply", "Las Cruces, NM", "Phoenix, AZ", 5, 4_100, EquipmentTypes.DryVan, 88, 0),
            O(1006, "Mesa Solar Components", "Albuquerque, NM", "Denver, CO", 14, 15_600, EquipmentTypes.DryVan, 72, 2),
            O(1007, "Juniper Craft Beverages", "Albuquerque, NM", "Las Vegas, NV", 9, 11_800, EquipmentTypes.DryVan, 66, 1),
            O(1008, "Sonoran Fresh Produce", "Santa Fe, NM", "Denver, CO", 10, 12_600, EquipmentTypes.Reefer, 90, 0),
            O(1009, "Pinon Pet Nutrition", "Las Cruces, NM", "El Paso, TX", 7, 6_300, EquipmentTypes.DryVan, 61, 1),
            O(1010, "Red Mesa Building Supply", "Albuquerque, NM", "Amarillo, TX", 12, 31_000, EquipmentTypes.Flatbed, 80, 0),
            O(1011, "Chaco Industrial Coatings", "Albuquerque, NM", "Denver, CO", 4, 5_400, EquipmentTypes.DryVan, 58, 3),
            O(1012, "High Desert Foods", "Albuquerque, NM", "Denver, CO", 6, 7_000, EquipmentTypes.Reefer, 70, 1),
            O(1013, "Turquoise Trail Apparel", "Santa Fe, NM", "Phoenix, AZ", 3, 2_200, EquipmentTypes.DryVan, 55, 2),
            O(1014, "Big Sky Ag Equipment", "Albuquerque, NM", "Lubbock, TX", 8, 22_000, EquipmentTypes.Flatbed, 64, 2));

        await db.SaveChangesAsync(ct);
    }
}
