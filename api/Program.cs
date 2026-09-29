using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Http.Resilience;
using Portfolio.Ltl.Api.Data;
using Portfolio.Ltl.Api.Integrations.Alvys;
using Portfolio.Ltl.Api.Integrations.Yard;
using Portfolio.Ltl.Api.Models;
using Portfolio.Ltl.Api.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();

var connectionString = ConnectionStringResolver.Resolve(builder.Configuration);
builder.Services.AddDbContext<AppDbContext>(o => o.UseNpgsql(connectionString));
builder.Services.AddScoped<PlannerService>();
builder.Services.Configure<AlvysOptions>(builder.Configuration.GetSection(AlvysOptions.Section));
builder.Services.AddSingleton<AlvysTokenProvider>();
builder.Services.AddSingleton<IExternalLoadReader, AlvysLoadReader>();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("writes", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Request.Headers["CF-Connecting-IP"].FirstOrDefault() ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

builder.Services.AddHttpClient(AlvysTokenProvider.AuthClient).AddStandardResilienceHandler();
builder.Services.AddHttpClient(AlvysLoadReader.ApiClient).AddStandardResilienceHandler();

var app = builder.Build();
app.UseExceptionHandler();
app.UseRateLimiter();
if (app.Environment.IsDevelopment()) app.MapOpenApi();

var databaseReady = false;
try
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    await DbSeeder.SeedAsync(db);
    databaseReady = true;
}
catch (Exception ex)
{
    app.Logger.LogError(ex, "Database unavailable during startup; liveness remains available.");
}

app.MapGet("/health", () => Results.Ok(new { status = "Healthy", service = "ltl-planner" }));
app.MapGet("/health/ready", async (AppDbContext db) =>
{
    try { return await db.Database.CanConnectAsync() ? Results.Ok(new { status="Ready" }) : Results.StatusCode(503); }
    catch { return Results.StatusCode(503); }
});

var orders = app.MapGroup("/api/orders");
orders.MapGet("", async (string? search, string? status, int? page, int? pageSize, AppDbContext db) =>
{
    var q = db.Orders.AsNoTracking().AsQueryable();
    if (!string.IsNullOrWhiteSpace(search)) q = q.Where(x => x.Id.Contains(search) || x.Customer.Contains(search));
    if (!string.IsNullOrWhiteSpace(status)) q = q.Where(x => x.Status == status);
    var size = Math.Clamp(pageSize ?? 50, 1, 100);
    var skip = (Math.Max(page ?? 1, 1) - 1) * size;
    return Results.Ok(await q.OrderBy(x => x.Id).Skip(skip).Take(size).Select(x => ToOrder(x)).ToListAsync());
});
orders.MapPost("", async (CreateOrderRequest r, AppDbContext db) =>
{
    var errors = ValidateOrder(r.Customer,r.Origin,r.Destination,r.Pallets,r.Weight,r.Equipment,r.Priority);
    if (errors.Count > 0) return Results.ValidationProblem(errors);
    var next = await db.Orders.CountAsync() + 1001;
    var entity = new ShipmentOrderEntity { Id=$"ORD-{next:0000}", Customer=r.Customer.Trim(), Origin=r.Origin.Trim(), Destination=r.Destination.Trim(), Pallets=r.Pallets, Weight=r.Weight, Equipment=r.Equipment.Trim(), Priority=r.Priority, ReadyDate=r.ReadyDate ?? DateTimeOffset.UtcNow.Date, Status="Open" };
    db.Orders.Add(entity); await db.SaveChangesAsync(); return Results.Created($"/api/orders/{entity.Id}", ToOrder(entity));
}).RequireRateLimiting("writes");
orders.MapPut("/{id}", async (string id, UpdateOrderRequest r, AppDbContext db) =>
{
    var entity=await db.Orders.FindAsync(id); if(entity is null) return Results.NotFound();
    if(entity.Status!="Open") return Results.Conflict(new { title="Only open orders can be edited." });
    var errors=ValidateOrder(r.Customer,r.Origin,r.Destination,r.Pallets,r.Weight,r.Equipment,r.Priority);
    if(errors.Count>0) return Results.ValidationProblem(errors);
    entity.Customer=r.Customer.Trim(); entity.Origin=r.Origin.Trim(); entity.Destination=r.Destination.Trim(); entity.Pallets=r.Pallets; entity.Weight=r.Weight; entity.Equipment=r.Equipment.Trim(); entity.Priority=r.Priority; entity.ReadyDate=r.ReadyDate ?? entity.ReadyDate;
    try { await db.SaveChangesAsync(); return Results.Ok(ToOrder(entity)); } catch(DbUpdateConcurrencyException){ return Results.Conflict(new { title="Order changed. Refresh and retry." }); }
}).RequireRateLimiting("writes");
orders.MapPost("/{id}/cancel", async (string id, AppDbContext db) =>
{
    var entity=await db.Orders.FindAsync(id); if(entity is null) return Results.NotFound();
    if(entity.Status!="Open") return Results.Conflict(new { title="Only open orders can be cancelled." });
    entity.Status="Cancelled"; await db.SaveChangesAsync(); return Results.Ok(ToOrder(entity));
}).RequireRateLimiting("writes");
orders.MapPost("/{id}/dispatch", async (string id, AppDbContext db) =>
{
    var entity=await db.Orders.FindAsync(id); if(entity is null) return Results.NotFound();
    if(entity.Status!="Planned") return Results.Conflict(new { title="Only planned orders can be dispatched." });
    entity.Status="Dispatched"; await db.SaveChangesAsync(); return Results.Ok(ToOrder(entity));
}).RequireRateLimiting("writes");

var trucks = app.MapGroup("/api/trucks");
trucks.MapGet("", async (AppDbContext db) => Results.Ok(await db.Trucks.AsNoTracking().OrderBy(x=>x.Id).Select(x=>ToTruck(x)).ToListAsync()));
trucks.MapPost("", async (CreateTruckRequest r, AppDbContext db) =>
{
    if(r.PalletCapacity<1 || r.PalletCapacity>60 || r.WeightCapacity<1000 || string.IsNullOrWhiteSpace(r.Equipment) || string.IsNullOrWhiteSpace(r.CurrentLocation))
        return Results.ValidationProblem(new Dictionary<string,string[]> {["truck"]=["Equipment, location and realistic capacities are required."]});
    var next=await db.Trucks.CountAsync()+201;
    var entity=new TruckEntity{Id=$"TRK-{next:000}",Equipment=r.Equipment.Trim(),PalletCapacity=r.PalletCapacity,WeightCapacity=r.WeightCapacity,CurrentLocation=r.CurrentLocation.Trim(),Active=true};
    db.Trucks.Add(entity); await db.SaveChangesAsync(); return Results.Created($"/api/trucks/{entity.Id}",ToTruck(entity));
}).RequireRateLimiting("writes");
trucks.MapPut("/{id}", async (string id, UpdateTruckRequest r, AppDbContext db) =>
{
    var e=await db.Trucks.FindAsync(id); if(e is null) return Results.NotFound();
    e.Equipment=r.Equipment.Trim(); e.PalletCapacity=r.PalletCapacity; e.WeightCapacity=r.WeightCapacity; e.CurrentLocation=r.CurrentLocation.Trim(); e.Active=r.Active;
    try { await db.SaveChangesAsync(); return Results.Ok(ToTruck(e)); } catch(DbUpdateConcurrencyException){ return Results.Conflict(new {title="Truck changed. Refresh and retry."}); }
}).RequireRateLimiting("writes");
trucks.MapPost("/{id}/deactivate", async (string id, AppDbContext db) => { var e=await db.Trucks.FindAsync(id); if(e is null)return Results.NotFound(); e.Active=false; await db.SaveChangesAsync(); return Results.Ok(ToTruck(e)); }).RequireRateLimiting("writes");

var plans=app.MapGroup("/api/plans");
plans.MapGet("", async (AppDbContext db) => Results.Ok((await db.Plans.AsNoTracking().OrderByDescending(x=>x.CreatedAt).Take(100).ToListAsync()).Select(DeserializePlan)));
plans.MapPost("/build", async (PlanBuildRequest request, PlannerService planner, AppDbContext db) =>
{
    var oq=db.Orders.AsNoTracking().Where(x=>x.Status=="Open");
    if(request.OrderIds is {Count:>0}) oq=oq.Where(x=>request.OrderIds.Contains(x.Id));
    var tq=db.Trucks.AsNoTracking().Where(x=>x.Active);
    if(request.TruckIds is {Count:>0}) tq=tq.Where(x=>request.TruckIds.Contains(x.Id));
    var result=planner.Build((await oq.ToListAsync()).Select(ToOrder).ToArray(),(await tq.ToListAsync()).Select(ToTruck).ToArray());
    db.Plans.Add(new PlanEntity{Id=result.Id,CreatedAt=result.CreatedAt,Status="Draft",Algorithm=result.Algorithm,PayloadJson=JsonSerializer.Serialize(result)});
    await db.SaveChangesAsync(); return Results.Ok(result);
}).RequireRateLimiting("writes");
plans.MapPost("/{id:guid}/commit", async (Guid id, AppDbContext db) =>
{
    await using var tx=await db.Database.BeginTransactionAsync();
    var plan=await db.Plans.FindAsync(id); if(plan is null)return Results.NotFound();
    if(plan.Status!="Draft") return Results.Conflict(new {title="Only draft plans can be committed."});
    var snapshot=DeserializePlan(plan); var ids=snapshot.Trucks.SelectMany(x=>x.Orders).Select(x=>x.Id).Distinct().ToArray();
    var persisted=await db.Orders.Where(x=>ids.Contains(x.Id)).ToListAsync();
    if(persisted.Count!=ids.Length || persisted.Any(x=>x.Status!="Open")) return Results.Conflict(new {title="An order changed after this draft was built. Rebuild the plan."});
    foreach(var order in persisted){order.Status="Planned";order.PlanId=id;}
    plan.Status="Committed";
    await db.SaveChangesAsync(); await tx.CommitAsync();
    return Results.Ok(snapshot with {Status="Committed"});
}).RequireRateLimiting("writes");
plans.MapPost("/{id:guid}/discard", async (Guid id, AppDbContext db) => { var p=await db.Plans.FindAsync(id); if(p is null)return Results.NotFound(); if(p.Status!="Draft")return Results.Conflict(new {title="Only draft plans can be discarded."}); p.Status="Discarded"; await db.SaveChangesAsync(); return Results.Ok(DeserializePlan(p) with {Status="Discarded"}); }).RequireRateLimiting("writes");

app.MapGet("/api/integrations/v1/yard/candidates", async (string trailerNumber,string? equipment,int? maxPallets,AppDbContext db) =>
{
    var capacity=Math.Clamp(maxPallets??26,1,60);
    var q=db.Orders.AsNoTracking().Where(o=>o.Status=="Open" && o.Pallets<=capacity);
    if(!string.IsNullOrWhiteSpace(equipment)) q=q.Where(o=>o.Equipment.ToLower()==equipment.ToLower());
    var items=await q.OrderByDescending(o=>o.Priority).ThenByDescending(o=>o.Pallets).Take(8)
        .Select(o=>new YardCandidate(o.Id,o.Customer,o.Origin,o.Destination,o.Pallets,o.Weight,o.Equipment,$"Fits {trailerNumber} by declared equipment and pallet capacity; final truck/route validation stays in LTL.")).ToListAsync();
    return Results.Ok(items);
});
app.MapPost("/api/integrations/v1/yard/events", async (HttpRequest request,IConfiguration config,AppDbContext db) =>
{
    using var reader=new StreamReader(request.Body); var body=await reader.ReadToEndAsync();
    if(!Signature.Verify(body,config["Integration:YardSigningKey"]??"",request.Headers["X-Portfolio-Signature"].ToString())) return Results.Unauthorized();
    var evt=JsonSerializer.Deserialize<YardIntegrationEvent>(body,new JsonSerializerOptions(JsonSerializerDefaults.Web));
    if(evt is null || evt.EventId==Guid.Empty || string.IsNullOrWhiteSpace(evt.TrailerNumber)) return Results.ValidationProblem(new Dictionary<string,string[]>{{"event",["A valid eventId and trailerNumber are required."]}});
    if(evt.SchemaVersion!=1) return Results.ValidationProblem(new Dictionary<string,string[]>{{"schemaVersion",["Only Yard integration schema version 1 is supported."]}});
    if(await db.YardEvents.AnyAsync(x=>x.EventId==evt.EventId)) return Results.Ok(new{accepted=false,duplicate=true,evt.EventId});
    await using var tx=await db.Database.BeginTransactionAsync();
    db.YardEvents.Add(new YardEventEntity{EventId=evt.EventId,EventType=evt.EventType,TrailerNumber=evt.TrailerNumber,OccurredAt=evt.OccurredAt,Details=evt.Details,ReceivedAt=DateTimeOffset.UtcNow,Processed=true});
    var trailer=await db.YardTrailers.FindAsync(evt.TrailerNumber);
    if(trailer is null) db.YardTrailers.Add(new YardTrailerEntity{TrailerNumber=evt.TrailerNumber,Status=evt.EventType,LastEventAt=evt.OccurredAt});
    else if(evt.OccurredAt>=trailer.LastEventAt){trailer.Status=evt.EventType;trailer.LastEventAt=evt.OccurredAt;}
    await db.SaveChangesAsync(); await tx.CommitAsync();
    return Results.Ok(new{accepted=true,duplicate=false,evt.EventId});
}).RequireRateLimiting("writes");
app.MapGet("/api/integrations/v1/yard/events", async (AppDbContext db)=>Results.Ok(await db.YardEvents.AsNoTracking().OrderByDescending(x=>x.OccurredAt).Take(100).ToListAsync()));
app.MapGet("/api/integrations/v1/yard/trailers", async (AppDbContext db)=>Results.Ok(await db.YardTrailers.AsNoTracking().OrderBy(x=>x.TrailerNumber).Select(x=>new YardTrailer(x.TrailerNumber,x.Status,x.LastEventAt)).ToListAsync()));

app.MapPost("/api/admin/reset-demo", async (HttpRequest request,IConfiguration config,AppDbContext db) =>
{
    var configured=config["DemoResetToken"]; if(string.IsNullOrWhiteSpace(configured)) return Results.NotFound();
    var supplied=request.Headers["X-Demo-Reset-Token"].ToString();
    if(!System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(System.Text.Encoding.UTF8.GetBytes(configured),System.Text.Encoding.UTF8.GetBytes(supplied.PadRight(configured.Length).Substring(0,configured.Length))) || supplied.Length!=configured.Length) return Results.Unauthorized();
    await using var tx=await db.Database.BeginTransactionAsync();
    await db.Database.ExecuteSqlRawAsync("TRUNCATE TABLE yard_trailers, yard_events, plans, shipment_orders, trucks");
    await DbSeeder.SeedAsync(db); await tx.CommitAsync(); return Results.Ok(new{reset=true});
}).RequireRateLimiting("writes");

app.MapGet("/api/alvys/loads", async (IExternalLoadReader loads,CancellationToken ct)=>Results.Ok(await loads.GetVisibleLoadsAsync(ct)));
app.Run();

static ShipmentOrder ToOrder(ShipmentOrderEntity x)=>new(x.Id,x.Customer,x.Origin,x.Destination,x.Pallets,x.Weight,x.Equipment,x.Priority,x.Status,x.ReadyDate,x.PlanId);
static TruckProfile ToTruck(TruckEntity x)=>new(x.Id,x.Equipment,x.PalletCapacity,x.WeightCapacity,x.CurrentLocation,x.Active);
static PlanResult DeserializePlan(PlanEntity x)=>(JsonSerializer.Deserialize<PlanResult>(x.PayloadJson) ?? throw new InvalidOperationException("Invalid persisted plan.")) with {Status=x.Status};
static Dictionary<string,string[]> ValidateOrder(string customer,string origin,string destination,int pallets,int weight,string equipment,int priority)
{
    var e=new Dictionary<string,string[]>();
    if(string.IsNullOrWhiteSpace(customer)||customer.Length>120)e["customer"]=["Customer is required and must be 120 characters or fewer."];
    if(string.IsNullOrWhiteSpace(origin)||string.IsNullOrWhiteSpace(destination))e["route"]=["Origin and destination are required."];
    if(pallets<1||pallets>60)e["pallets"]=["Pallets must be between 1 and 60."];
    if(weight<1||weight>100000)e["weight"]=["Weight must be between 1 and 100,000 lb."];
    if(string.IsNullOrWhiteSpace(equipment)||equipment.Length>40)e["equipment"]=["Equipment is required."];
    if(priority<1||priority>100)e["priority"]=["Priority must be between 1 and 100."];
    return e;
}

public partial class Program { }
