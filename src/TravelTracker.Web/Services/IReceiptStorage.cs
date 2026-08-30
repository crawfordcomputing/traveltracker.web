namespace TravelTracker.Web.Services;

// A saved receipt: the opaque storage key plus the content type to serve it with.
public readonly record struct ReceiptContent(Stream Stream, string ContentType);

// Abstraction over where receipt files live. Backed by Azure Blob (the only provider;
// local disk was removed because App Service wipes wwwroot/App_Data on deploy). Dev/eval
// use Azurite via Storage:Blob:ConnectionString=UseDevelopmentStorage=true.
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
