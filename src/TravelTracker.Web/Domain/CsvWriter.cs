using System.Globalization;
using System.Text;

namespace TravelTracker.Web.Domain;

// Minimal, zero-dependency CSV writer following RFC 4180: fields containing a
// comma, double-quote, CR, or LF are wrapped in double-quotes with embedded quotes
// doubled. Rows are joined with CRLF (the RFC line terminator, and what Excel
// expects). Pure and unit-testable — the reports feed it string[] rows.
//
// A leading UTF-8 BOM is prepended so Excel opens accented text / currency symbols
// in the right encoding on a double-click. Numeric fields are formatted by the
// caller with InvariantCulture (Format helper) so a comma decimal separator never
// collides with the field delimiter.
public static class CsvWriter
{
    public static string Write(IEnumerable<string> header, IEnumerable<IEnumerable<string>> rows)
    {
        var sb = new StringBuilder();
        AppendRow(sb, header);
        foreach (var row in rows)
            AppendRow(sb, row);
        return sb.ToString();
    }

    // The bytes to stream as a file: UTF-8 with BOM.
    public static byte[] ToBytes(IEnumerable<string> header, IEnumerable<IEnumerable<string>> rows)
    {
        var csv = Write(header, rows);
        var bom = Encoding.UTF8.GetPreamble();
        var body = Encoding.UTF8.GetBytes(csv);
        var buffer = new byte[bom.Length + body.Length];
        Buffer.BlockCopy(bom, 0, buffer, 0, bom.Length);
        Buffer.BlockCopy(body, 0, buffer, bom.Length, body.Length);
        return buffer;
    }

    // Invariant, delimiter-safe formatting for money / numbers.
    public static string Money(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);
    public static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static void AppendRow(StringBuilder sb, IEnumerable<string> fields)
    {
        var first = true;
        foreach (var field in fields)
        {
            if (!first) sb.Append(',');
            sb.Append(Escape(field));
            first = false;
        }
        sb.Append("\r\n");
    }

    private static string Escape(string? field)
    {
        field ??= string.Empty;
        var mustQuote = field.Contains(',') || field.Contains('"')
                        || field.Contains('\n') || field.Contains('\r');
        if (!mustQuote) return field;
        return "\"" + field.Replace("\"", "\"\"") + "\"";
    }
}
