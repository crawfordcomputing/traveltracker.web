using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TravelTracker.Web.Data;

namespace TravelTracker.Tests;

// Central helper so the whole test suite runs against SQL Server. By default it
// targets Windows LocalDB (MSSQLLocalDB); override the server for CI via the
// TT_TEST_SQL environment variable, e.g. a SQL Server service container:
//   TT_TEST_SQL="Server=localhost,1433;User ID=sa;Password=Your_password123;Encrypt=True;TrustServerCertificate=True"
//
// Each test gets an isolated, uniquely-named database that is dropped afterward.
public static class TestDatabase
{
    private const string DefaultServer =
        "Server=(localdb)\\MSSQLLocalDB;Trusted_Connection=True;Encrypt=False";

    private static string BaseConnection =>
        Environment.GetEnvironmentVariable("TT_TEST_SQL") ?? DefaultServer;

    // A brand-new connection string with a fresh, unique database name.
    public static string NewConnectionString() =>
        ConnectionStringFor($"tt_test_{Guid.NewGuid():N}");

    // Deterministic connection string for a given token (e.g. a per-test unique
    // string). Setup and teardown pass the same token to target the same
    // database. The token is sanitized into a valid SQL Server database name.
    public static string ConnectionStringFor(string token)
    {
        var name = Regex.Replace(Path.GetFileNameWithoutExtension(token), "[^A-Za-z0-9_]", "_");
        if (name.Length == 0 || char.IsDigit(name[0])) name = "tt_" + name;
        return $"{BaseConnection.TrimEnd(';')};Database={name}";
    }

    // Drops the database named by the connection string. Best-effort: clears
    // pooled connections first so an open handle can't block the DROP.
    public static void Drop(string connectionString)
    {
        SqlConnection.ClearAllPools();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString).Options;
        using var db = new AppDbContext(options);
        db.Database.EnsureDeleted();
    }
}
