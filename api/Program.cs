using System.Threading.RateLimiting;
using Microsoft.Extensions.Http.Resilience;
using Portfolio.Ltl.Api.Data;
using Portfolio.Ltl.Api.Endpoints;
using Portfolio.Ltl.Api.Integrations.Alvys;
using Portfolio.Ltl.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 64 * 1024);
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<DatabaseExceptionHandler>();
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddLtlDatabase(builder.Configuration);
builder.Services.AddSingleton<DemoSeeder>();
builder.Services.AddSingleton<DatabaseGate>();
builder.Services.AddSingleton<PlannerService>();

builder.Services.Configure<AlvysOptions>(builder.Configuration.GetSection(AlvysOptions.Section));
builder.Services.AddSingleton<AlvysTokenProvider>();
builder.Services.AddSingleton<IExternalLoadReader, AlvysLoadReader>();

builder.Services.AddHttpClient(AlvysTokenProvider.AuthClient)
    .AddStandardResilienceHandler(options =>
    {
        options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(8);
        options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(20);
    });

builder.Services.AddHttpClient(AlvysLoadReader.ApiClient)
    .AddStandardResilienceHandler(options =>
    {
        options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(12);
        options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(30);
    });

// Anonymous public demo: limit writes per client (Cloudflare supplies the caller's IP).
// Yard Ops' signed events are service-to-service traffic and are not counted.
var writesPerMinute = builder.Configuration.GetValue("RateLimiting:WritesPerMinute", 30);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        if (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method) ||
            context.Request.Path.StartsWithSegments("/api/integrations"))
            return RateLimitPartition.GetNoLimiter("unlimited");
        var client = context.Request.Headers["CF-Connecting-IP"].FirstOrDefault()
                     ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(client, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = writesPerMinute,
            Window = TimeSpan.FromMinutes(1)
        });
    });
});

var app = builder.Build();
app.UseExceptionHandler();
app.UseRateLimiter();
if (app.Environment.IsDevelopment()) app.MapOpenApi();

await Database.InitializeAsync(app.Services);

app.MapGet("/health", () => Results.Ok(new { status = "Healthy", service = "ltl-planner" }));
app.MapGet("/health/ready", async (DatabaseGate gate, DatabaseStatus database, CancellationToken ct) =>
{
    var ready = await gate.EnsureReadyAsync(ct);
    var body = new { status = ready ? "Ready" : "Degraded", database = new { database.Mode, database.Ready, database.Error } };
    return ready ? Results.Ok(body) : Results.Json(body, statusCode: StatusCodes.Status503ServiceUnavailable);
});

var api = app.MapGroup("/api");
api.MapPlatformEndpoints();

var data = api.MapGroup("").AddEndpointFilter(DatabaseGate.Filter);
data.MapPlanningEndpoints();
data.MapYardContract();
data.MapYardViews();

api.MapGet("/alvys/loads", async (IExternalLoadReader loads, CancellationToken ct) =>
    Results.Ok(await loads.GetVisibleLoadsAsync(ct)));

app.Run();

public partial class Program;
