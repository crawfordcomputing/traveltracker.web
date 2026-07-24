using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TravelTracker.Web.Data;

// Design-time factory used by the EF Core tools (e.g. `dotnet ef migrations add`,
// `dotnet ef database update`). It reads the connection string from the
// ConnectionStrings__Default environment variable when present, otherwise falls
// back to LocalDB so migrations can be scaffolded without extra setup. This is
// never used at runtime — the app builds its context through DI in
// DatabaseSetup.AddAppDatabase.
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? "Server=(localdb)\\MSSQLLocalDB;Database=traveltracker_design;"
             + "Trusted_Connection=True;Encrypt=False";

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new AppDbContext(options);
    }
}
