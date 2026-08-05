using System.Text;
using Api.Services;

namespace Orkyo.Foundation.Tests.Services;

public class ICalendarWriterTests
{
    private static CalendarFeedEvent Event(string summary = "Pack customer orders", string? location = null) => new()
    {
        Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
        Summary = summary,
        StartUtc = new DateTime(2026, 8, 15, 9, 0, 0, DateTimeKind.Utc),
        EndUtc = new DateTime(2026, 8, 15, 10, 30, 0, DateTimeKind.Utc),
        Location = location,
        LastModifiedUtc = new DateTime(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc),
    };

    private static string Write(params CalendarFeedEvent[] events) =>
        ICalendarWriter.Write(events, "Orkyo schedule", "orkyo.com");

    [Fact]
    public void Write_ProducesAWellFormedCalendarEnvelope()
    {
        var ics = Write(Event());

        ics.Should().StartWith("BEGIN:VCALENDAR\r\n");
        ics.Should().EndWith("END:VCALENDAR\r\n");
        ics.Should().Contain("VERSION:2.0");
        // Outlook reads X-PUBLISHED-TTL; newer clients read REFRESH-INTERVAL. Both.
        ics.Should().Contain("X-PUBLISHED-TTL:PT60M");
        ics.Should().Contain("REFRESH-INTERVAL;VALUE=DURATION:PT60M");
    }

    [Fact]
    public void Write_UsesCrlfLineEndings()
    {
        // RFC 5545 §3.1 requires CRLF; bare LF makes strict parsers reject the file.
        var ics = Write(Event());
        ics.Replace("\r\n", "").Should().NotContain("\n");
    }

    [Fact]
    public void Write_EmitsUtcTimestampsInBasicFormat()
    {
        var ics = Write(Event());

        ics.Should().Contain("DTSTART:20260815T090000Z");
        ics.Should().Contain("DTEND:20260815T103000Z");
    }

    [Fact]
    public void Write_ConvertsNonUtcTimestampsRatherThanMislabellingThem()
    {
        var local = new CalendarFeedEvent
        {
            Id = Guid.NewGuid(),
            Summary = "Local",
            StartUtc = new DateTime(2026, 8, 15, 9, 0, 0, DateTimeKind.Utc).ToLocalTime(),
            EndUtc = new DateTime(2026, 8, 15, 10, 0, 0, DateTimeKind.Utc).ToLocalTime(),
            LastModifiedUtc = DateTime.UtcNow,
        };

        // A Local DateTime stamped with Z would move the event by the offset.
        ICalendarWriter.Write([local], "n", "orkyo.com").Should().Contain("DTSTART:20260815T090000Z");
    }

    [Fact]
    public void Write_GivesEachEventAStableUid()
    {
        // Re-fetching must update the event in place, not duplicate it.
        Write(Event()).Should().Contain("UID:11111111-1111-1111-1111-111111111111@orkyo.com");
    }

    [Fact]
    public void Write_OmitsOptionalPropertiesThatHaveNoValue()
    {
        var ics = Write(Event(location: null));

        ics.Should().NotContain("LOCATION:");
        ics.Should().NotContain("DESCRIPTION:");
    }

    [Theory]
    [InlineData("Pack, then ship", "Pack\\, then ship")]
    [InlineData("Bay 1; Bay 2", "Bay 1\\; Bay 2")]
    [InlineData("C:\\temp", "C:\\\\temp")]
    public void Escape_ProtectsTheCharactersThatWouldSplitAProperty(string input, string expected)
    {
        ICalendarWriter.Escape(input).Should().Be(expected);
    }

    [Fact]
    public void Escape_TurnsNewlinesIntoTheLiteralEscape()
    {
        // A raw newline inside a value ends the property and corrupts the file.
        ICalendarWriter.Escape("line one\r\nline two").Should().Be("line one\\nline two");
        ICalendarWriter.Escape("line one\nline two").Should().Be("line one\\nline two");
    }

    [Fact]
    public void Write_EscapesValuesInsideTheEvent()
    {
        var ics = Write(Event(summary: "Pack, sort; ship"));
        ics.Should().Contain("SUMMARY:Pack\\, sort\\; ship");
    }

    [Fact]
    public void AppendLine_LeavesShortLinesUnfolded()
    {
        var sb = new StringBuilder();
        ICalendarWriter.AppendLine(sb, "SUMMARY:short");
        sb.ToString().Should().Be("SUMMARY:short\r\n");
    }

    [Fact]
    public void AppendLine_FoldsLongLinesToSeventyFiveOctetsWithLeadingSpace()
    {
        var sb = new StringBuilder();
        ICalendarWriter.AppendLine(sb, "SUMMARY:" + new string('x', 200));

        var lines = sb.ToString().Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        lines.Length.Should().BeGreaterThan(1);
        lines.Should().OnlyContain(l => Encoding.UTF8.GetByteCount(l) <= 75);
        lines.Skip(1).Should().OnlyContain(l => l.StartsWith(' '));
    }

    [Fact]
    public void AppendLine_FoldsByOctetsSoMultibyteCharactersSurvive()
    {
        // Folding by character count would split a multi-byte sequence and produce
        // mojibake — the classic failure of a naive writer on non-English names.
        var sb = new StringBuilder();
        var line = "SUMMARY:" + string.Concat(Enumerable.Repeat("Grüße", 40));
        ICalendarWriter.AppendLine(sb, line);

        var folded = sb.ToString();
        folded.Replace("\r\n ", "").TrimEnd('\r', '\n').Should().Be(line);
        foreach (var l in folded.Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
        {
            Encoding.UTF8.GetByteCount(l).Should().BeLessThanOrEqualTo(75);
        }
    }

    [Fact]
    public void Write_HandlesAnEmptyScheduleWithoutProducingAnInvalidFile()
    {
        var ics = ICalendarWriter.Write([], "Orkyo schedule", "orkyo.com");

        ics.Should().Contain("BEGIN:VCALENDAR");
        ics.Should().Contain("END:VCALENDAR");
        ics.Should().NotContain("BEGIN:VEVENT");
    }
}
