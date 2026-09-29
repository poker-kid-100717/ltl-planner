#!/usr/bin/env bash
# Builds the Angular app, renders the Wrangler config, and deploys the Worker +
# .NET Container to Cloudflare. Used by GitHub Actions; also runnable locally.
#
# Required (unless VALIDATE_ONLY=true): CLOUDFLARE_API_TOKEN, CLOUDFLARE_ACCOUNT_ID
# Optional:
#   APP_HOST              custom hostname (for example ltl.example.com). When empty the
#                         Worker is served from its workers.dev URL only.
#   DATABASE_URL          required Neon pooled PostgreSQL URL\n#   DEMO_RESET_TOKEN      required token for the scheduled public-demo reset\n#   YARD_LTL_SIGNING_KEY  shared HMAC key; must match the Yard Ops deployment.
#                         A per-deployment key is generated when empty.
#   ALVYS_CLIENT_ID / ALVYS_CLIENT_SECRET  enable live, read-only Alvys mode.
#   VALIDATE_ONLY=true    build and run `wrangler deploy --dry-run` without contacting Cloudflare.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CF="$ROOT/cloudflare"
VALIDATE_ONLY="${VALIDATE_ONLY:-false}"
APP_HOST="${APP_HOST:-}"

if [ "$VALIDATE_ONLY" != "true" ]; then
  : "${CLOUDFLARE_API_TOKEN:?CLOUDFLARE_API_TOKEN is required}"
  : "${CLOUDFLARE_ACCOUNT_ID:?CLOUDFLARE_ACCOUNT_ID is required}"
fi

if [ -z "${YARD_LTL_SIGNING_KEY:-}" ]; then
  YARD_LTL_SIGNING_KEY="$(openssl rand -hex 32)"
  echo "::notice::YARD_LTL_SIGNING_KEY is not set; generated a per-deployment key. Signed Yard events will be rejected until Yard Ops uses the same key."
fi
if [ "$VALIDATE_ONLY" != "true" ]; then\n  : "${DATABASE_URL:?DATABASE_URL is required}"\n  : "${DEMO_RESET_TOKEN:?DEMO_RESET_TOKEN is required}"\nfi\nDATABASE_URL="${DATABASE_URL:-postgres://demo:demo@localhost:5432/ltl?sslmode=require}"\nDEMO_RESET_TOKEN="${DEMO_RESET_TOKEN:-dry-run-reset-token}"\nexport DATABASE_URL DEMO_RESET_TOKEN YARD_LTL_SIGNING_KEY APP_HOST

echo "==> Building Angular app"
npm install --prefix "$ROOT/web"
npm run build --prefix "$ROOT/web"

echo "==> Preparing Cloudflare Worker"
npm install --prefix "$CF"

node - "$CF/wrangler.template.jsonc" "$CF/wrangler.generated.jsonc" <<'NODE'
const fs = require("fs");
const [template, output] = process.argv.slice(2);
const config = JSON.parse(fs.readFileSync(template, "utf8"));
if (process.env.APP_HOST) {
  config.routes = [{ pattern: process.env.APP_HOST, custom_domain: true }];
}
fs.writeFileSync(output, JSON.stringify(config, null, 2) + "\n");
NODE

SECRETS_FILE="$(mktemp)"
trap 'rm -f "$SECRETS_FILE"' EXIT
chmod 600 "$SECRETS_FILE"
node > "$SECRETS_FILE" <<'NODE'
const secrets = { DATABASE_URL: process.env.DATABASE_URL, DEMO_RESET_TOKEN: process.env.DEMO_RESET_TOKEN, YARD_LTL_SIGNING_KEY: process.env.YARD_LTL_SIGNING_KEY };
if (process.env.ALVYS_CLIENT_ID && process.env.ALVYS_CLIENT_SECRET) {
  secrets.ALVYS_CLIENT_ID = process.env.ALVYS_CLIENT_ID;
  secrets.ALVYS_CLIENT_SECRET = process.env.ALVYS_CLIENT_SECRET;
}
process.stdout.write(JSON.stringify(secrets));
NODE

cd "$CF"
if [ "$VALIDATE_ONLY" = "true" ]; then
  npx wrangler deploy --dry-run --outdir "${RUNNER_TEMP:-/tmp}/wrangler-dry-run" \
    --config wrangler.generated.jsonc --secrets-file "$SECRETS_FILE"
  echo "Cloudflare dry-run passed."
  exit 0
fi

DEPLOY_LOG="$(mktemp)"
npx wrangler deploy --config wrangler.generated.jsonc --secrets-file "$SECRETS_FILE" | tee "$DEPLOY_LOG"

if [ -n "$APP_HOST" ]; then
  APP_URL="https://$APP_HOST"
else
  APP_URL="$(grep -oE 'https://[a-z0-9.-]+\.workers\.dev' "$DEPLOY_LOG" | head -1)"
fi
: "${APP_URL:?could not determine the deployed URL}"

wait_for() {
  url="$1"
  pattern="$2"
  for attempt in $(seq 1 18); do
    body="$(curl --fail --silent --show-error --retry 2 --retry-delay 2 --retry-all-errors "$url" 2>/dev/null || true)"
    if printf '%s' "$body" | grep -q "$pattern"; then
      return 0
    fi
    echo "Waiting for $url (attempt $attempt/18)..."
    sleep 10
  done
  echo "::error::Smoke test failed: $url"
  return 1
}

echo "==> Smoke testing $APP_URL"
wait_for "$APP_URL/health" "Healthy"
wait_for "$APP_URL/" "<app-root"
wait_for "$APP_URL/api/integrations/v1/yard/candidates?trailerNumber=SMOKE&equipment=Dry%20Van&maxPallets=26" "\["

echo "Deployed: $APP_URL"
if [ -n "${GITHUB_OUTPUT:-}" ]; then echo "url=$APP_URL" >> "$GITHUB_OUTPUT"; fi
if [ -n "${GITHUB_STEP_SUMMARY:-}" ]; then echo "LTL Planner deployed: $APP_URL" >> "$GITHUB_STEP_SUMMARY"; fi
