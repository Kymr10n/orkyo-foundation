using System.Globalization;
using System.Reflection;
using System.Text;
using Api.Models.Reporting;
using Microsoft.AspNetCore.Http;

namespace Api.Reporting;

/// <summary>
/// Serializes any reporting DTO list to CSV.
/// One class, reused by every reporting endpoint — not per-DTO.
///
/// Hand-written rather than delegating to CsvHelper: the used surface was a single
/// WriteRecordsAsync call, and the reporting DTOs are flat records of scalars. The
/// emitted bytes are a published integration contract (Power BI, Excel, Metabase,
/// Superset), so ReportingCsvSerializerTests pins whole documents against the output
/// the previous implementation produced.
/// </summary>
public static class ReportingCsvSerializer
{
    /// <summary>
    /// Fields needing quotes, per RFC 4180 plus the surrounding-whitespace rule: a reader
    /// that trims unquoted fields would otherwise lose a deliberate leading or trailing
    /// space. A lone CR is not in this set — the document's newline is "\n", so a bare
    /// carriage return is ordinary data.
    /// </summary>
    private static readonly char[] MustQuote = [',', '"', '\n'];

    public static IResult ToCsvResult<T>(ReportingResult<T> result, string filename)
    {
        // Reflected once per call, not once per row: the property set is fixed per T.
        var properties = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance);

        return Results.Stream(
            async stream =>
            {
                await using var writer = new StreamWriter(stream, leaveOpen: true);

                await writer.WriteAsync(string.Join(',', properties.Select(p => Escape(p.Name))));
                await writer.WriteAsync('\n');

                foreach (var item in result.Items)
                {
                    var fields = properties.Select(p => Escape(Format(p.GetValue(item))));
                    await writer.WriteAsync(string.Join(',', fields));
                    await writer.WriteAsync('\n');
                }
            },
            contentType: "text/csv",
            fileDownloadName: filename);
    }

    /// <summary>
    /// Invariant formatting throughout: on a machine whose culture uses a comma as the
    /// decimal separator, a culture-sensitive number would split one field into two.
    /// </summary>
    private static string Format(object? value) => value switch
    {
        null => string.Empty,
        string s => s,
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };

    private static string Escape(string field)
    {
        var needsQuotes =
            field.IndexOfAny(MustQuote) >= 0 ||
            (field.Length > 0 && (char.IsWhiteSpace(field[0]) || char.IsWhiteSpace(field[^1])));

        if (!needsQuotes) return field;

        // A quote inside a quoted field is escaped by doubling it.
        return string.Concat("\"", field.Replace("\"", "\"\"", StringComparison.Ordinal), "\"");
    }

    public static bool IsCsvRequested(HttpRequest request, string? format)
        => string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase)
        || request.Headers.Accept.Any(h => h != null &&
               h.Contains("text/csv", StringComparison.OrdinalIgnoreCase));
}
