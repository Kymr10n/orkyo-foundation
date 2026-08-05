using System.Globalization;
using System.Text;

namespace Api.Services;

/// <summary>One scheduled item as a calendar client sees it.</summary>
public record CalendarFeedEvent
{
    public required Guid Id { get; init; }
    public required string Summary { get; init; }
    public required DateTime StartUtc { get; init; }
    public required DateTime EndUtc { get; init; }
    public string? Description { get; init; }
    /// <summary>Resource names, joined into the event's location line.</summary>
    public string? Location { get; init; }
    public required DateTime LastModifiedUtc { get; init; }
}

/// <summary>
/// Writes RFC 5545 iCalendar text.
///
/// Hand-written rather than pulled from a package: the subset a read-only feed
/// needs is a dozen properties, and the two rules that actually bite — escaping
/// and 75-octet line folding — are cheaper to own and test here than to audit in
/// a dependency (and this repo takes no dependency it can avoid).
/// </summary>
public static class ICalendarWriter
{
    private const string ProductId = "-//Orkyo//Schedule Feed//EN";

    /// <summary>
    /// Renders a full VCALENDAR. <paramref name="feedName"/> becomes the calendar
    /// name Outlook shows; <paramref name="refreshMinutes"/> tells clients how
    /// often to poll (they treat it as a hint, not an instruction).
    /// </summary>
    public static string Write(
        IEnumerable<CalendarFeedEvent> events,
        string feedName,
        string domain,
        int refreshMinutes = 60)
    {
        var sb = new StringBuilder();
        AppendLine(sb, "BEGIN:VCALENDAR");
        AppendLine(sb, "VERSION:2.0");
        AppendLine(sb, $"PRODID:{ProductId}");
        AppendLine(sb, "CALSCALE:GREGORIAN");
        AppendLine(sb, "METHOD:PUBLISH");
        AppendLine(sb, $"X-WR-CALNAME:{Escape(feedName)}");
        // Both spellings: X-PUBLISHED-TTL is what Outlook reads, REFRESH-INTERVAL
        // is the standardised one (RFC 7986) that newer clients prefer.
        AppendLine(sb, $"X-PUBLISHED-TTL:PT{refreshMinutes}M");
        AppendLine(sb, $"REFRESH-INTERVAL;VALUE=DURATION:PT{refreshMinutes}M");

        foreach (var e in events)
        {
            AppendLine(sb, "BEGIN:VEVENT");
            // Stable per request: re-fetching updates the event in place instead
            // of duplicating it, which is the whole point of a subscription.
            AppendLine(sb, $"UID:{e.Id}@{domain}");
            AppendLine(sb, $"DTSTAMP:{FormatUtc(e.LastModifiedUtc)}");
            AppendLine(sb, $"DTSTART:{FormatUtc(e.StartUtc)}");
            AppendLine(sb, $"DTEND:{FormatUtc(e.EndUtc)}");
            AppendLine(sb, $"SUMMARY:{Escape(e.Summary)}");
            if (!string.IsNullOrWhiteSpace(e.Description))
                AppendLine(sb, $"DESCRIPTION:{Escape(e.Description)}");
            if (!string.IsNullOrWhiteSpace(e.Location))
                AppendLine(sb, $"LOCATION:{Escape(e.Location)}");
            AppendLine(sb, $"LAST-MODIFIED:{FormatUtc(e.LastModifiedUtc)}");
            AppendLine(sb, "END:VEVENT");
        }

        AppendLine(sb, "END:VCALENDAR");
        return sb.ToString();
    }

    /// <summary>RFC 5545 §3.3.5 UTC form: 20260815T090000Z.</summary>
    private static string FormatUtc(DateTime value)
    {
        var utc = value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
        return utc.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// RFC 5545 §3.3.11: backslash, semicolon and comma are escaped, newlines
    /// become the literal two characters \n. A request named "Pack, then ship"
    /// would otherwise split the property into two values.
    /// </summary>
    internal static string Escape(string value) =>
        value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace(";", "\\;", StringComparison.Ordinal)
            .Replace(",", "\\,", StringComparison.Ordinal)
            .Replace("\r\n", "\\n", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\r", "\\n", StringComparison.Ordinal);

    /// <summary>
    /// Appends one content line, folded to 75 octets (RFC 5545 §3.1) with CRLF
    /// endings. Folding counts UTF-8 bytes, not characters: splitting mid-sequence
    /// produces a corrupt file, which is how long non-ASCII names break naive
    /// writers.
    /// </summary>
    internal static void AppendLine(StringBuilder sb, string line)
    {
        const int maxOctets = 75;
        var bytes = Encoding.UTF8.GetByteCount(line);
        if (bytes <= maxOctets)
        {
            sb.Append(line).Append("\r\n");
            return;
        }

        var first = true;
        var index = 0;
        while (index < line.Length)
        {
            // Continuation lines start with a space, which itself costs an octet.
            var budget = first ? maxOctets : maxOctets - 1;
            var taken = TakeByOctets(line, index, budget);
            if (!first) sb.Append(' ');
            sb.Append(line, index, taken).Append("\r\n");
            index += taken;
            first = false;
        }
    }

    /// <summary>How many chars from <paramref name="start"/> fit in the octet budget without splitting a surrogate pair.</summary>
    private static int TakeByOctets(string line, int start, int budget)
    {
        var octets = 0;
        var count = 0;
        while (start + count < line.Length)
        {
            var isSurrogatePair = char.IsHighSurrogate(line[start + count]) && start + count + 1 < line.Length;
            var charLength = isSurrogatePair ? 2 : 1;
            var next = Encoding.UTF8.GetByteCount(line.Substring(start + count, charLength));
            if (octets + next > budget) break;
            octets += next;
            count += charLength;
        }
        // A single character wider than the budget would otherwise loop forever.
        return Math.Max(count, 1);
    }
}
