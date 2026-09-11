using System.Text.RegularExpressions;
using TravelTracker.Web.Domain;
using Xunit;

namespace TravelTracker.Tests;

// Pure-logic tests for the trip reference code (ADR-0004): shape, alphabet,
// forgiving normalization, and strict parsing.
public class TripCodeTests
{
    private static readonly Regex Shape = new(@"^TT-\d{4}-[0-9A-HJKMNP-TV-Z]{3}-[0-9A-HJKMNP-TV-Z]{3}$");

    [Fact]
    public void Generate_Has_Canonical_Shape_And_Alphabet()
    {
        for (var i = 0; i < 200; i++)
        {
            var code = TripCode.Generate(2026);
            Assert.Matches(Shape, code);
            Assert.Equal(15, code.Length);
            Assert.DoesNotContain('I', code);
            Assert.DoesNotContain('L', code);
            Assert.DoesNotContain('O', code);
            Assert.DoesNotContain('U', code);
        }
    }

    [Fact]
    public void Generate_Uses_Injected_Index_Source()
    {
        var indexes = new Queue<int>(new[] { 7, 19, 4, 23, 9, 20 }); // 7 K 4 Q 9 M
        var code = TripCode.Generate(2026, indexes.Dequeue);
        Assert.Equal("TT-2026-7K4-Q9M", code);
    }

    [Fact]
    public void Generate_Rejects_Out_Of_Range_Index()
    {
        Assert.Throws<InvalidOperationException>(() => TripCode.Generate(2026, () => 32));
    }

    [Theory]
    [InlineData("TT-2026-7K4-Q9M")]
    [InlineData("tt-2026-7k4-q9m")]
    [InlineData("  tt 2026 7k4q9m ")]
    [InlineData("TT20267K4Q9M")]
    [InlineData("TT-2026-7K4Q9M")]      // the 3+3 split is optional on input
    [InlineData("TT_2026_7K4_Q9M")]
    public void TryParse_Normalizes_Separators_And_Case(string input)
    {
        Assert.True(TripCode.TryParse(input, out var code));
        Assert.Equal("TT-2026-7K4-Q9M", code);
    }

    [Theory]
    [InlineData("TT-2026-OK4-Q9M", "TT-2026-0K4-Q9M")] // O -> 0
    [InlineData("TT-2026-7K4-Q9I", "TT-2026-7K4-Q91")] // I -> 1
    [InlineData("TT-2026-7K4-Q9l", "TT-2026-7K4-Q91")] // l -> 1
    public void TryParse_Applies_Crockford_Aliases(string input, string expected)
    {
        Assert.True(TripCode.TryParse(input, out var code));
        Assert.Equal(expected, code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("7K4Q9M")]              // suffix alone is not a full code
    [InlineData("TT-2026-7K4-Q9")]      // too short
    [InlineData("TT-2026-7K4-Q9MX")]    // too long
    [InlineData("TT-20X6-7K4-Q9M")]     // bad year
    [InlineData("XX-2026-7K4-Q9M")]     // bad prefix
    [InlineData("TT-2026-7K4-Q9U")]     // U is not in the alphabet
    [InlineData("TT-2026-7K4-Q9%")]
    public void TryParse_Rejects_Malformed(string? input)
    {
        Assert.False(TripCode.TryParse(input, out var code));
        Assert.Equal(string.Empty, code);
    }

    [Theory]
    [InlineData("7K4Q9M", "7K4-Q9M")]
    [InlineData("7k4-q9m", "7K4-Q9M")]
    [InlineData(" 7k4 q9m ", "7K4-Q9M")]
    [InlineData("oK4Q9l", "0K4-Q91")]
    public void TryParseSuffix_Accepts_Random_Part_Only(string input, string expected)
    {
        Assert.True(TripCode.TryParseSuffix(input, out var suffix));
        Assert.Equal(expected, suffix);
    }

    [Theory]
    [InlineData("TT-2026-7K4-Q9M")]  // full code is not a suffix
    [InlineData("7K4Q9")]
    [InlineData("Berlin!")]
    [InlineData("")]
    public void TryParseSuffix_Rejects_Other_Input(string input)
    {
        Assert.False(TripCode.TryParseSuffix(input, out _));
    }

    [Fact]
    public void Normalize_Strips_Separators_Uppercases_And_Aliases()
    {
        Assert.Equal("TT20267K4Q9M", TripCode.Normalize(" tt-2026 7k4_q9m "));
        Assert.Equal("0011", TripCode.Normalize("oOiL"));
        Assert.Equal(string.Empty, TripCode.Normalize(null));
    }

    [Fact]
    public void Format_Validates_Parts()
    {
        Assert.Equal("TT-2026-7K4-Q9M", TripCode.Format(2026, "7K4Q9M"));
        Assert.Throws<ArgumentException>(() => TripCode.Format(2026, "7K4Q9"));
        Assert.Throws<ArgumentException>(() => TripCode.Format(2026, "7K4Q9U"));
        Assert.Throws<ArgumentOutOfRangeException>(() => TripCode.Format(999, "7K4Q9M"));
    }
}
