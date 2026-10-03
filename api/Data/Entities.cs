namespace Portfolio.Ltl.Api.Data;

// Domain records shared by the Neo4j store and the SQLite demo store. Timestamps are UTC DateTime
// (ZonedDateTime in Neo4j) so both stores sort and filter on them natively.

public sealed class Order
{
    public string Id { get; set; } = "";
    public string Customer { get; set; } = "";
    public string Origin { get; set; } = "";
    public string Destination { get; set; } = "";
    public int Pallets { get; set; }
    public int Weight { get; set; }
    public string Equipment { get; set; } = EquipmentTypes.DryVan;
    public int Priority { get; set; }
    public DateOnly ReadyOn { get; set; }
    public string Status { get; set; } = OrderStatuses.Open;
    public Guid? PlanId { get; set; }
    public string? TruckId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class Truck
{
    public string Id { get; set; } = "";
    public string Equipment { get; set; } = EquipmentTypes.DryVan;
    public int PalletCapacity { get; set; }
    public int WeightCapacity { get; set; }
    public string CurrentLocation { get; set; } = "";
    public bool Active { get; set; } = true;
}

/// <summary>
/// A saved plan. The planner's full, explained result is stored as JSON: it is a snapshot of
/// what the planner decided, read back as a whole and never queried by field. Once committed, the plan
/// is also connected to its orders in the graph (Plan-INCLUDES->Order-ASSIGNED_TO->Truck).
/// </summary>
public sealed class Plan
{
    public Guid Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public string Status { get; set; } = PlanStatuses.Draft;
    public DateTime? DecidedAt { get; set; }
    public int TruckCount { get; set; }
    public int PlannedOrders { get; set; }
    public int UnassignedOrders { get; set; }
    public string ResultJson { get; set; } = "{}";
}

/// <summary>A signed event received from Yard Ops. The event id is the primary key, which makes ingestion idempotent.</summary>
public sealed class YardEvent
{
    public Guid EventId { get; set; }
    public string EventType { get; set; } = "";
    public string TrailerNumber { get; set; } = "";
    public DateTime OccurredAt { get; set; }
    public string? Details { get; set; }
    public int SchemaVersion { get; set; } = 1;
    public DateTime ReceivedAt { get; set; }
}

/// <summary>What Yard Ops last told us about each trailer, updated with every accepted event.</summary>
public sealed class YardTrailer
{
    public string TrailerNumber { get; set; } = "";
    public string Status { get; set; } = "";
    public string LastEventType { get; set; } = "";
    public DateTime LastEventAt { get; set; }
}

public static class OrderStatuses
{
    public const string Open = "Open";
    public const string Planned = "Planned";
    public const string Dispatched = "Dispatched";
    public const string Cancelled = "Cancelled";
    public static readonly string[] All = [Open, Planned, Dispatched, Cancelled];
}

public static class PlanStatuses
{
    public const string Draft = "Draft";
    public const string Committed = "Committed";
    public const string Discarded = "Discarded";
    public static readonly string[] All = [Draft, Committed, Discarded];
}

public static class EquipmentTypes
{
    public const string DryVan = "Dry Van";
    public const string Reefer = "Reefer";
    public const string Flatbed = "Flatbed";
    public static readonly string[] All = [DryVan, Reefer, Flatbed];
}

public static class Limits
{
    public const int Name = 120;
    public const int Place = 80;
    public const int MaxPallets = 30;
    public const int MaxWeight = 48_000;
}
