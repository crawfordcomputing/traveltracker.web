namespace TravelTracker.Web.Services;

// Receipt storage is Azure Blob, always. Local disk was removed: on Azure App
// Service, wwwroot/App_Data is wiped on every deploy, so receipts saved there
// silently disappeared. Blob is durable across deploys and scales out, and is now
// the single supported provider.
//
// Dev/eval run against Azurite via Storage:Blob:ConnectionString=UseDevelopmentStorage=true.
public static class StorageSetup
{
    public static IServiceCollection AddReceiptStorage(
        this IServiceCollection services, IConfiguration config)
    {
        services.AddSingleton<IReceiptStorage, AzureBlobReceiptStorage>();
        return services;
    }
}
