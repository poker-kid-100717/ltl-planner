using Microsoft.EntityFrameworkCore;

namespace Portfolio.Ltl.Api.Data;

/// <summary>EF Core model for the SQLite demo store (created from the model; Neo4j is the persistent store).</summary>
public sealed class LtlDbContext(DbContextOptions<LtlDbContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<Truck> Trucks => Set<Truck>();
    public DbSet<Plan> Plans => Set<Plan>();
    public DbSet<YardEvent> YardEvents => Set<YardEvent>();
    public DbSet<YardTrailer> YardTrailers => Set<YardTrailer>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Order>(e =>
        {
            e.Property(x => x.Id).HasMaxLength(20);
            e.Property(x => x.Customer).HasMaxLength(Limits.Name);
            e.Property(x => x.Origin).HasMaxLength(Limits.Place);
            e.Property(x => x.Destination).HasMaxLength(Limits.Place);
            e.Property(x => x.Equipment).HasMaxLength(20);
            e.Property(x => x.Status).HasMaxLength(20);
            e.Property(x => x.TruckId).HasMaxLength(20);
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.PlanId);
        });

        model.Entity<Truck>(e =>
        {
            e.Property(x => x.Id).HasMaxLength(20);
            e.Property(x => x.Equipment).HasMaxLength(20);
            e.Property(x => x.CurrentLocation).HasMaxLength(Limits.Place);
        });

        model.Entity<Plan>(e =>
        {
            e.Property(x => x.Status).HasMaxLength(20);
            e.HasIndex(x => x.CreatedAt);
        });

        model.Entity<YardEvent>(e =>
        {
            e.HasKey(x => x.EventId);
            e.Property(x => x.EventType).HasMaxLength(60);
            e.Property(x => x.TrailerNumber).HasMaxLength(40);
            e.Property(x => x.Details).HasMaxLength(1000);
            e.HasIndex(x => x.OccurredAt);
        });

        model.Entity<YardTrailer>(e =>
        {
            e.HasKey(x => x.TrailerNumber);
            e.Property(x => x.TrailerNumber).HasMaxLength(40);
            e.Property(x => x.Status).HasMaxLength(40);
            e.Property(x => x.LastEventType).HasMaxLength(60);
        });
    }
}
