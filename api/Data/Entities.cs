namespace Portfolio.Ltl.Api.Data;

public sealed class ShipmentOrderEntity
{
    public string Id { get; set; } = "";
    public string Customer { get; set; } = "";
    public string Origin { get; set; } = "";
    public string Destination { get; set; } = "";
    public int Pallets { get; set; }
    public int Weight { get; set; }
    public string Equipment { get; set; } = "";
    public int Priority { get; set; }
    public DateTimeOffset ReadyDate { get; set; }
    public string Status { get; set; } = "Open";
    public Guid? PlanId { get; set; }
    public uint Version { get; set; }
}

public sealed class TruckEntity
{
    public string Id { get; set; } = "";
    public string Equipment { get; set; } = "";
    public int PalletCapacity { get; set; }
    public int WeightCapacity { get; set; }
    public string CurrentLocation { get; set; } = "";
    public bool Active { get; set; } = true;
    public uint Version { get; set; }
}

public sealed class PlanEntity
{
    public Guid Id { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string Status { get; set; } = "Draft";
    public string Algorithm { get; set; } = "";
    public string PayloadJson { get; set; } = "{}";
    public uint Version { get; set; }
}

public sealed class YardEventEntity
{
    public Guid EventId { get; set; }
    public string EventType { get; set; } = "";
    public string TrailerNumber { get; set; } = "";
    public DateTimeOffset OccurredAt { get; set; }
    public string? Details { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    public bool Processed { get; set; }
}

public sealed class YardTrailerEntity
{
    public string TrailerNumber { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTimeOffset LastEventAt { get; set; }
}
