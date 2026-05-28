using System.Globalization;
using System.Reflection;
using Api.Models.Reporting;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.AspNetCore.Http;

namespace Api.Reporting;

/// <summary>
/// Serializes any reporting DTO list to CSV using CsvHelper.
/// One class, reused by every reporting endpoint — not per-DTO.
/// </summary>
public static class ReportingCsvSerializer
{
    private static readonly CsvConfiguration Config = new(CultureInfo.InvariantCulture)
    {
        HasHeaderRecord = true,
        NewLine = "\n",
    };

    public static IResult ToCsvResult<T>(ReportingResult<T> result, string filename)
    {
        return Results.Stream(
            async stream =>
            {
                await using var writer = new StreamWriter(stream, leaveOpen: true);
                await using var csv = new CsvWriter(writer, Config);
                await csv.WriteRecordsAsync(result.Items);
            },
            contentType: "text/csv",
            fileDownloadName: filename);
    }

    public static bool IsCsvRequested(HttpRequest request, string? format)
        => string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase)
        || request.Headers.Accept.Any(h => h != null &&
               h.Contains("text/csv", StringComparison.OrdinalIgnoreCase));
}
