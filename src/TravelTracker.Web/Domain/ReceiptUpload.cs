using TravelTracker.Web.Services;

namespace TravelTracker.Web.Domain;

// Pure validation for an uploaded receipt: allowed type + size cap. Kept out of the
// page so it's unit-testable and shared by Create and Edit. Returns an error message
// or null when the upload is acceptable.
public static class ReceiptUpload
{
    public static string? Validate(string fileName, string contentType, long lengthBytes, int maxMb)
    {
        if (lengthBytes <= 0)
            return "The receipt file is empty.";

        var maxBytes = (long)maxMb * 1024 * 1024;
        if (lengthBytes > maxBytes)
            return $"Receipt is larger than the {maxMb} MB limit.";

        var ext = Path.GetExtension(fileName);
        if (string.IsNullOrEmpty(ext) || !ReceiptContentTypes.IsAllowedExtension(ext))
            return "Receipt must be a PDF or image (pdf, jpg, png, heic, webp).";

        // Defense in depth: the browser-supplied content type should also be one we
        // serve. Not authoritative (extension is the gate), but rejects obvious mismatches.
        if (!string.IsNullOrEmpty(contentType) && !ReceiptContentTypes.IsAllowedContentType(contentType))
            return "Receipt must be a PDF or image (pdf, jpg, png, heic, webp).";

        return null;
    }
}
