# LTL Planner

[![CI](https://github.com/poker-kid-100717/ltl-planner/actions/workflows/ci.yml/badge.svg)](https://github.com/poker-kid-100717/ltl-planner/actions/workflows/ci.yml)

A clean-room portfolio application that groups LTL shipment orders onto compatible truck profiles under hard pallet, weight, and equipment constraints, and explains every assignment it makes.

It is the standalone version of the LTL Planner from [logistics-portfolio-suite](https://github.com/poker-kid-100717/logistics-portfolio-suite), and pairs with [yard-ops](https://github.com/poker-kid-100717/yard-ops) over a signed, versioned HTTP contract.

## What it does

A working planning tool. Everything below reads and writes a real database.

| Menu | Pages |
| --- | --- |
| **Overview** | Dashboard: open freight against active capacity by equipment, plans, and trailers Yard Ops reports |
| **Planning** | Orders (create, edit, cancel, dispatch) · Trucks (add, edit, take out of service) · Plan Builder (pick orders and trucks, build a draft, review the explanation, commit or discard) · Plans (history and detail) |
| **Yard** | Yard Feed (signed events from Yard Ops and the latest status of each trailer) · Loads (read-only TMS loads) |

Settings sits at the bottom of the menu.

Rules the API enforces: only open orders are planned or edited; committing a draft marks its orders Planned on their trucks in one transaction, and returns 409 without changing anything if an order or truck changed after the draft was built; only planned orders can be dispatched; Yard events are stored once per event id, and a late event never overwrites a newer trailer status.

## Demonstrates

- deterministic, explainable planning: hard constraints are enforced before any scoring, and every order the planner cannot place gets a reason
- .NET 10 minimal API with EF Core 10 on PostgreSQL (migrations applied by the deploy pipeline as the owner; the running app connects as a least-privilege role), ProblemDetails validation and 409s for rule violations
- Angular 22 routed app: lazy-loaded pages, signals, one accessible drawer for create/edit forms, light and dark themes, phone-width layout
- the Yard-facing v1 contract: candidate lookup (`GET /api/integrations/v1/yard/candidates`) and idempotent event ingestion (`POST /api/integrations/v1/yard/events`), verified with an HMAC-SHA256 signature over a timestamp and the body (five-minute replay window), now persisted
- public-demo safeguards: per-client write rate limits (Yard's signed calls are exempt), body size limits, a daily reset from a Cloudflare cron trigger
- optional read-only Alvys Loads Search through an OAuth 2.0 client-credentials adapter
- integration tests against both SQLite and PostgreSQL in CI
- Cloudflare Workers + Containers hosting deployed from GitHub Actions

The planner uses a deliberately understandable best-fit heuristic rather than a copied operational algorithm. See [docs/architecture.md](docs/architecture.md).

## Run locally

```bash
cp .env.example .env
docker compose up --build
```

Compose starts PostgreSQL too; the API applies migrations and seeds fictional orders and trucks on first start.

- UI: http://localhost:4202
- API: http://localhost:5102 (health at `/health`)

Without Docker:

```bash
ASPNETCORE_URLS=http://localhost:5102 dotnet run --project api   # no DATABASE_URL: throwaway SQLite demo store
cd web && npm install && npm start   # UI on http://localhost:4202; /api proxies to :5102
```

Demo mode is the default and needs no credentials. To enable live, read-only Alvys reads set `ALVYS_MODE=Live`, `ALVYS_CLIENT_ID`, and `ALVYS_CLIENT_SECRET`. Credentials stay server-side; the Angular app never sees them.

## Tests

```bash
dotnet test tests/Portfolio.Ltl.Api.Tests.csproj                                   # SQLite
TEST_DATABASE_URL=postgres://user:pass@localhost:5432/ltl_test dotnet test tests/Portfolio.Ltl.Api.Tests.csproj  # PostgreSQL
```

The integration tests start the real API and cover the planning rules and the Yard contract (shapes, signatures, idempotency, schema versions).

## Deploy to Cloudflare

Deployment runs from GitHub Actions on every push to `main` (`.github/workflows/deploy-cloudflare.yml`). It builds the Angular app, deploys a Worker that serves it from the edge, runs the .NET API in a Cloudflare Container, and smoke-tests the result.

Repository **secrets**:

| Secret | Required | Purpose |
| --- | --- | --- |
| `CLOUDFLARE_API_TOKEN` | yes | Wrangler deploys |
| `CLOUDFLARE_ACCOUNT_ID` | yes | Wrangler deploys |
| `DATABASE_URL` | recommended | PostgreSQL URL the running app uses: a Neon **pooled** URL ending in `?sslmode=require`, for a role with data rights only (see below). Without it the app runs on a demo database that resets whenever the container restarts. |
| `DATABASE_URL_UNPOOLED` | recommended | The owner's **direct** (non-pooled) URL. The deploy applies migrations with it before the new container starts. Without it the app migrates itself on startup (and then needs DDL rights). |
| `DEMO_RESET_TOKEN` | no | Token for `POST /api/admin/reset-demo`. The demo orders and trucks reset nightly at 08:23 UTC; a per-deploy token is generated when unset. |
| `YARD_LTL_SIGNING_KEY` | recommended | Verifies Yard Ops events. Must equal the key in the yard-ops repo. Signatures cover an `X-Portfolio-Timestamp` header and the body; LTL rejects anything more than five minutes old. Generated per deploy when absent. |
| `ALVYS_CLIENT_ID` / `ALVYS_CLIENT_SECRET` | no | Live, read-only Alvys mode |

Database roles (Neon or any PostgreSQL): the owner role in `DATABASE_URL_UNPOOLED` owns the schema; the app role in `DATABASE_URL` only reads and writes rows:

```sql
CREATE ROLE ltl_app LOGIN PASSWORD '...';
GRANT CONNECT ON DATABASE ltl TO ltl_app;
GRANT USAGE ON SCHEMA public TO ltl_app;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO ltl_app;
ALTER DEFAULT PRIVILEGES FOR ROLE ltl_owner IN SCHEMA public GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO ltl_app;
ALTER DEFAULT PRIVILEGES FOR ROLE ltl_owner IN SCHEMA public GRANT USAGE, SELECT ON SEQUENCES TO ltl_app;
```

Cloudflare cron triggers handle the nightly reset and a keep-warm ping to `/health/ready` every five minutes during weekday business hours (14:00-23:55 UTC), so the first visitor does not wait for a cold container and a suspended database.

Repository **variable** (optional):

| Variable | Purpose |
| --- | --- |
| `APP_HOST` | Custom domain such as `ltl.example.com`. When unset the app is served from its `workers.dev` URL. |

Until the Cloudflare secrets exist the deploy job skips cleanly. `scripts/cloudflare-deploy.sh` can also be run locally with the same environment variables.

## Repository structure

```text
api/          .NET 10 API: Data/ (EF Core model, migrations, demo seed), Endpoints/, planner, Yard contract, Alvys adapter
tests/        xUnit unit and integration tests
web/          Angular 22 UI
cloudflare/   Worker + Container definition for Cloudflare hosting
scripts/      deploy and publication-safety scripts
docs/         architecture, Alvys public API notes, clean-room boundary
```

## Clean-room boundary

Written from generic workflow requirements and public vendor documentation. It contains no former-employer source code, data, credentials, internal URLs, customer names, or company-specific business rules. See [docs/clean-room-boundary.md](docs/clean-room-boundary.md) and [NOTICE.md](NOTICE.md).

## License

MIT
