using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace TravelTracker.Web.Services;

// Receipt storage: one blob per receipt in a private container. The only provider
// (local disk was removed as too fragile on App Service). Config:
//   Storage:Blob:ConnectionString  (UseDevelopmentStorage=true for Azurite in dev)
//   Storage:Blob:Container         (default "receipts")
//
// Keys are date-partitioned by UPLOAD time: {yyyy}/{MM}/{guid}{ext}. Partitioning on
// upload time (not the editable Expense.Date) keeps a key immutable once written, so
// editing an expense never orphans a file. Blob has no real folders; the '/' just
// makes prefix listing (by month) and lifecycle/retention rules cheap.
//
// The container is created if missing and is PRIVATE (no public access); receipts
// are streamed only through the authorized Receipt handler, never a public URL.
// Creation happens lazily and asynchronously on first use rather than as a blocking
// network call inside the singleton constructor (which would stall whichever request
// thread first resolves the service).
public sealed class AzureBlobReceiptStorage : IReceiptStorage, IDisposable
{
    private readonly BlobContainerClient _container;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private volatile bool _initialized;

    public AzureBlobReceiptStorage(IConfiguration config)
    {
        var connectionString = config["Storage:Blob:ConnectionString"];
        var containerName = config["Storage:Blob:Container"] ?? "receipts";

        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException(
                "Storage:Blob:ConnectionString is required. Use UseDevelopmentStorage=true for Azurite in dev.");

        _container = new BlobContainerClient(connectionString, containerName);
    }

    // Singleton: the DI container disposes it at shutdown.
    public void Dispose() => _initLock.Dispose();

    private async ValueTask EnsureContainerAsync(CancellationToken ct)
    {
        if (_initialized) return;
        await _initLock.WaitAsync(ct);
        try
        {
            if (_initialized) return;
            await _container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: ct);
            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async Task<string> SaveAsync(
        Stream content, string originalFileName, string contentType, CancellationToken ct = default)
    {
        var ext = Path.GetExtension(originalFileName);
        if (string.IsNullOrEmpty(ext) || !ReceiptContentTypes.IsAllowedExtension(ext))
            ext = string.Empty;

        // Date-partitioned, opaque, collision-free key. UTC upload time so the prefix
        // is stable and independent of the user's editable expense date.
        var now = DateTimeOffset.UtcNow;
        var key = $"{now:yyyy}/{now:MM}/{Guid.NewGuid():N}{ext.ToLowerInvariant()}";

        await EnsureContainerAsync(ct);
        var blob = _container.GetBlobClient(key);
        await blob.UploadAsync(
            content,
            new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = contentType } },
            ct);
        return key;
    }

    public async Task<ReceiptContent?> OpenReadAsync(string key, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        await EnsureContainerAsync(ct);
        var blob = _container.GetBlobClient(key);
        try
        {
            var response = await blob.DownloadStreamingAsync(cancellationToken: ct);
            var contentType = response.Value.Details.ContentType;
            if (string.IsNullOrEmpty(contentType))
                contentType = ReceiptContentTypes.FromFileName(key);
            return new ReceiptContent(response.Value.Content, contentType);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task DeleteAsync(string key, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        await EnsureContainerAsync(ct);
        await _container.DeleteBlobIfExistsAsync(key, cancellationToken: ct);
    }
}
