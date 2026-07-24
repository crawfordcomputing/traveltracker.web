namespace TravelTracker.Web.Services;

// Single source of truth for which receipt file types are accepted and how each
// maps to a content type. Used by the upload guardrails (page) and by local-disk
// storage (which derives the content type from the stored extension on read).
public static class ReceiptContentTypes
{
    // Extension (lower, with dot) -> content type. HEIC/WEBP included for phone photos.
    private static readonly IReadOnlyDictionary<string, string> ByExtension =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".pdf"] = "application/pdf",
            [".jpg"] = "image/jpeg",
            [".jpeg"] = "image/jpeg",
            [".png"] = "image/png",
            [".heic"] = "image/heic",
            [".webp"] = "image/webp",
        };

    public static bool IsAllowedExtension(string extension) =>
        ByExtension.ContainsKey(extension);

    public static bool IsAllowedContentType(string contentType) =>
        ByExtension.Values.Contains(contentType, StringComparer.OrdinalIgnoreCase);

    // Best-effort content type from a filename/extension; octet-stream fallback.
    public static string FromFileName(string fileName)
    {
        var ext = Path.GetExtension(fileName);
        return ByExtension.TryGetValue(ext, out var ct) ? ct : "application/octet-stream";
    }
}
