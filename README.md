# LTL Planner

[![CI](https://github.com/poker-kid-100717/ltl-planner/actions/workflows/ci.yml/badge.svg)](https://github.com/poker-kid-100717/ltl-planner/actions/workflows/ci.yml)

A clean-room portfolio application that groups LTL shipment orders onto compatible truck profiles under hard pallet, weight, and equipment constraints, and explains every assignment it makes.

It is the standalone version of the LTL Planner from [logistics-portfolio-suite](https://github.com/poker-kid-100717/logistics-portfolio-suite), and pairs with [yard-ops](https://github.com/poker-kid-100717/yard-ops) over a signed, versioned HTTP contract.

## Demonstrates

- deterministic, explainable planning: hard constraints are enforced before any scoring
- .NET 10 minimal API with typed service boundaries
- Angular 22 standalone planning workspace with signal-based state
- optional read-only Alvys Loads Search through an OAuth 2.0 client-credentials adapter
- a Yard-facing candidate endpoint (`GET /api/integrations/v1/yard/candidates`)
- HMAC-SHA256-verified, idempotent Yard event ingestion (`POST /api/integrations/v1/yard/events`)
- Cloudflare Workers + Containers hosting deployed from GitHub Actions

The planner uses a deliberately understandable best-fit heuristic rather than a copied operational algorithm. See [docs/architecture.md](docs/architecture.md).

## Run locally

```bash
cp .env.example .env
docker compose up --build
```

- UI: http://localhost:4202
- API: http://localhost:5102 (health at `/health`)

Without Docker:

```bash
ASPNETCORE_URLS=http://localhost:5102 dotnet run --project api
cd web && npm install && npm start   # UI on http://localhost:4202; /api proxies to :5102
```

Demo mode is the default and needs no credentials. To enable live, read-only Alvys reads set `ALVYS_MODE=Live`, `ALVYS_CLIENT_ID`, and `ALVYS_CLIENT_SECRET`. Credentials stay server-side; the Angular app never sees them.

## Tests

```bash
dotnet test tests/Portfolio.Ltl.Api.Tests.csproj
```

## Deploy to Cloudflare

Deployment runs from GitHub Actions on every push to `main` (`.github/workflows/deploy-cloudflare.yml`). It builds the Angular app, deploys a Worker that serves it from the edge, runs the .NET API in a Cloudflare Container, and smoke-tests the result.

Repository **secrets**:

| Secret | Required | Purpose |
| --- | --- | --- |
| `CLOUDFLARE_API_TOKEN` | yes | Wrangler deploys |
| `CLOUDFLARE_ACCOUNT_ID` | yes | Wrangler deploys |
| `YARD_LTL_SIGNING_KEY` | recommended | Verifies Yard Ops events. Must equal the key in the yard-ops repo. Generated per deploy when absent. |
| `ALVYS_CLIENT_ID` / `ALVYS_CLIENT_SECRET` | no | Live, read-only Alvys mode |

Repository **variable** (optional):

| Variable | Purpose |
| --- | --- |
| `APP_HOST` | Custom domain such as `ltl.example.com`. When unset the app is served from its `workers.dev` URL. |

Until the Cloudflare secrets exist the deploy job skips cleanly. `scripts/cloudflare-deploy.sh` can also be run locally with the same environment variables.

## Repository structure

```text
api/          .NET 10 API (planning, Yard integration contract, Alvys adapter)
tests/        xUnit tests for the planner, signatures, and event inbox
web/          Angular 22 UI
cloudflare/   Worker + Container definition for Cloudflare hosting
scripts/      deploy and publication-safety scripts
docs/         architecture, Alvys public API notes, clean-room boundary
```

## Clean-room boundary

Written from generic workflow requirements and public vendor documentation. It contains no former-employer source code, data, credentials, internal URLs, customer names, or company-specific business rules. See [docs/clean-room-boundary.md](docs/clean-room-boundary.md) and [NOTICE.md](NOTICE.md).

## License

MIT
