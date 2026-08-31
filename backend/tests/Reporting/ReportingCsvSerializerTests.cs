using System.Text;
using Api.Models.Reporting;
using Api.Reporting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Orkyo.Foundation.Tests.Reporting;

/// <summary>
/// Byte-parity tests for the reporting CSV writer.
///
/// The reporting endpoints are a published integration surface (Power BI, Excel, Metabase,
/// Superset) and the CSV body is what those tools parse, so the exact bytes are the contract.
/// These expectations were captured from the CsvHelper implementation before it was replaced,
/// which is why they assert whole documents rather than "contains" fragments: a quoting or
/// number-format change has to fail here rather than in a customer's workbook.
/// </summary>
public class ReportingCsvSerializerTests
{
    private sealed class Row
    {
        public string Name { get; init; } = "";
        public string? Optional { get; init; }
        public int Count { get; init; }
        public double Ratio { get; init; }
        public double? MaybeRatio { get; init; }
        public bool Flag { get; init; }
        public DateTime When { get; init; }
        public DateTime? MaybeWhen { get; init; }
    }

    private static async Task<string> RenderAsync<T>(params T[] items)
    {
        var result = ReportingCsvSerializer.ToCsvResult(
            new ReportingResult<T> { Items = items, Metadata = new ReportingMetadata() },
            "report.csv");

        // PushStreamHttpResult resolves ILoggerFactory from the request services.
        var context = new DefaultHttpContext { RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider() };
        using var body = new MemoryStream();
        context.Response.Body = body;
        await result.ExecuteAsync(context);
        return Encoding.UTF8.GetString(body.ToArray());
    }

    [Fact]
    public async Task WritesHeaderInDeclarationOrder_ThenOneLinePerRow()
    {
        var csv = await RenderAsync(new Row
        {
            Name = "Assembly line",
            Optional = "note",
            Count = 42,
            Ratio = 0.75,
            MaybeRatio = 1.5,
            Flag = true,
            When = new DateTime(2026, 3, 12, 14, 30, 0, DateTimeKind.Utc),
            MaybeWhen = new DateTime(2026, 3, 13, 0, 0, 0, DateTimeKind.Utc),
        });

        csv.Should().Be(
            "Name,Optional,Count,Ratio,MaybeRatio,Flag,When,MaybeWhen\n" +
            "Assembly line,note,42,0.75,1.5,True,03/12/2026 14:30:00,03/13/2026 00:00:00\n");
    }

    [Fact]
    public async Task WritesNullsAsEmptyFields()
    {
        var csv = await RenderAsync(new Row
        {
            Name = "x",
            Optional = null,
            Count = 0,
            Ratio = 0,
            MaybeRatio = null,
            Flag = false,
            When = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            MaybeWhen = null,
        });

        csv.Should().Be(
            "Name,Optional,Count,Ratio,MaybeRatio,Flag,When,MaybeWhen\n" +
            "x,,0,0,,False,01/01/2026 00:00:00,\n");
    }

    [Theory]
    // A comma would otherwise split one field into two.
    [InlineData("Bay 1, Bay 2", "\"Bay 1, Bay 2\"")]
    // A quote is escaped by doubling it, and the field is then quoted.
    [InlineData("The \"Big\" Press", "\"The \"\"Big\"\" Press\"")]
    // A newline inside a field would otherwise look like the start of a new record.
    [InlineData("line one\nline two", "\"line one\nline two\"")]
    // A LONE carriage return is deliberately NOT quoted: the document's newline is "\n",
    // so a bare \r is just data. Captured from the previous implementation — parity, not taste.
    [InlineData("carriage\rreturn", "carriage\rreturn")]
    // Leading/trailing spaces are preserved by quoting; parsers otherwise trim them.
    [InlineData(" leading", "\" leading\"")]
    [InlineData("trailing ", "\"trailing \"")]
    // Nothing special: no quotes added.
    [InlineData("plain", "plain")]
    [InlineData("", "")]
    public async Task QuotesOnlyTheFieldsThatNeedIt(string value, string expected)
    {
        var csv = await RenderAsync(new Row { Name = value, When = default });

        var secondLine = csv[(csv.IndexOf('\n') + 1)..];
        secondLine.Should().StartWith(expected + ",");
    }

    [Fact]
    public async Task UsesInvariantCultureForNumbers()
    {
        // A machine with a comma decimal separator must still emit a dot, or every
        // fractional value would silently become a field separator.
        var csv = await RenderAsync(new Row { Name = "n", Ratio = 1234.5678, When = default });

        csv.Should().Contain("1234.5678");
    }

    [Fact]
    public async Task WritesHeaderOnlyForAnEmptyResult()
    {
        var csv = await RenderAsync<Row>();

        csv.Should().Be("Name,Optional,Count,Ratio,MaybeRatio,Flag,When,MaybeWhen\n");
    }

    [Fact]
    public async Task UsesUnixNewlinesThroughout()
    {
        var csv = await RenderAsync(
            new Row { Name = "a", When = default },
            new Row { Name = "b", When = default });

        csv.Should().NotContain("\r\n");
        csv.Split('\n').Length.Should().Be(4); // header + 2 rows + trailing empty
    }
}
