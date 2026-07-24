using Microsoft.EntityFrameworkCore;

namespace TravelTracker.Web.Data;

// The app targets SQL Server everywhere: SQL Server (or LocalDB) for local
// development and testing, Azure SQL in production. Both use the same EF Core
// SqlServer provider — only the connection string differs (supplied via
// user-secrets locally, and app settings / Key Vault in Azure).
public static class DatabaseSetup
{
    public static IServiceCollection AddAppDatabase(
        this IServiceCollection services, IConfiguration config)
    {
        var connectionString = config.GetConnectionString("Default");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException(
                "ConnectionStrings:Default is required. Set it via user-secrets " +
                "for local development, or app settings / Key Vault in Azure.");

        services.AddDbContext<AppDbContext>(options => options.UseSqlServer(connectionString));

        return services;
    }
}
