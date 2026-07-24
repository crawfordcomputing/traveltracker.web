using System.Security.Cryptography;

namespace TravelTracker.Web.Domain;

// Generates a one-time password an admin hands to a user (new account, or a reset).
// Cryptographically strong and guaranteed to satisfy the configured Identity policy
// (>=8 chars, at least one lowercase and one digit). Shared by Admin/Users Create
// and the reset-password action so the two can't drift.
public static class TempPassword
{
    public static string Generate()
    {
        const string lower = "abcdefghijkmnpqrstuvwxyz";
        const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        const string digits = "23456789";
        const string symbols = "!@#$%*?-_";
        const string all = lower + upper + digits + symbols;

        var chars = new List<char>
        {
            Pick(lower), Pick(upper), Pick(digits), Pick(symbols)
        };
        while (chars.Count < 16) chars.Add(Pick(all));

        // Fisher-Yates shuffle with crypto-strong indices.
        for (int i = chars.Count - 1; i > 0; i--)
        {
            int j = RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }
        return new string(chars.ToArray());
    }

    private static char Pick(string set) => set[RandomNumberGenerator.GetInt32(set.Length)];
}
