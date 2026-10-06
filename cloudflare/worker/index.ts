import { Container } from "@cloudflare/containers";

export interface Env {
  ASSETS: Fetcher;
  API: DurableObjectNamespace<LtlApi>;
  YARD_LTL_SIGNING_KEY?: string;
  DATABASE_URL?: string;
  MIGRATE_ON_STARTUP?: string;
  DEMO_RESET_TOKEN?: string;
  ALVYS_CLIENT_ID?: string;
  ALVYS_CLIENT_SECRET?: string;
}

export class LtlApi extends Container<Env> {
  defaultPort = 8080;
  sleepAfter = "10m";
  pingEndpoint = "localhost/health";

  constructor(ctx: DurableObjectState<{}>, env: Env) {
    super(ctx, env);
    this.envVars = {
      ASPNETCORE_ENVIRONMENT: "Production",
      Integration__YardSigningKey: env.YARD_LTL_SIGNING_KEY ?? "portfolio-bootstrap-only",
      // Without DATABASE_URL the API runs on a throwaway demo database.
      ...(env.DATABASE_URL ? { ConnectionStrings__Default: env.DATABASE_URL } : {}),
      // "false" when the deploy pipeline applies migrations as the owner (DATABASE_URL_UNPOOLED).
      Database__MigrateOnStartup: env.MIGRATE_ON_STARTUP ?? "true",
      ...(env.DEMO_RESET_TOKEN ? { Demo__ResetToken: env.DEMO_RESET_TOKEN } : {}),
      Alvys__Mode: env.ALVYS_CLIENT_ID && env.ALVYS_CLIENT_SECRET ? "Live" : "Demo",
      ...(env.ALVYS_CLIENT_ID ? { Alvys__ClientId: env.ALVYS_CLIENT_ID } : {}),
      ...(env.ALVYS_CLIENT_SECRET ? { Alvys__ClientSecret: env.ALVYS_CLIENT_SECRET } : {}),
    };
  }
}

const api = (env: Env) => env.API.getByName("api");

export default {
  async fetch(request: Request, env: Env): Promise<Response> {
    const url = new URL(request.url);
    if (url.pathname.startsWith("/api/") || url.pathname === "/health" || url.pathname.startsWith("/health/")) {
      return api(env).fetch(request);
    }
    return env.ASSETS.fetch(request);
  },

  // Crons (wrangler.template.jsonc): the nightly demo reset, and a business-hours
  // keep-warm ping so the first visitor does not wait on a cold container and a suspended database.
  async scheduled(controller: ScheduledController, env: Env, ctx: ExecutionContext): Promise<void> {
    const request = scheduledRequest(controller.cron, env);
    if (!request) return;
    ctx.waitUntil(
      api(env).fetch(request).then(response => {
        if (!response.ok) console.error(`Cron "${controller.cron}" ${new URL(request.url).pathname} failed: ${response.status}`);
      }),
    );
  },
} satisfies ExportedHandler<Env>;

const RESET_CRON = "23 8 * * *";
// Every 5 minutes, 14:00-23:55 UTC Monday-Friday (Cloudflare numbers weekdays from 1 = Sunday, so use names): 8am-6pm Mountain daylight time (7am-5pm standard).
const WARM_CRON = "*/5 14-23 * * MON-FRI";

function scheduledRequest(cron: string, env: Env): Request | null {
  switch (cron) {
    case RESET_CRON:
      return env.DEMO_RESET_TOKEN
        ? new Request("http://api/api/admin/reset-demo", { method: "POST", headers: { "X-Demo-Reset-Token": env.DEMO_RESET_TOKEN } })
        : null;
    case WARM_CRON:
      // /health/ready goes through the database gate, so it wakes Neon as well as the container.
      return new Request("http://api/health/ready");
    default:
      return null;
  }
}
