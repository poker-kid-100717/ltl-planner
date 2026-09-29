namespace Portfolio.Ltl.Api.Models;

public sealed record ShipmentOrder(
    string Id, string Customer, string Origin, string Destination, int Pallets, int Weight,
    string Equipment, int Priority, string Status = "Open", DateTimeOffset? ReadyDate = null, Guid? PlanId = null);

public sealed record TruckProfile(
    string Id, string Equipment, int PalletCapacity, int WeightCapacity, string CurrentLocation, bool Active = true);

public sealed record PlanBuildRequest(IReadOnlyList<string>? OrderIds, IReadOnlyList<string>? TruckIds);
public sealed record CreateOrderRequest(string Customer, string Origin, string Destination, int Pallets, int Weight, string Equipment, int Priority, DateTimeOffset? ReadyDate);
public sealed record UpdateOrderRequest(string Customer, string Origin, string Destination, int Pallets, int Weight, string Equipment, int Priority, DateTimeOffset? ReadyDate);
public sealed record CreateTruckRequest(string Equipment, int PalletCapacity, int WeightCapacity, string CurrentLocation);
public sealed record UpdateTruckRequest(string Equipment, int PalletCapacity, int WeightCapacity, string CurrentLocation, bool Active);

public sealed record PlannedTruck(
    string TruckId, string Equipment, IReadOnlyList<ShipmentOrder> Orders, int UsedPallets,
    int PalletCapacity, int UsedWeight, int WeightCapacity, decimal Utilization, IReadOnlyList<string> Explanations);

public sealed record UnassignedOrder(ShipmentOrder Order, string Reason);

public sealed record PlanResult(
    Guid Id, DateTimeOffset CreatedAt, string Status, IReadOnlyList<PlannedTruck> Trucks,
    IReadOnlyList<UnassignedOrder> UnassignedOrders, string Algorithm);

public sealed record YardCandidate(
    string OrderId, string Customer, string Origin, string Destination, int Pallets, int Weight,
    string Equipment, string Reason);

public sealed record YardIntegrationEvent(
    Guid EventId, string EventType, string TrailerNumber, DateTimeOffset OccurredAt, string? Details, int SchemaVersion = 1);

public sealed record YardTrailer(string TrailerNumber, string Status, DateTimeOffset LastEventAt);

public sealed record ExternalLoad(
    string LoadNumber, string CustomerName, string Status, DateTimeOffset? ScheduledPickupAt,
    DateTimeOffset? ScheduledDeliveryAt, IReadOnlyList<string> RequiredEquipment, decimal? Weight, string Source);

public sealed record ExternalLoadResult(
    string Provider, bool Live, bool Degraded, string? DegradedReason, IReadOnlyList<ExternalLoad> Loads);
