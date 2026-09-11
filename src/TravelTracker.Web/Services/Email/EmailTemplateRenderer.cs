using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using System.Text.Unicode;
using TravelTracker.Web.Data.Entities;

namespace TravelTracker.Web.Services.Email;

public sealed class EmailTemplateException : Exception
{
    public EmailTemplateException(string message) : base(message) { }
}

// Plain {{Token}} substitution for email templates (ADR-0005). No loops, no
// conditionals, no expressions, no engine dependency. Pure and static so it is
// trivially unit-testable.
//
// Safety: every token value is HTML-encoded before it lands in the body (so a
// trip purpose or comment can never inject markup, and links are safe inside
// href="..."); the subject gets values as plain text with CR/LF stripped so a
// value can never smuggle in an extra mail header.
public static partial class EmailTemplateRenderer
{
    public const int MaxSubjectLength = 256;
    public const int MaxBodyLength = 20_000;

    // Only HTML-significant characters are escaped; non-ASCII (en dashes, accents)
    // passes through so the output matches what the hardcoded emails produced.
    private static readonly HtmlEncoder Html = HtmlEncoder.Create(UnicodeRanges.All);

    [GeneratedRegex(@"\{\{\s*([A-Za-z][A-Za-z0-9.]*)\s*\}\}")]
    private static partial Regex TokenPattern();

    // Distinct token names used in a piece of template text.
    public static IReadOnlyCollection<string> TokensIn(string text) =>
        TokenPattern().Matches(text).Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

    // Save-time validation. Returns human-readable problems; empty means valid.
    public static IReadOnlyList<string> Validate(EmailTemplateKey key, string subject, string htmlBody)
    {
        var spec = EmailTemplateTokens.For(key);
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(subject))
            errors.Add("Subject is required.");
        else if (subject.Length > MaxSubjectLength)
            errors.Add($"Subject must be {MaxSubjectLength} characters or fewer.");

        if (string.IsNullOrWhiteSpace(htmlBody))
            errors.Add("Body is required.");
        else if (htmlBody.Length > MaxBodyLength)
            errors.Add($"Body must be {MaxBodyLength:N0} characters or fewer.");

        var used = TokensIn(subject).Concat(TokensIn(htmlBody)).ToHashSet(StringComparer.Ordinal);

        foreach (var t in used.Where(t => !spec.Allowed.Contains(t, StringComparer.Ordinal)).OrderBy(t => t))
            errors.Add($"Unknown token {{{{{t}}}}}.");

        foreach (var t in spec.Required.Where(t => !used.Contains(t)))
            errors.Add($"Required token {{{{{t}}}}} is missing.");

        return errors;
    }

    // Renders a template against token values. Throws EmailTemplateException on an
    // unknown token or a missing required one, so a drifted stored template can be
    // detected and the caller can fall back to the default.
    public static EmailTemplateContent Render(
        EmailTemplateKey key, EmailTemplateContent template,
        IReadOnlyDictionary<string, string?> tokens)
    {
        var spec = EmailTemplateTokens.For(key);
        var values = new Dictionary<string, string?>(tokens, StringComparer.Ordinal);

        // The only derived token: a whole paragraph, so it's inserted as markup with
        // the comment itself encoded.
        if (spec.Allowed.Contains(EmailTemplateTokens.CommentBlock))
        {
            var comment = values.GetValueOrDefault(EmailTemplateTokens.Comment);
            values[EmailTemplateTokens.CommentBlock] = string.IsNullOrWhiteSpace(comment)
                ? string.Empty
                : $"<p>Comment: {Html.Encode(comment)}</p>";
        }

        foreach (var t in spec.Required)
        {
            if (string.IsNullOrEmpty(values.GetValueOrDefault(t)))
                throw new EmailTemplateException($"Required token {{{{{t}}}}} has no value.");
            if (!TokensIn(template.HtmlBody).Contains(t) && !TokensIn(template.Subject).Contains(t))
                throw new EmailTemplateException($"Template does not use required token {{{{{t}}}}}.");
        }

        var subject = Substitute(template.Subject, spec, values, encode: false);
        var body = Substitute(template.HtmlBody, spec, values, encode: true);
        return new EmailTemplateContent(StripLineBreaks(subject), body);
    }

    private static string Substitute(string text, EmailTemplateTokens.Spec spec,
        IReadOnlyDictionary<string, string?> values, bool encode)
    {
        return TokenPattern().Replace(text, m =>
        {
            var name = m.Groups[1].Value;
            if (!spec.Allowed.Contains(name, StringComparer.Ordinal))
                throw new EmailTemplateException($"Unknown token {{{{{name}}}}}.");

            var value = values.GetValueOrDefault(name) ?? string.Empty;
            if (!encode) return value;
            // CommentBlock is already-built markup; everything else is text.
            return name == EmailTemplateTokens.CommentBlock ? value : Html.Encode(value);
        });
    }

    // Header-injection guard: a subject is a single line, full stop.
    private static string StripLineBreaks(string s) =>
        s.Replace("\r", string.Empty, StringComparison.Ordinal)
         .Replace("\n", string.Empty, StringComparison.Ordinal);
}
