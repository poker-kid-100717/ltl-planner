# Architecture

```mermaid
flowchart LR
  WEB[Angular 22 UI] --> API[LTL Planner .NET 10 API]
  API --> DB[(Neo4j)]
  API -->|read-only loads| ALVYS[Alvys Public API]
  YARD[Yard Ops API] -->|GET planning candidates| API
  YARD -->|signed outbox event| API
```

## Boundary

LTL Planner owns internal shipment planning, capacity validation, and explainable assignment results. It exposes a narrow, versioned integration contract to Yard Ops rather than sharing tables or domain entities.

## Data model

Planning data is a graph in Neo4j (`api/Data/Neo4jLtlStore.cs`), behind the `ILtlStore` interface the endpoints use:

```
(:Order)-[:SHIPS_FROM]->(:Location)<-[:LOCATED_AT]-(:Truck)
(:Order)-[:SHIPS_TO]->(:Location)
(:Order)-[:REQUIRES]->(:Equipment)<-[:HAS_EQUIPMENT]-(:Truck)
(:Plan)-[:INCLUDES]->(:Order)-[:ASSIGNED_TO]->(:Truck)        once a plan is committed
(:YardEvent)-[:ABOUT]->(:Trailer)
```

Uniqueness constraints (order, truck, plan, location, equipment, event id, trailer) are created at startup and do the job migrations and primary keys did before: they make Yard event ingestion idempotent and catch racing writes. Questions that cross entities are traversals: `GET /api/lanes` walks from each lane's origin `Location` through the required `Equipment` to the active `Truck`s already there. Writes that must see a consistent state (status moves, plan commits) lock the nodes they read first, so a commit checks and updates its orders and trucks in one transaction.

Without `DATABASE_URL` the API uses a throwaway SQLite database (`SqliteLtlStore`, EF Core) so the demo still runs with nothing configured. The integration tests run against both stores.

A plan stores the planner's full result (trucks, orders, explanations, unplaced orders and reasons) as a JSON snapshot. It is always read back whole, and the snapshot is what a commit is checked against.

## Plan lifecycle

1. **Build** plans open orders onto active trucks and saves a Draft. Nothing else changes.
2. **Commit** runs in one transaction: every order must still be Open with the same pallets, weight and equipment, and every truck still active with the same capacity. Then the orders become Planned on their trucks. Otherwise it returns 409, lists what changed, and changes nothing.
3. **Discard** closes a draft. Planned orders can then be **dispatched**; open orders can be edited or **cancelled**.

## Planning

`PlannerService` orders unassigned shipments by priority, pallets, and weight, then places each one on the compatible truck with the best score. Compatibility (equipment, pallet capacity, weight capacity) is a hard filter applied before scoring; scoring only ranks trucks that can legally take the shipment. Each planned truck carries a plain-language explanation of why its orders were grouped, and every order that could not be placed gets a reason: no active truck with that equipment, too large for any such truck, or every such truck already full with higher-priority orders.

## Yard integration contract

Exposed under `/api/integrations/v1/`:

- `GET yard/candidates?trailerNumber=&equipment=&maxPallets=`: open orders that fit a trailer's declared equipment and pallet capacity. Final truck/route validation stays in LTL.
- `POST yard/events`: Yard state-change events. The raw body must carry an `X-Portfolio-Signature` HMAC-SHA256 header computed with the shared `YARD_LTL_SIGNING_KEY`. Events are stored once, keyed by `eventId` in the database, and each accepted event updates the trailer view in the same transaction (an event older than the trailer's latest one never overwrites its status); unknown `schemaVersion` values are rejected at the edge.
- `GET yard/events`: the received event inbox, shown on the operations screen.

## Failure behavior

- External Alvys calls have bounded timeouts/resilience and surface degraded state instead of fabricating data.
- Unsigned or incorrectly signed events are rejected with `401`.
- Duplicate events are acknowledged but not re-applied.

## Cloudflare topology

```text
        Cloudflare edge
              |
   <APP_HOST> or ltl-planner.<account>.workers.dev
              |
     Worker + Angular assets
              |  /api/*, /health
              v
     LTL .NET 10 Container
```

All state lives in Neo4j, so nothing is lost when the container sleeps after 10 minutes without traffic.
