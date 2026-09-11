using System.Security.Cryptography;
using System.Text;

namespace TravelTracker.Web.Domain;

// Immutable, human-readable trip reference: TT-{YYYY}-{XXX}-{XXX}, e.g.
// TT-2026-7K4-Q9M. The year is the UTC year the trip was created (context for
// humans only); the six random characters carry the uniqueness (32^6 ≈ 1.07 billion
// per year). Crockford Base32 alphabet: no I, L, O (confusable with 1/0) and no U.
// Codes are stored and displayed in canonical form; input is normalized before
// matching so "tt 2026 7k4q9m" resolves the same trip. Assigned centrally in
// AppDbContext.SaveChangesAsync so every creation path gets one. See ADR-0004.
public static class TripCode
{
    public const string Prefix = "TT";
    public const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    public const int RandomLength = 6;
    // TT-2026-7K4-Q9M is 15 chars; the column is nvarchar(20) to leave headroom.
    public const int MaxLength = 20;

    // A fresh candidate code for the given creation year, using a crypto RNG.
    public static string Generate(int year) =>
        Generate(year, () => RandomNumberGenerator.GetInt32(Alphabet.Length));

    // Same, with an injectable index source (0..31 per call) so tests can force
    // collisions. Out-of-range indexes throw rather than silently wrapping.
    public static string Generate(int year, Func<int> nextIndex)
    {
        ArgumentNullException.ThrowIfNull(nextIndex);
        var random = new char[RandomLength];
        for (var i = 0; i < RandomLength; i++)
        {
            var idx = nextIndex();
            if (idx < 0 || idx >= Alphabet.Length)
                throw new InvalidOperationException($"Random index {idx} is outside the alphabet.");
            random[i] = Alphabet[idx];
        }
        return Format(year, new string(random));
    }

    // Canonical form from its parts. `random` must already be 6 alphabet chars.
    public static string Format(int year, string random)
    {
        if (year < 1000 || year > 9999)
            throw new ArgumentOutOfRangeException(nameof(year), year, "Year must be four digits.");
        if (random is null || random.Length != RandomLength || !random.All(IsAlphabet))
            throw new ArgumentException("Random part must be six Crockford Base32 characters.", nameof(random));
        return $"{Prefix}-{year}-{random[..3]}-{random[3..]}";
    }

    // Forgiving normalization: trim, drop whitespace and hyphens, upper-case, then
    // apply the Crockford decoding aliases O->0 and I/L->1. Returns the compact
    // form (no separators), e.g. "TT20267K4Q9M". Never throws.
    public static string Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        var sb = new StringBuilder(input.Length);
        foreach (var raw in input)
        {
            if (char.IsWhiteSpace(raw) || raw == '-' || raw == '_') continue;
            var c = char.ToUpperInvariant(raw);
            sb.Append(c switch
            {
                'O' => '0',
                'I' or 'L' => '1',
                _ => c,
            });
        }
        return sb.ToString();
    }

    // Parses any user-typed variant of a full code into its canonical form.
    public static bool TryParse(string? input, out string code)
    {
        code = string.Empty;
        var n = Normalize(input);
        // TT + YYYY + XXXXXX
        if (n.Length != Prefix.Length + 4 + RandomLength) return false;
        if (!n.StartsWith(Prefix, StringComparison.Ordinal)) return false;

        var yearPart = n.Substring(Prefix.Length, 4);
        if (!yearPart.All(char.IsAsciiDigit)) return false;

        var random = n[(Prefix.Length + 4)..];
        if (!random.All(IsAlphabet)) return false;

        code = Format(int.Parse(yearPart, System.Globalization.CultureInfo.InvariantCulture), random);
        return true;
    }

    // Parses just the random part ("7K4Q9M", "7k4-q9m") into its canonical hyphenated
    // suffix ("7K4-Q9M"), for searching by the short form. The suffix is unique per
    // year only, so callers search with EndsWith rather than equality.
    public static bool TryParseSuffix(string? input, out string suffix)
    {
        suffix = string.Empty;
        var n = Normalize(input);
        if (n.Length != RandomLength || !n.All(IsAlphabet)) return false;
        suffix = $"{n[..3]}-{n[3..]}";
        return true;
    }

    private static bool IsAlphabet(char c) => Alphabet.Contains(c, StringComparison.Ordinal);
}
