namespace TravelTracker.Web.Services;

// Provider switch for receipt storage: local disk by default (frictionless dev/eval),
// Azure Blob when Storage:Provider=AzureBlob. Same "sensible default, one config
// switch to production" pattern as EmailSetup and the DB provider.
public static class StorageSetup
{
    public static IServiceCollection AddReceiptStorage(
        this IServiceCollection services, IConfiguration config)
    {
        var provider = config["Storage:Provider"] ?? "LocalDisk";

        switch (provider.ToLowerInvariant())
        {
            case "azureblob":
                services.AddSingleton<IReceiptStorage, AzureBlobReceiptStorage>();
                break;

            case "localdisk":
            default:
                services.AddSingleton<IReceiptStorage, LocalDiskReceiptStorage>();
                break;
        }

        return services;
    }
}
