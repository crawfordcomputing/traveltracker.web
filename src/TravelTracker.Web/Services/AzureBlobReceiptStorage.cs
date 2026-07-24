using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace TravelTracker.Web.Services;

// Production receipt storage: one blob per receipt in a private container.
// Selected via Storage:Provider=AzureBlob. Config:
//   Storage:Blob:ConnectionString  (or a managed-identity account URL — see below)
//   Storage:Blob:Container         (default "receipts")
//
// The container is created if missing and is PRIVATE (no public access); receipts
// are streamed only through the authorized Receipt handler, never a public URL.
public sealed class AzureBlobReceiptStorage : IReceiptStorage
{
    private readonly BlobContainerClient _container;

    public AzureBlobReceiptStorage(IConfiguration config)
    {
        var connectionString = config["Storage:Blob:ConnectionString"];
        var containerName = config["Storage:Blob:Container"] ?? "receipts";

        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException(
                "Storage:Blob:ConnectionString is required when Storage:Provider=AzureBlob.");

        _container = new BlobContainerClient(connectionString, containerName);
        _container.CreateIfNotExists(PublicAccessType.None);
    }

    public async Task<string> SaveAsync(
        Stream content, string originalFileName, string contentType, CancellationToken ct = default)
    {
        var ext = Path.GetExtension(originalFileName);
        if (string.IsNullOrEmpty(ext) || !ReceiptContentTypes.IsAllowedExtension(ext))
            ext = string.Empty;

        var key = $"{Guid.NewGuid():N}{ext.ToLowerInvariant()}";
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
        await _container.DeleteBlobIfExistsAsync(key, cancellationToken: ct);
    }
}
