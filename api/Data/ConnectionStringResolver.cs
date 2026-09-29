using Npgsql;

namespace Portfolio.Ltl.Api.Data;

public static class ConnectionStringResolver
{
    public static string Resolve(IConfiguration configuration)
    {
        var value = configuration.GetConnectionString("Default") ?? configuration["DATABASE_URL"] ?? "";
        if (string.IsNullOrWhiteSpace(value)) return "";

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "postgres" && uri.Scheme != "postgresql"))
            return value;

        var userInfo = uri.UserInfo.Split(':', 2);
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.IsDefaultPort ? 5432 : uri.Port,
            Database = uri.AbsolutePath.Trim('/'),
            Username = Uri.UnescapeDataString(userInfo.ElementAtOrDefault(0) ?? ""),
            Password = Uri.UnescapeDataString(userInfo.ElementAtOrDefault(1) ?? ""),
            SslMode = SslMode.Require,
            Pooling = true
        };
        return builder.ConnectionString;
    }
}
