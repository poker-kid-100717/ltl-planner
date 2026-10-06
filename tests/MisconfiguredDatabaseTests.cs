using System.Net;
using Microsoft.AspNetCore.Hosting;

namespace Portfolio.Ltl.Api.Tests;

/// <summary>A pasted `psql '...'` snippet instead of a URL.</summary>
public sealed class MisconfiguredDatabaseFactory : ApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("ConnectionStrings:Default", "psql 'postgresql://user:secret@db.example.com/app?sslmode=require'");
    }
}

public sealed class MisconfiguredDatabaseTests(MisconfiguredDatabaseFactory factory) : IClassFixture<MisconfiguredDatabaseFactory>
{
    [Fact]
    public async Task ApiStillStartsAndReadinessExplainsTheBadConnectionString()
    {
        var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);

        var ready = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
        var body = await ready.Content.ReadAsStringAsync();
        Assert.Contains("not a valid PostgreSQL connection string", body);
        Assert.DoesNotContain("secret", body);
    }
}
