using System.Text.RegularExpressions;
using Ganss.Xss;

namespace TravelTracker.Web.Services.Email;

// Save-time cleanup of admin-authored HTML (ADR-0005): strips <script>, <iframe>,
// on* handlers and javascript: URLs. Mail clients mostly block those anyway; the
// real reason is that the admin preview renders the template in the browser.
//
// Tokens are masked before sanitizing so a {{Link}} inside href="..." is not
// mangled or dropped as a malformed URL, then restored afterwards. A token that
// sat inside a removed attribute (onclick="{{X}}") disappears with it, which is
// exactly the intended outcome.
public static partial class EmailTemplateSanitizer
{
    [GeneratedRegex(@"\{\{\s*[A-Za-z][A-Za-z0-9.]*\s*\}\}")]
    private static partial Regex TokenPattern();

    [GeneratedRegex(@"emailtoken(\d+)x")]
    private static partial Regex MaskPattern();

    private static readonly HtmlSanitizer Sanitizer = Create();

    private static HtmlSanitizer Create()
    {
        var s = new HtmlSanitizer();
        s.AllowedAttributes.Add("class"); // harmless in mail, occasionally useful for inline-CSS frameworks
        return s;
    }

    public static string Sanitize(string html)
    {
        var originals = new List<string>();
        var masked = TokenPattern().Replace(html, m =>
        {
            originals.Add(m.Value);
            return $"emailtoken{originals.Count - 1}x";
        });

        var clean = Sanitizer.Sanitize(masked);

        return MaskPattern().Replace(clean, m =>
            int.TryParse(m.Groups[1].Value, out var i) && i < originals.Count ? originals[i] : m.Value);
    }
}
