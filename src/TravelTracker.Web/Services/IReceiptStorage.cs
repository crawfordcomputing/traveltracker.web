namespace TravelTracker.Web.Services;

// A saved receipt: the opaque storage key plus the content type to serve it with.
public readonly record struct ReceiptContent(Stream Stream, string ContentType);

// Abstraction over where receipt files live, so the app can run on local disk in
// dev/eval and Azure Blob in production by flipping Storage:Provider — the same
// "sensible default, one config switch to production" pattern as the DB provider.
//
// Keys are opaque, storage-generated strings (never a user-supplied path). Callers
// persist the key on the Expense and read it back only through this abstraction and
// the authorized Receipt handler — receipts are PII and are never a public URL.
public interface IReceiptStorage
{
    // Persists the stream and returns the opaque key to store on the expense.
    // originalFileName is used only to preserve a sensible extension.
    Task<string> SaveAsync(
        Stream content, string originalFileName, string contentType, CancellationToken ct = default);

    // Opens the stored file, or null if the key is unknown/missing.
    Task<ReceiptContent?> OpenReadAsync(string key, CancellationToken ct = default);

    // Removes the stored file. No-op if already gone.
    Task DeleteAsync(string key, CancellationToken ct = default);
}
