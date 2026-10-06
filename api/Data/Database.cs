using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Portfolio.Ltl.Api.Data;

/// <summary>What the API is storing data in, and whether it is reachable.</summary>
public sealed class DatabaseStatus
{
    public required string Mode { get; init; }
    public bool Ready { get; set; }
    public string? Error { get; set; }
    /// <summary>The configured connection string could not be parsed; the API runs but never connects.</summary>
    public bool Misconfigured { get; init; }
    public bool Persistent => Mode == Database.PostgresMode;
}

public static class Database
{
    public const string PostgresMode = "PostgreSQL";
    public const string DemoMode = "Demo (SQLite, resets on restart)";

    /// <summary>
    /// PostgreSQL when ConnectionStrings:Default or DATABASE_URL is set (a Neon URL works as-is);
    /// otherwise a throwaway SQLite file so the demo runs with no database configured.
    /// </summary>
    public static DatabaseStatus AddLtlDatabase(this IServiceCollection services, IConfiguration config)
    {
        var configured = config.GetConnectionString("Default");
        if (string.IsNullOrWhiteSpace(configured)) configured = config["DATABASE_URL"];

        if (!string.IsNullOrWhiteSpace(configured))
        {
            // A malformed value (for example a pasted `psql '...'` snippet) must not crash the process:
            // the API still starts, /health stays up, and /health/ready says what is wrong.
            string connectionString;
            try
            {
                connectionString = new NpgsqlConnectionStringBuilder(ToNpgsql(configured)).ConnectionString;
            }
            catch (Exception ex) when (ex is UriFormatException or ArgumentException or FormatException or KeyNotFoundException)
            {
                services.AddDbContext<LtlDbContext>(o => o.UseNpgsql("Host=invalid.invalid"));
                return Register(services, new DatabaseStatus
                {
                    Mode = PostgresMode,
                    Misconfigured = true,
                    Error = "DATABASE_URL is not a valid PostgreSQL connection string (expected postgresql://user:password@host/db?sslmode=require)."
                });
            }
            // No retrying execution strategy: several writes use explicit transactions.
            services.AddDbContext<LtlDbContext>(o => o.UseNpgsql(connectionString));
            return Register(services, new DatabaseStatus { Mode = PostgresMode });
        }

        var path = config["Demo:SqlitePath"] ?? Path.Combine(Path.GetTempPath(), "ltl-planner-demo.db");
        services.AddDbContext<LtlDbContext>(o => o.UseSqlite($"Data Source={path};Default Timeout=10"));
        return Register(services, new DatabaseStatus { Mode = DemoMode });
    }

    private static DatabaseStatus Register(IServiceCollection services, DatabaseStatus status)
    {
        services.AddSingleton(status);
        return status;
    }

    /// <summary>Accepts either an Npgsql connection string or a postgres:// URL.</summary>
    public static string ToNpgsql(string value)
    {
        if (!value.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) &&
            !value.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            return value;
        }

        var uri = new Uri(value);
        var user = uri.UserInfo.Split(':', 2);
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.IsDefaultPort || uri.Port <= 0 ? 5432 : uri.Port,
            Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')),
            Username = Uri.UnescapeDataString(user[0]),
            Password = user.Length > 1 ? Uri.UnescapeDataString(user[1]) : null
        };

        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            var key = Uri.UnescapeDataString(parts[0]).ToLowerInvariant();
            var setting = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : "";
            switch (key)
            {
                case "sslmode":
                    builder.SslMode = Enum.Parse<SslMode>(setting.Replace("-", ""), ignoreCase: true);
                    break;
                case "channel_binding":
                    builder.ChannelBinding = Enum.Parse<ChannelBinding>(setting, ignoreCase: true);
                    break;
            }
        }

        return builder.ConnectionString;
    }

    /// <summary>
    /// Applies pending migrations and exits: the deploy pipeline runs this with the owner (unpooled)
    /// connection before the new container starts, so the running app never needs DDL rights.
    /// Returns false when there is no PostgreSQL connection or the migration fails.
    /// </summary>
    public static async Task<bool> MigrateAsync(IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var status = scope.ServiceProvider.GetRequiredService<DatabaseStatus>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Database");
        if (!status.Persistent)
        {
            logger.LogError("migrate needs a PostgreSQL connection (ConnectionStrings:Default or DATABASE_URL).");
            return false;
        }
        if (status.Misconfigured)
        {
            logger.LogError("{Error}", status.Error);
            return false;
        }

        var db = scope.ServiceProvider.GetRequiredService<LtlDbContext>();
        try
        {
            var pending = (await db.Database.GetPendingMigrationsAsync(ct)).ToList();
            await db.Database.MigrateAsync(ct);
            logger.LogInformation("Applied {Count} migration(s): {Migrations}", pending.Count,
                pending.Count == 0 ? "none pending" : string.Join(", ", pending));
            return true;
        }
        catch (Exception ex) when (ex is DbException or InvalidOperationException or TimeoutException)
        {
            logger.LogError(ex, "Migration failed.");
            return false;
        }
    }

    /// <summary>
    /// Brings the schema up to date (or, when Database:MigrateOnStartup is false, checks that the deploy
    /// pipeline already did) and seeds demo data into an empty database. Never throws:
    /// if the database is unreachable the API still starts, /health/ready reports why, and
    /// data endpoints answer 503.
    /// </summary>
    public static async Task InitializeAsync(IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var status = scope.ServiceProvider.GetRequiredService<DatabaseStatus>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Database");
        var db = scope.ServiceProvider.GetRequiredService<LtlDbContext>();
        var seeder = scope.ServiceProvider.GetRequiredService<DemoSeeder>();
        var migrateOnStartup = scope.ServiceProvider.GetRequiredService<IConfiguration>().GetValue("Database:MigrateOnStartup", true);
        if (status.Misconfigured)
        {
            status.Ready = false;
            logger.LogError("{Error}", status.Error);
            return;
        }

        try
        {
            if (status.Persistent && migrateOnStartup)
            {
                await db.Database.MigrateAsync(ct);
            }
            else if (status.Persistent)
            {
                // Production runs as a least-privilege role: migrations belong to the deploy pipeline.
                var pending = (await db.Database.GetPendingMigrationsAsync(ct)).ToList();
                if (pending.Count > 0)
                {
                    status.Ready = false;
                    status.Error = "The database schema is behind this build; run the migration step.";
                    logger.LogError("Pending migrations with MigrateOnStartup disabled: {Migrations}", string.Join(", ", pending));
                    return;
                }
            }
            else
            {
                await db.Database.EnsureDeletedAsync(ct);
                await db.Database.EnsureCreatedAsync(ct);
            }

            if (!await db.Trucks.AnyAsync(ct)) await seeder.SeedAsync(db, ct);
            status.Ready = true;
            status.Error = null;
            logger.LogInformation("Database ready ({Mode}).", status.Mode);
        }
        catch (Exception ex) when (ex is DbException or InvalidOperationException or TimeoutException)
        {
            status.Ready = false;
            status.Error = "The database could not be reached.";
            logger.LogError(ex, "Database initialization failed ({Mode}).", status.Mode);
        }
    }
}
