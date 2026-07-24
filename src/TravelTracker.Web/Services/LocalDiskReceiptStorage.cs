namespace TravelTracker.Web.Services;

// Default receipt storage: files on local disk under Storage:LocalPath.
//
// On Azure App Service, point Storage:LocalPath at /home/data/receipts so receipts
// live on the persisted /home share and survive deploys — the same rule that keeps
// the SQLite DB out of the publish folder. For scale-out or hardened deploys, flip
// Storage:Provider to AzureBlob.
public sealed class LocalDiskReceiptStorage : IReceiptStorage
{
    private readonly string _root;

    public LocalDiskReceiptStorage(IConfiguration config, IHostEnvironment env)
    {
        var configured = config["Storage:LocalPath"] ?? "App_Data/receipts";
        // Resolve relative paths against the content root so behavior is stable
        // regardless of the current working directory.
        _root = Path.IsPathRooted(configured)
            ? configured
            : Path.Combine(env.ContentRootPath, configured);
        Directory.CreateDirectory(_root);
    }

    public async Task<string> SaveAsync(
        Stream content, string originalFileName, string contentType, CancellationToken ct = default)
    {
        var ext = Path.GetExtension(originalFileName);
        if (string.IsNullOrEmpty(ext) || !ReceiptContentTypes.IsAllowedExtension(ext))
            ext = string.Empty; // guardrails run in the page; stay defensive here too

        // Opaque, collision-free key; also the on-disk filename (flat layout).
        var key = $"{Guid.NewGuid():N}{ext.ToLowerInvariant()}";
        var path = Path.Combine(_root, key);

        await using var file = File.Create(path);
        await content.CopyToAsync(file, ct);
        return key;
    }

    public Task<ReceiptContent?> OpenReadAsync(string key, CancellationToken ct = default)
    {
        var path = SafePath(key);
        if (path is null || !File.Exists(path))
            return Task.FromResult<ReceiptContent?>(null);

        Stream stream = File.OpenRead(path);
        var contentType = ReceiptContentTypes.FromFileName(path);
        return Task.FromResult<ReceiptContent?>(new ReceiptContent(stream, contentType));
    }

    public Task DeleteAsync(string key, CancellationToken ct = default)
    {
        var path = SafePath(key);
        if (path is not null && File.Exists(path))
            File.Delete(path);
        return Task.CompletedTask;
    }

    // Collapse the key to a bare filename and re-root it, so a crafted key can never
    // escape the receipts directory (path traversal).
    private string? SafePath(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        var name = Path.GetFileName(key);
        return string.IsNullOrEmpty(name) ? null : Path.Combine(_root, name);
    }
}
