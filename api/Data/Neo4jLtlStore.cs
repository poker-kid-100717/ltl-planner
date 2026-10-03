using Neo4j.Driver;
using Portfolio.Ltl.Api.Endpoints;

namespace Portfolio.Ltl.Api.Data;

/// <summary>
/// The Neo4j connection, parsed from DATABASE_URL: neo4j+s://user:password@host[/database] (an AuraDB URL works
/// as-is) or bolt://... for a local server. One driver is shared by the whole app.
/// </summary>
public sealed class Neo4jConnection : IAsyncDisposable
{
    public IDriver Driver { get; }
    public string? Database { get; }

    public Neo4jConnection(string url)
    {
        var uri = new Uri(url);
        var user = uri.UserInfo.Split(':', 2);
        var server = new UriBuilder(uri) { UserName = "", Password = "", Path = "" }.Uri;
        Database = uri.AbsolutePath.Trim('/') is { Length: > 0 } db ? Uri.UnescapeDataString(db) : null;
        var auth = user[0].Length == 0
            ? AuthTokens.None
            : AuthTokens.Basic(Uri.UnescapeDataString(user[0]), user.Length > 1 ? Uri.UnescapeDataString(user[1]) : "");
        Driver = GraphDatabase.Driver(server, auth, o => o.WithConnectionTimeout(TimeSpan.FromSeconds(15)));
    }

    public static bool IsNeo4jUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme.StartsWith("neo4j", StringComparison.OrdinalIgnoreCase) ||
                                                               uri.Scheme.StartsWith("bolt", StringComparison.OrdinalIgnoreCase));

    public ValueTask DisposeAsync() => Driver.DisposeAsync();
}

/// <summary>
/// Planning data as a graph. Orders ship FROM and TO Location nodes and REQUIRE an Equipment node; trucks HAVE equipment
/// and are LOCATED_AT a location; a committed plan INCLUDES its orders, each ASSIGNED_TO a truck; Yard events are ABOUT a
/// trailer. Compatibility questions (which trucks can serve a lane) become traversals instead of joins.
/// </summary>
public sealed class Neo4jLtlStore(Neo4jConnection connection, DemoSeeder seeder) : ILtlStore
{
    private const string ConstraintFailed = "Neo.ClientError.Schema.ConstraintValidationFailed";

    private static readonly string[] Schema =
    [
        "CREATE CONSTRAINT order_id IF NOT EXISTS FOR (o:Order) REQUIRE o.id IS UNIQUE",
        "CREATE CONSTRAINT truck_id IF NOT EXISTS FOR (t:Truck) REQUIRE t.id IS UNIQUE",
        "CREATE CONSTRAINT plan_id IF NOT EXISTS FOR (p:Plan) REQUIRE p.id IS UNIQUE",
        "CREATE CONSTRAINT location_name IF NOT EXISTS FOR (l:Location) REQUIRE l.name IS UNIQUE",
        "CREATE CONSTRAINT equipment_name IF NOT EXISTS FOR (e:Equipment) REQUIRE e.name IS UNIQUE",
        "CREATE CONSTRAINT yard_event_id IF NOT EXISTS FOR (e:YardEvent) REQUIRE e.eventId IS UNIQUE",
        "CREATE CONSTRAINT trailer_number IF NOT EXISTS FOR (t:Trailer) REQUIRE t.trailerNumber IS UNIQUE",
        "CREATE INDEX order_status IF NOT EXISTS FOR (o:Order) ON (o.status)",
        "CREATE INDEX plan_created IF NOT EXISTS FOR (p:Plan) ON (p.createdAt)",
        "CREATE INDEX yard_event_occurred IF NOT EXISTS FOR (e:YardEvent) ON (e.occurredAt)",
    ];

    // An order with its lane and equipment; callers bind (o) first.
    private const string OrderShape = """
        MATCH (o)-[:SHIPS_FROM]->(f:Location), (o)-[:SHIPS_TO]->(d:Location), (o)-[:REQUIRES]->(eq:Equipment)
        OPTIONAL MATCH (o)-[:ASSIGNED_TO]->(t:Truck)
        OPTIONAL MATCH (p:Plan)-[:INCLUDES]->(o)
        """;
    private const string OrderColumns = "o, f.name AS origin, d.name AS destination, eq.name AS equipment, t.id AS truckId, p.id AS planId";

    private const string TruckShape = "MATCH (t)-[:HAS_EQUIPMENT]->(eq:Equipment), (t)-[:LOCATED_AT]->(l:Location)";
    private const string TruckColumns = "t, eq.name AS equipment, l.name AS location";

    // Creates or replaces an order's lane and equipment relationships; callers bind (o).
    private const string LinkOrder = """
        OPTIONAL MATCH (o)-[old:SHIPS_FROM|SHIPS_TO|REQUIRES]->()
        DELETE old
        WITH DISTINCT o
        MERGE (f:Location {name: $origin})
        MERGE (d:Location {name: $destination})
        MERGE (eq:Equipment {name: $equipment})
        CREATE (o)-[:SHIPS_FROM]->(f), (o)-[:SHIPS_TO]->(d), (o)-[:REQUIRES]->(eq)
        """;

    private const string LinkTruck = """
        OPTIONAL MATCH (t)-[old:HAS_EQUIPMENT|LOCATED_AT]->()
        DELETE old
        WITH DISTINCT t
        MERGE (eq:Equipment {name: $equipment})
        MERGE (l:Location {name: $location})
        CREATE (t)-[:HAS_EQUIPMENT]->(eq), (t)-[:LOCATED_AT]->(l)
        """;

    public async Task InitializeAsync(CancellationToken ct)
    {
        foreach (var statement in Schema) await WriteAsync(statement, null, ct);
        var trucks = await ReadAsync("MATCH (t:Truck) RETURN count(t) AS n", null, ct);
        if (trucks[0]["n"].As<long>() == 0) await SeedAsync(ct);
    }

    public async Task ResetDemoAsync(CancellationToken ct)
    {
        await WriteAsync("MATCH (n) WHERE n:Plan OR n:Order OR n:Truck DETACH DELETE n", null, ct);
        await SeedAsync(ct);
    }

    private async Task SeedAsync(CancellationToken ct)
    {
        foreach (var truck in seeder.Trucks()) await AddTruckAsync(truck, ct);
        foreach (var order in seeder.Orders()) await AddOrderAsync(order, ct);
    }

    // ---------- orders ----------

    private const string OrderFilter = """
        MATCH (o:Order)-[:SHIPS_FROM]->(f:Location), (o)-[:SHIPS_TO]->(d:Location), (o)-[:REQUIRES]->(eq:Equipment)
        WHERE ($status IS NULL OR o.status = $status) AND ($equipment IS NULL OR eq.name = $equipment)
          AND ($search IS NULL OR toLower(o.id) CONTAINS $search OR toLower(o.customer) CONTAINS $search
               OR toLower(f.name) CONTAINS $search OR toLower(d.name) CONTAINS $search)
        """;

    public async Task<Paged<Order>> ListOrdersAsync(OrderQuery q, CancellationToken ct)
    {
        var parameters = new { status = q.Status, equipment = q.Equipment, search = q.Search?.ToLowerInvariant(), skip = (q.Page - 1) * q.PageSize, take = q.PageSize };
        var total = (await ReadAsync($"{OrderFilter} RETURN count(o) AS n", parameters, ct))[0]["n"].As<int>();
        var rows = await ReadAsync($"""
            {OrderFilter}
            WITH o ORDER BY o.priority DESC, o.id SKIP $skip LIMIT $take
            {OrderShape}
            RETURN {OrderColumns} ORDER BY o.priority DESC, o.id
            """, parameters, ct);
        return new Paged<Order>(rows.Select(ToOrder).ToList(), total, q.Page, q.PageSize);
    }

    public async Task<Order?> GetOrderAsync(string id, CancellationToken ct) =>
        (await ReadAsync($"MATCH (o:Order {{id: $id}}) {OrderShape} RETURN {OrderColumns}", new { id }, ct)).Select(ToOrder).SingleOrDefault();

    public async Task<IReadOnlyList<string>> OrderIdsAsync(CancellationToken ct) =>
        (await ReadAsync("MATCH (o:Order) RETURN o.id AS id", null, ct)).Select(r => r["id"].As<string>()).ToList();

    public Task AddOrderAsync(Order order, CancellationToken ct) =>
        WriteAsync($"CREATE (o:Order {{id: $id}}) SET o += $props WITH o {LinkOrder}", OrderParameters(order), ct);

    public Task UpdateOrderAsync(Order order, CancellationToken ct) =>
        WriteAsync($"MATCH (o:Order {{id: $id}}) SET o += $props WITH o {LinkOrder}", OrderParameters(order), ct);

    public async Task<TransitionResult> TransitionOrderAsync(string id, string from, string to, DateTime now, CancellationToken ct)
    {
        // Setting a property first takes the write lock, so two concurrent moves cannot both see the old status.
        var rows = await WriteAsync("""
            MATCH (o:Order {id: $id})
            SET o._lock = true REMOVE o._lock
            WITH o, o.status AS current
            FOREACH (_ IN CASE WHEN current = $from THEN [1] ELSE [] END | SET o.status = $to, o.updatedAt = $now)
            RETURN current
            """, new { id, from, to, now = Zoned(now) }, ct);
        if (rows.Count == 0) return new TransitionResult(false, false, null);
        var current = rows[0]["current"].As<string>();
        return current == from ? new TransitionResult(true, true, to) : new TransitionResult(true, false, current);
    }

    private static object OrderParameters(Order o) => new
    {
        id = o.Id, origin = o.Origin, destination = o.Destination, equipment = o.Equipment,
        props = new Dictionary<string, object?>
        {
            ["customer"] = o.Customer, ["pallets"] = o.Pallets, ["weight"] = o.Weight, ["priority"] = o.Priority,
            ["readyOn"] = new LocalDate(o.ReadyOn), ["status"] = o.Status, ["createdAt"] = Zoned(o.CreatedAt), ["updatedAt"] = Zoned(o.UpdatedAt)
        }
    };

    private static Order ToOrder(IRecord r)
    {
        var o = r["o"].As<INode>().Properties;
        return new Order
        {
            Id = o["id"].As<string>(), Customer = o["customer"].As<string>(), Origin = r["origin"].As<string>(),
            Destination = r["destination"].As<string>(), Pallets = o["pallets"].As<int>(), Weight = o["weight"].As<int>(),
            Equipment = r["equipment"].As<string>(), Priority = o["priority"].As<int>(), ReadyOn = o["readyOn"].As<LocalDate>().ToDateOnly(),
            Status = o["status"].As<string>(), PlanId = r["planId"] is string plan ? Guid.Parse(plan) : null, TruckId = r["truckId"] as string,
            CreatedAt = Utc(o["createdAt"]), UpdatedAt = Utc(o["updatedAt"])
        };
    }

    // ---------- trucks ----------

    public async Task<IReadOnlyList<Truck>> ListTrucksAsync(bool? active, CancellationToken ct) =>
        (await ReadAsync($"MATCH (t:Truck) WHERE $active IS NULL OR t.active = $active {TruckShape} RETURN {TruckColumns} ORDER BY t.id",
            new { active }, ct)).Select(ToTruck).ToList();

    public async Task<Truck?> GetTruckAsync(string id, CancellationToken ct) =>
        (await ReadAsync($"MATCH (t:Truck {{id: $id}}) {TruckShape} RETURN {TruckColumns}", new { id }, ct)).Select(ToTruck).SingleOrDefault();

    public async Task<IReadOnlyList<string>> TruckIdsAsync(CancellationToken ct) =>
        (await ReadAsync("MATCH (t:Truck) RETURN t.id AS id", null, ct)).Select(r => r["id"].As<string>()).ToList();

    public Task AddTruckAsync(Truck truck, CancellationToken ct) =>
        WriteAsync($"CREATE (t:Truck {{id: $id}}) SET t += $props WITH t {LinkTruck}", TruckParameters(truck), ct);

    public Task UpdateTruckAsync(Truck truck, CancellationToken ct) =>
        WriteAsync($"MATCH (t:Truck {{id: $id}}) SET t += $props WITH t {LinkTruck}", TruckParameters(truck), ct);

    private static object TruckParameters(Truck t) => new
    {
        id = t.Id, equipment = t.Equipment, location = t.CurrentLocation,
        props = new Dictionary<string, object?> { ["palletCapacity"] = t.PalletCapacity, ["weightCapacity"] = t.WeightCapacity, ["active"] = t.Active }
    };

    private static Truck ToTruck(IRecord r)
    {
        var t = r["t"].As<INode>().Properties;
        return new Truck
        {
            Id = t["id"].As<string>(), Equipment = r["equipment"].As<string>(), PalletCapacity = t["palletCapacity"].As<int>(),
            WeightCapacity = t["weightCapacity"].As<int>(), CurrentLocation = r["location"].As<string>(), Active = t["active"].As<bool>()
        };
    }

    // ---------- plans ----------

    public async Task<(IReadOnlyList<Order> Orders, IReadOnlyList<Truck> Trucks)> PlanningInputAsync(
        IReadOnlyCollection<string>? orderIds, IReadOnlyCollection<string>? truckIds, CancellationToken ct)
    {
        var parameters = new { orderIds = orderIds is { Count: > 0 } ? orderIds.ToList() : null, truckIds = truckIds is { Count: > 0 } ? truckIds.ToList() : null };
        var orders = await ReadAsync($$"""
            MATCH (o:Order {status: 'Open'}) WHERE $orderIds IS NULL OR o.id IN $orderIds
            {{OrderShape}}
            RETURN {{OrderColumns}}
            """, parameters, ct);
        var trucks = await ReadAsync($$"""
            MATCH (t:Truck {active: true}) WHERE $truckIds IS NULL OR t.id IN $truckIds
            {{TruckShape}}
            RETURN {{TruckColumns}}
            """, parameters, ct);
        return (orders.Select(ToOrder).ToList(), trucks.Select(ToTruck).ToList());
    }

    public async Task<Paged<PlanSummary>> ListPlansAsync(string? status, int page, int pageSize, CancellationToken ct)
    {
        var parameters = new { status, skip = (page - 1) * pageSize, take = pageSize };
        var total = (await ReadAsync("MATCH (p:Plan) WHERE $status IS NULL OR p.status = $status RETURN count(p) AS n", parameters, ct))[0]["n"].As<int>();
        var rows = await ReadAsync("""
            MATCH (p:Plan) WHERE $status IS NULL OR p.status = $status
            RETURN p ORDER BY p.createdAt DESC SKIP $skip LIMIT $take
            """, parameters, ct);
        var items = rows.Select(r => ToPlan(r["p"].As<INode>()))
            .Select(p => new PlanSummary(p.Id, p.CreatedAt, p.Status, p.DecidedAt, p.TruckCount, p.PlannedOrders, p.UnassignedOrders))
            .ToList();
        return new Paged<PlanSummary>(items, total, page, pageSize);
    }

    public async Task<Plan?> GetPlanAsync(Guid id, CancellationToken ct) =>
        (await ReadAsync("MATCH (p:Plan {id: $id}) RETURN p", new { id = id.ToString() }, ct)).Select(r => ToPlan(r["p"].As<INode>())).SingleOrDefault();

    public Task AddPlanAsync(Plan plan, CancellationToken ct) =>
        WriteAsync("CREATE (p:Plan {id: $id}) SET p += $props", new
        {
            id = plan.Id.ToString(),
            props = new Dictionary<string, object?>
            {
                ["createdAt"] = Zoned(plan.CreatedAt), ["status"] = plan.Status, ["truckCount"] = plan.TruckCount,
                ["plannedOrders"] = plan.PlannedOrders, ["unassignedOrders"] = plan.UnassignedOrders, ["resultJson"] = plan.ResultJson
            }
        }, ct);

    public async Task<PlanDecisionResult> CommitPlanAsync(Guid id, DateTime now, CancellationToken ct)
    {
        await using var session = Session();
        return await session.ExecuteWriteAsync(async tx =>
        {
            var plan = await LockPlanAsync(tx, id);
            if (plan is null) return new PlanDecisionResult(PlanDecision.NotFound);
            if (plan.Status != PlanStatuses.Draft) return new PlanDecisionResult(PlanDecision.AlreadyDecided, plan);

            var result = PlanningEndpoints.Read(plan);
            var assignments = result.Trucks.SelectMany(t => t.Orders.Select(o => new Dictionary<string, object> { ["orderId"] = o.Id, ["truckId"] = t.TruckId })).ToList();
            var parameters = new
            {
                orderIds = assignments.Select(a => a["orderId"]).ToList(),
                truckIds = result.Trucks.Select(t => t.TruckId).ToList()
            };

            // Lock the orders and trucks in the draft, then check nothing changed since it was built.
            var orders = (await (await tx.RunAsync($"""
                MATCH (o:Order) WHERE o.id IN $orderIds
                SET o._lock = true REMOVE o._lock
                WITH o
                {OrderShape}
                RETURN {OrderColumns}
                """, parameters)).ToListAsync()).Select(ToOrder).ToDictionary(o => o.Id);
            var trucks = (await (await tx.RunAsync($"""
                MATCH (t:Truck) WHERE t.id IN $truckIds
                SET t._lock = true REMOVE t._lock
                WITH t
                {TruckShape}
                RETURN {TruckColumns}
                """, parameters)).ToListAsync()).Select(ToTruck).ToDictionary(t => t.Id);

            var stale = PlanRules.FindStale(result, orders, trucks);
            if (stale.Count > 0) return new PlanDecisionResult(PlanDecision.Stale, plan, string.Join("; ", stale));

            await (await tx.RunAsync("""
                MATCH (p:Plan {id: $planId})
                SET p.status = 'Committed', p.decidedAt = $now
                WITH p
                UNWIND $assignments AS a
                MATCH (o:Order {id: a.orderId}), (t:Truck {id: a.truckId})
                SET o.status = 'Planned', o.updatedAt = $now
                CREATE (p)-[:INCLUDES]->(o), (o)-[:ASSIGNED_TO]->(t)
                """, new { planId = id.ToString(), now = Zoned(now), assignments })).ConsumeAsync();

            plan.Status = PlanStatuses.Committed;
            plan.DecidedAt = now;
            return new PlanDecisionResult(PlanDecision.Done, plan);
        });
    }

    public async Task<PlanDecisionResult> DiscardPlanAsync(Guid id, DateTime now, CancellationToken ct)
    {
        await using var session = Session();
        return await session.ExecuteWriteAsync(async tx =>
        {
            var plan = await LockPlanAsync(tx, id);
            if (plan is null) return new PlanDecisionResult(PlanDecision.NotFound);
            if (plan.Status != PlanStatuses.Draft) return new PlanDecisionResult(PlanDecision.AlreadyDecided, plan);
            await (await tx.RunAsync("MATCH (p:Plan {id: $id}) SET p.status = 'Discarded', p.decidedAt = $now",
                new { id = id.ToString(), now = Zoned(now) })).ConsumeAsync();
            plan.Status = PlanStatuses.Discarded;
            plan.DecidedAt = now;
            return new PlanDecisionResult(PlanDecision.Done, plan);
        });
    }

    private static async Task<Plan?> LockPlanAsync(IAsyncQueryRunner tx, Guid id)
    {
        var rows = await (await tx.RunAsync("MATCH (p:Plan {id: $id}) SET p._lock = true REMOVE p._lock RETURN p", new { id = id.ToString() })).ToListAsync();
        return rows.Count == 0 ? null : ToPlan(rows[0]["p"].As<INode>());
    }

    private static Plan ToPlan(INode node)
    {
        var p = node.Properties;
        return new Plan
        {
            Id = Guid.Parse(p["id"].As<string>()), CreatedAt = Utc(p["createdAt"]), Status = p["status"].As<string>(),
            DecidedAt = p.TryGetValue("decidedAt", out var decided) && decided is not null ? Utc(decided) : null,
            TruckCount = p["truckCount"].As<int>(), PlannedOrders = p["plannedOrders"].As<int>(),
            UnassignedOrders = p["unassignedOrders"].As<int>(), ResultJson = p["resultJson"].As<string>()
        };
    }

    // ---------- yard ----------

    public async Task<IReadOnlyList<Order>> YardCandidatesAsync(int maxPallets, string? equipment, int take, CancellationToken ct) =>
        (await ReadAsync($$"""
            MATCH (o:Order {status: 'Open'})-[:REQUIRES]->(e:Equipment)
            WHERE o.pallets <= $maxPallets AND ($equipment IS NULL OR toLower(e.name) = $equipment)
            WITH o ORDER BY o.priority DESC, o.pallets DESC, o.id LIMIT $take
            {{OrderShape}}
            RETURN {{OrderColumns}} ORDER BY o.priority DESC, o.pallets DESC, o.id
            """, new { maxPallets, equipment = equipment?.ToLowerInvariant(), take }, ct)).Select(ToOrder).ToList();

    public async Task<bool> AcceptYardEventAsync(YardEvent evt, string trailerStatus, CancellationToken ct)
    {
        try
        {
            await using var session = Session();
            return await session.ExecuteWriteAsync(async tx =>
            {
                var existing = await (await tx.RunAsync("MATCH (e:YardEvent {eventId: $id}) RETURN count(e) AS n", new { id = evt.EventId.ToString() })).SingleAsync();
                if (existing["n"].As<long>() > 0) return false;

                // An older event (Yard's outbox retries can arrive late) never overwrites a newer trailer status.
                await (await tx.RunAsync("""
                    MERGE (t:Trailer {trailerNumber: $trailerNumber})
                    CREATE (e:YardEvent {eventId: $id})-[:ABOUT]->(t)
                    SET e += $props
                    WITH t
                    WHERE t.lastEventAt IS NULL OR $occurredAt >= t.lastEventAt
                    SET t.status = $status, t.lastEventType = $eventType, t.lastEventAt = $occurredAt
                    """, new
                {
                    trailerNumber = evt.TrailerNumber, id = evt.EventId.ToString(), status = trailerStatus, eventType = evt.EventType,
                    occurredAt = Zoned(evt.OccurredAt),
                    props = new Dictionary<string, object?>
                    {
                        ["eventType"] = evt.EventType, ["occurredAt"] = Zoned(evt.OccurredAt), ["details"] = evt.Details,
                        ["schemaVersion"] = evt.SchemaVersion, ["receivedAt"] = Zoned(evt.ReceivedAt)
                    }
                })).ConsumeAsync();
                return true;
            });
        }
        catch (ClientException ex) when (ex.Code == ConstraintFailed)
        {
            // Two deliveries of the same event raced; the other one won.
            return false;
        }
    }

    public async Task<IReadOnlyList<YardEvent>> RecentYardEventsAsync(int take, CancellationToken ct) =>
        (await ReadAsync("""
            MATCH (e:YardEvent)-[:ABOUT]->(t:Trailer)
            RETURN e, t.trailerNumber AS trailerNumber ORDER BY e.occurredAt DESC LIMIT $take
            """, new { take }, ct)).Select(r =>
        {
            var e = r["e"].As<INode>().Properties;
            return new YardEvent
            {
                EventId = Guid.Parse(e["eventId"].As<string>()), EventType = e["eventType"].As<string>(), TrailerNumber = r["trailerNumber"].As<string>(),
                OccurredAt = Utc(e["occurredAt"]), Details = e.TryGetValue("details", out var details) ? details as string : null,
                SchemaVersion = e["schemaVersion"].As<int>(), ReceivedAt = Utc(e["receivedAt"])
            };
        }).ToList();

    public async Task<IReadOnlyList<YardTrailer>> YardTrailersAsync(CancellationToken ct) =>
        (await ReadAsync("MATCH (t:Trailer) RETURN t ORDER BY t.lastEventAt DESC", null, ct)).Select(r =>
        {
            var t = r["t"].As<INode>().Properties;
            return new YardTrailer
            {
                TrailerNumber = t["trailerNumber"].As<string>(), Status = t["status"].As<string>(),
                LastEventType = t["lastEventType"].As<string>(), LastEventAt = Utc(t["lastEventAt"])
            };
        }).ToList();

    // ---------- overview ----------

    public async Task<DashboardData> DashboardAsync(CancellationToken ct)
    {
        var orders = await ReadAsync($"MATCH (o:Order) {OrderShape} RETURN {OrderColumns}", null, ct);
        var trucks = await ListTrucksAsync(true, ct);
        var plans = await ReadAsync("MATCH (p:Plan) RETURN p.status AS status", null, ct);
        var trailers = await ReadAsync("MATCH (t:Trailer) RETURN t.status AS status", null, ct);
        return new DashboardData(orders.Select(ToOrder).ToList(), trucks,
            plans.Select(r => r["status"].As<string>()).ToList(), trailers.Select(r => r["status"].As<string>()).ToList());
    }

    /// <summary>
    /// Open freight per lane and the active trucks already positioned at the lane's origin with matching equipment:
    /// one traversal from each origin Location through Equipment to Trucks.
    /// </summary>
    public async Task<IReadOnlyList<LaneSummary>> LanesAsync(CancellationToken ct)
    {
        var rows = await ReadAsync("""
            MATCH (o:Order {status: 'Open'})-[:SHIPS_FROM]->(f:Location), (o)-[:SHIPS_TO]->(d:Location), (o)-[:REQUIRES]->(eq:Equipment)
            WITH f, d, count(o) AS orders, sum(o.pallets) AS pallets, sum(o.weight) AS weight, collect(DISTINCT eq) AS equipment
            CALL (f, equipment) {
              OPTIONAL MATCH (t:Truck {active: true})-[:LOCATED_AT]->(f)
              WHERE EXISTS { MATCH (t)-[:HAS_EQUIPMENT]->(e) WHERE e IN equipment }
              RETURN collect(t.id) AS trucks
            }
            RETURN f.name AS origin, d.name AS destination, orders, pallets, weight, [e IN equipment | e.name] AS equipment, trucks
            """, null, ct);
        return rows
            .Select(r => new LaneSummary(r["origin"].As<string>(), r["destination"].As<string>(), r["orders"].As<int>(), r["pallets"].As<int>(),
                r["weight"].As<int>(), r["equipment"].As<List<string>>().Order(StringComparer.Ordinal).ToList(),
                r["trucks"].As<List<string>>().Order(StringComparer.Ordinal).ToList()))
            .OrderByDescending(l => l.OpenPallets).ThenBy(l => l.Origin, StringComparer.Ordinal).ThenBy(l => l.Destination, StringComparer.Ordinal)
            .ToList();
    }

    // ---------- plumbing ----------

    private IAsyncSession Session() => connection.Driver.AsyncSession(o =>
    {
        if (connection.Database is { } db) o.WithDatabase(db);
    });

    private async Task<IReadOnlyList<IRecord>> ReadAsync(string cypher, object? parameters, CancellationToken ct) =>
        (await connection.Driver.ExecutableQuery(cypher).WithParameters(parameters ?? new { })
            .WithConfig(new QueryConfig(RoutingControl.Readers, connection.Database)).ExecuteAsync(ct)).Result;

    private async Task<IReadOnlyList<IRecord>> WriteAsync(string cypher, object? parameters, CancellationToken ct) =>
        (await connection.Driver.ExecutableQuery(cypher).WithParameters(parameters ?? new { })
            .WithConfig(new QueryConfig(RoutingControl.Writers, connection.Database)).ExecuteAsync(ct)).Result;

    private static ZonedDateTime Zoned(DateTime utc) => new(new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)));

    private static DateTime Utc(object value) => value.As<ZonedDateTime>().ToDateTimeOffset().UtcDateTime;
}
