using System.Text;
using TravelTracker.Web.Domain;
using Xunit;

namespace TravelTracker.Tests;

public class CsvWriterTests
{
    private static IEnumerable<IEnumerable<string>> Rows(params string[][] rows) => rows;

    [Fact]
    public void Writes_Header_And_Rows_With_Crlf()
    {
        var csv = CsvWriter.Write(
            new[] { "A", "B" },
            Rows(new[] { "1", "2" }, new[] { "3", "4" }));
        Assert.Equal("A,B\r\n1,2\r\n3,4\r\n", csv);
    }

    [Fact]
    public void Quotes_Fields_Containing_A_Comma()
    {
        var csv = CsvWriter.Write(new[] { "H" }, Rows(new[] { "a,b" }));
        Assert.Equal("H\r\n\"a,b\"\r\n", csv);
    }

    [Fact]
    public void Doubles_Embedded_Quotes()
    {
        var csv = CsvWriter.Write(new[] { "H" }, Rows(new[] { "say \"hi\"" }));
        Assert.Equal("H\r\n\"say \"\"hi\"\"\"\r\n", csv);
    }

    [Fact]
    public void Quotes_Fields_Containing_Newlines()
    {
        var csv = CsvWriter.Write(new[] { "H" }, Rows(new[] { "line1\nline2" }));
        Assert.Equal("H\r\n\"line1\nline2\"\r\n", csv);
    }

    [Fact]
    public void Plain_Fields_Are_Not_Quoted()
    {
        var csv = CsvWriter.Write(new[] { "H" }, Rows(new[] { "plain" }));
        Assert.Equal("H\r\nplain\r\n", csv);
    }

    [Fact]
    public void ToBytes_Prepends_Utf8_Bom()
    {
        var bytes = CsvWriter.ToBytes(new[] { "H" }, Rows(new[] { "x" }));
        var bom = Encoding.UTF8.GetPreamble();
        Assert.Equal(bom, bytes.Take(bom.Length).ToArray());
        Assert.Equal("H\r\nx\r\n", Encoding.UTF8.GetString(bytes, bom.Length, bytes.Length - bom.Length));
    }

    [Fact]
    public void Money_Is_Invariant_Two_Decimals()
    {
        Assert.Equal("1234.50", CsvWriter.Money(1234.5m));
        Assert.Equal("0.00", CsvWriter.Money(0m));
        Assert.Equal("-5.00", CsvWriter.Money(-5m));
    }

    [Fact]
    public void Number_Is_Invariant()
    {
        Assert.Equal("42", CsvWriter.Number(42));
    }
}
