using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace TravelTracker.Tests;

public class AuthorizationTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _connectionString;

    public AuthorizationTests(WebApplicationFactory<Program> factory)
    {
        // Isolate the run in its own SQL Server database (LocalDB locally, or the
        // TT_TEST_SQL server in CI).
        _connectionString = TestDatabase.NewConnectionString();
        _factory = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("ConnectionStrings:Default", _connectionString);
            // Seed password must be supplied out-of-band now (no committed default).
            b.UseSetting("Seed:AdminPassword", "Test-Admin-123!");
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
    public async Task Admin_Area_Redirects_Anonymous_To_Login()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var resp = await client.GetAsync("/Admin");
        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        Assert.Contains("/Account/Login", resp.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Login_Page_Is_Publicly_Accessible()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/Account/Login");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Contains("Log in", await resp.Content.ReadAsStringAsync());
    }
}
