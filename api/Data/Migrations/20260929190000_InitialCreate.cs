using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Portfolio.Ltl.Api.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260929190000_InitialCreate")]
public sealed class InitialCreate : Migration
{
    protected override void Up(MigrationBuilder m)
    {
        m.CreateTable("plans", t => new {
            Id=t.Column<Guid>("Id", nullable:false),
            CreatedAt=t.Column<DateTimeOffset>("CreatedAt", nullable:false),
            Status=t.Column<string>("Status", maxLength:20, nullable:false),
            Algorithm=t.Column<string>("Algorithm", maxLength:240, nullable:false),
            PayloadJson=t.Column<string>("PayloadJson", type:"jsonb", nullable:false)
        }, constraints:t => t.PrimaryKey("PK_plans", x => x.Id));

        m.CreateTable("shipment_orders", t => new {
            Id=t.Column<string>("Id", maxLength:16, nullable:false),
            Customer=t.Column<string>("Customer", maxLength:120, nullable:false),
            Origin=t.Column<string>("Origin", maxLength:120, nullable:false),
            Destination=t.Column<string>("Destination", maxLength:120, nullable:false),
            Pallets=t.Column<int>("Pallets", nullable:false),
            Weight=t.Column<int>("Weight", nullable:false),
            Equipment=t.Column<string>("Equipment", maxLength:40, nullable:false),
            Priority=t.Column<int>("Priority", nullable:false),
            ReadyDate=t.Column<DateTimeOffset>("ReadyDate", nullable:false),
            Status=t.Column<string>("Status", maxLength:20, nullable:false),
            PlanId=t.Column<Guid>("PlanId", nullable:true)
        }, constraints:t => t.PrimaryKey("PK_shipment_orders", x => x.Id));

        m.CreateTable("trucks", t => new {
            Id=t.Column<string>("Id", maxLength:16, nullable:false),
            Equipment=t.Column<string>("Equipment", maxLength:40, nullable:false),
            PalletCapacity=t.Column<int>("PalletCapacity", nullable:false),
            WeightCapacity=t.Column<int>("WeightCapacity", nullable:false),
            CurrentLocation=t.Column<string>("CurrentLocation", maxLength:120, nullable:false),
            Active=t.Column<bool>("Active", nullable:false)
        }, constraints:t => t.PrimaryKey("PK_trucks", x => x.Id));

        m.CreateTable("yard_events", t => new {
            EventId=t.Column<Guid>("EventId", nullable:false),
            EventType=t.Column<string>("EventType", maxLength:80, nullable:false),
            TrailerNumber=t.Column<string>("TrailerNumber", maxLength:40, nullable:false),
            OccurredAt=t.Column<DateTimeOffset>("OccurredAt", nullable:false),
            Details=t.Column<string>("Details", maxLength:500, nullable:true),
            ReceivedAt=t.Column<DateTimeOffset>("ReceivedAt", nullable:false),
            Processed=t.Column<bool>("Processed", nullable:false)
        }, constraints:t => t.PrimaryKey("PK_yard_events", x => x.EventId));

        m.CreateTable("yard_trailers", t => new {
            TrailerNumber=t.Column<string>("TrailerNumber", maxLength:40, nullable:false),
            Status=t.Column<string>("Status", maxLength:80, nullable:false),
            LastEventAt=t.Column<DateTimeOffset>("LastEventAt", nullable:false)
        }, constraints:t => t.PrimaryKey("PK_yard_trailers", x => x.TrailerNumber));

        m.CreateIndex("IX_plans_CreatedAt","plans","CreatedAt");
        m.CreateIndex("IX_shipment_orders_PlanId","shipment_orders","PlanId");
        m.CreateIndex("IX_shipment_orders_Status","shipment_orders","Status");
        m.CreateIndex("IX_trucks_Active","trucks","Active");
        m.CreateIndex("IX_yard_events_EventId","yard_events","EventId", unique:true);
        m.CreateIndex("IX_yard_events_OccurredAt","yard_events","OccurredAt");
    }

    protected override void Down(MigrationBuilder m)
    {
        m.DropTable("yard_trailers");
        m.DropTable("yard_events");
        m.DropTable("trucks");
        m.DropTable("shipment_orders");
        m.DropTable("plans");
    }
}
