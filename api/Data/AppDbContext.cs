using Microsoft.EntityFrameworkCore;

namespace Portfolio.Ltl.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<ShipmentOrderEntity> Orders => Set<ShipmentOrderEntity>();
    public DbSet<TruckEntity> Trucks => Set<TruckEntity>();
    public DbSet<PlanEntity> Plans => Set<PlanEntity>();
    public DbSet<YardEventEntity> YardEvents => Set<YardEventEntity>();
    public DbSet<YardTrailerEntity> YardTrailers => Set<YardTrailerEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ShipmentOrderEntity>(e =>
        {
            e.ToTable("shipment_orders");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(16);
            e.Property(x => x.Customer).HasMaxLength(120).IsRequired();
            e.Property(x => x.Origin).HasMaxLength(120).IsRequired();
            e.Property(x => x.Destination).HasMaxLength(120).IsRequired();
            e.Property(x => x.Equipment).HasMaxLength(40).IsRequired();
            e.Property(x => x.Status).HasMaxLength(20).IsRequired();
            e.Property(x => x.Version).HasColumnName("xmin").IsRowVersion();
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.PlanId);
        });

        modelBuilder.Entity<TruckEntity>(e =>
        {
            e.ToTable("trucks");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(16);
            e.Property(x => x.Equipment).HasMaxLength(40).IsRequired();
            e.Property(x => x.CurrentLocation).HasMaxLength(120).IsRequired();
            e.Property(x => x.Version).HasColumnName("xmin").IsRowVersion();
            e.HasIndex(x => x.Active);
        });

        modelBuilder.Entity<PlanEntity>(e =>
        {
            e.ToTable("plans");
            e.HasKey(x => x.Id);
            e.Property(x => x.Status).HasMaxLength(20).IsRequired();
            e.Property(x => x.Algorithm).HasMaxLength(240).IsRequired();
            e.Property(x => x.PayloadJson).HasColumnType("jsonb").IsRequired();
            e.Property(x => x.Version).HasColumnName("xmin").IsRowVersion();
            e.HasIndex(x => x.CreatedAt);
        });

        modelBuilder.Entity<YardEventEntity>(e =>
        {
            e.ToTable("yard_events");
            e.HasKey(x => x.EventId);
            e.Property(x => x.EventType).HasMaxLength(80).IsRequired();
            e.Property(x => x.TrailerNumber).HasMaxLength(40).IsRequired();
            e.Property(x => x.Details).HasMaxLength(500);
            e.HasIndex(x => x.EventId).IsUnique();
            e.HasIndex(x => x.OccurredAt);
        });

        modelBuilder.Entity<YardTrailerEntity>(e =>
        {
            e.ToTable("yard_trailers");
            e.HasKey(x => x.TrailerNumber);
            e.Property(x => x.TrailerNumber).HasMaxLength(40);
            e.Property(x => x.Status).HasMaxLength(80).IsRequired();
        });
    }
}
