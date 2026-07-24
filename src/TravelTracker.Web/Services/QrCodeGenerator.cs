using QRCoder;

namespace TravelTracker.Web.Services;

// Renders a QR code as a self-contained PNG data URI. Uses QRCoder's pure-managed
// PngByteQRCode (no System.Drawing), so it works unchanged on Linux App Service, and
// the data URI keeps the image inline — no extra request, works offline. Used to encode
// the otpauth:// URI on the authenticator enrollment page.
public static class QrCodeGenerator
{
    public static string ToPngDataUri(string payload)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.Q);
        var png = new PngByteQRCode(data);
        var bytes = png.GetGraphic(10);
        return "data:image/png;base64," + Convert.ToBase64String(bytes);
    }
}
