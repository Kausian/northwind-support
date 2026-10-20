using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace SupportDesk.ApiTests;

/// <summary>DK-7: GET /health reports database reachability, without a token and without secrets.</summary>
public sealed class HealthApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Health_WithoutAToken_Is200AndHealthy_WhenTheDatabaseIsReachable()
    {
        var client = factory.CreateSeededClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Healthy", body.RootElement.GetProperty("status").GetString());

        var database = Assert.Single(body.RootElement.GetProperty("checks").EnumerateArray());
        Assert.Equal("database", database.GetProperty("name").GetString());
        Assert.Equal("Healthy", database.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Health_StillLeavesEveryOtherEndpointProtected()
    {
        var client = factory.CreateSeededClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/tickets")).StatusCode);
    }
}

/// <summary>The same API pointed at a database that cannot be opened.</summary>
public sealed class UnreachableDatabaseApiFactory : ApiFactory
{
    public const string ConnectionString = "Data Source=does-not-exist/northwind-health.db;Mode=ReadOnly";

    protected override void UseDatabase(DbContextOptionsBuilder options) => options.UseSqlite(ConnectionString);
}

public sealed class UnhealthyApiTests(UnreachableDatabaseApiFactory factory) : IClassFixture<UnreachableDatabaseApiFactory>
{
    [Fact]
    public async Task Health_Is503AndUnhealthy_WhenTheDatabaseIsUnreachable()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health");
        var text = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var body = JsonDocument.Parse(text);
        Assert.Equal("Unhealthy", body.RootElement.GetProperty("status").GetString());
        Assert.Equal("Unhealthy", body.RootElement.GetProperty("checks")[0].GetProperty("status").GetString());

        // Nothing about where the database is or why it failed.
        Assert.DoesNotContain("does-not-exist", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Data Source", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("exception", text, StringComparison.OrdinalIgnoreCase);
    }
}
