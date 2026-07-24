using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace TravelTracker.Tests;

public class HealthEndpointTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _connectionString;

    public HealthEndpointTests(WebApplicationFactory<Program> factory)
    {
        // Isolate the run in its own SQL Server database (LocalDB locally, or the
        // TT_TEST_SQL server in CI).
        _connectionString = TestDatabase.NewConnectionString();
        _factory = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("ConnectionStrings:Default", _connectionString);
            // Skip the Development sample-data seed (Program boots as Development).
            b.UseSetting("Seed:DevData", "false");
        });
    }

    public void Dispose()
    {
        TestDatabase.Drop(_connectionString);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Healthz_Returns_Healthy()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/healthz");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("Healthy", await resp.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Home_Page_Loads()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("Travel Tracker", body);
    }
}
