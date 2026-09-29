import { Container } from "@cloudflare/containers";

export interface Env {
  ASSETS: Fetcher;
  API: DurableObjectNamespace<LtlApi>;
  YARD_LTL_SIGNING_KEY?: string;
  DATABASE_URL?: string;
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

  // Daily reset of the public demo's orders and trucks (see triggers.crons in wrangler.template.jsonc).
  async scheduled(_controller: ScheduledController, env: Env, ctx: ExecutionContext): Promise<void> {
    if (!env.DEMO_RESET_TOKEN) return;
    ctx.waitUntil(
      api(env)
        .fetch(new Request("http://api/api/admin/reset-demo", {
          method: "POST",
          headers: { "X-Demo-Reset-Token": env.DEMO_RESET_TOKEN },
        }))
        .then(response => {
          if (!response.ok) console.error(`Demo reset failed: ${response.status}`);
        }),
    );
  },
} satisfies ExportedHandler<Env>;
