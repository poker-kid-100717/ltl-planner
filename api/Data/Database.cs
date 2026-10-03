using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Neo4j.Driver;

namespace Portfolio.Ltl.Api.Data;

/// <summary>What the API is storing data in, and whether it is reachable.</summary>
public sealed class DatabaseStatus
{
    public required string Mode { get; init; }
    public bool Ready { get; set; }
    public string? Error { get; set; }
    public bool Persistent => Mode == Database.Neo4jMode;
}

public static class Database
{
    public const string Neo4jMode = "Neo4j";
    public const string DemoMode = "Demo (SQLite, resets on restart)";

    /// <summary>
    /// Neo4j when ConnectionStrings:Default or DATABASE_URL is a neo4j:// or bolt:// URL (an AuraDB
    /// neo4j+s://user:password@host URL works as-is); otherwise a throwaway SQLite file so the demo runs
    /// with no database configured.
    /// </summary>
    public static DatabaseStatus AddLtlDatabase(this IServiceCollection services, IConfiguration config)
    {
        var configured = config.GetConnectionString("Default");
        if (string.IsNullOrWhiteSpace(configured)) configured = config["DATABASE_URL"];

        if (!string.IsNullOrWhiteSpace(configured))
        {
            if (!Neo4jConnection.IsNeo4jUrl(configured))
                throw new InvalidOperationException("DATABASE_URL must be a Neo4j URL, for example neo4j+s://neo4j:<password>@<id>.databases.neo4j.io.");
            services.AddSingleton(new Neo4jConnection(configured));
            services.AddScoped<ILtlStore, Neo4jLtlStore>();
            return Register(services, new DatabaseStatus { Mode = Neo4jMode });
        }

        var path = config["Demo:SqlitePath"] ?? Path.Combine(Path.GetTempPath(), "ltl-planner-demo.db");
        services.AddDbContext<LtlDbContext>(o => o.UseSqlite($"Data Source={path};Default Timeout=10"));
        services.AddScoped<ILtlStore, SqliteLtlStore>();
        return Register(services, new DatabaseStatus { Mode = DemoMode });
    }

    private static DatabaseStatus Register(IServiceCollection services, DatabaseStatus status)
    {
        services.AddSingleton(status);
        return status;
    }

    /// <summary>True for errors that mean the database cannot be reached right now.</summary>
    public static bool IsUnavailable(Exception ex) =>
        ex is DbException or TimeoutException or ServiceUnavailableException or SessionExpiredException or AuthenticationException;

    /// <summary>
    /// Brings the schema up to date and seeds demo data into an empty database. Never throws:
    /// if the database is unreachable the API still starts, /health/ready reports why, and
    /// data endpoints answer 503.
    /// </summary>
    public static async Task InitializeAsync(IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var status = scope.ServiceProvider.GetRequiredService<DatabaseStatus>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Database");

        try
        {
            await scope.ServiceProvider.GetRequiredService<ILtlStore>().InitializeAsync(ct);
            status.Ready = true;
            status.Error = null;
            logger.LogInformation("Database ready ({Mode}).", status.Mode);
        }
        catch (Exception ex) when (IsUnavailable(ex) || ex is InvalidOperationException)
        {
            status.Ready = false;
            status.Error = "The database could not be reached.";
            logger.LogError(ex, "Database initialization failed ({Mode}).", status.Mode);
        }
    }
}
