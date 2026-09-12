using Bogus;

namespace Orkyo.Foundation.Seed.Narrative;

/// <summary>
/// The time scaffold around the reference date (~6 months of history → ~12 months ahead),
/// so the demo shows real completed history AND upcoming/planned work — letting the Insights
/// dashboard populate both backward- and forward-looking ranges. Provides working-day logic
/// (Mon–Fri, minus public holidays and plant shutdowns), the working shift, job-slot generation,
/// per-facility campaign windows, and time→status. All times UTC.
///
/// Slots are authored in SITE LOCAL time and converted on the way out. They used to be UTC while
/// the sites run Europe/Berlin, so the late shift (14–22 UTC = 16:00–24:00 local in summer) fell
/// almost entirely outside working hours — and Insights masks booked minutes to the working
/// calendar, which silently discarded about a third of every seeded person-hour. Authoring locally
/// is what keeps the seeded day inside the day the product counts.
/// </summary>
public sealed class YearCalendar
{
    /// <summary>The zone every seeded site runs in — see <c>TenantConfigFactory</c>, which seeds
    /// the matching <c>scheduling_settings</c> from this same constant.</summary>
    public const string SiteTimeZoneId = "Europe/Berlin";

    public static readonly TimeZoneInfo SiteTimeZone = TimeZoneInfo.FindSystemTimeZoneById(SiteTimeZoneId);

    /// <summary>Site working hours, local. Must match <c>TenantConfigFactory</c>.</summary>
    /// <remarks>
    /// ONE eight-hour shift, and the length is load-bearing. Insights divides a resource's booked
    /// minutes by the site's working minutes, so the working day is the denominator every
    /// utilization number on the dashboard is measured against. The demo used to declare a
    /// twelve-hour two-shift day while staffing a single shift: a person working a full eight-hour
    /// day then read 8/12 ≈ 67 % at best, and with shorter jobs nearer 50 %, which is what made the
    /// shop look idle. Lowering people's own availability instead is not an option — people are
    /// Fractional, and the validator turns any allocation above their availability into an
    /// overbooking blocker, so a 67 %-available person would flag every job they lead.
    /// </remarks>
    public const int WorkDayStartHour = 6;
    public const int WorkDayEndHour = 14;

    // One shift, matching the working day above, so a full day's job fills a full day.
    private static readonly (int H, int Len)[] Shifts = [(6, 8)];

    public DateTime ReferenceDate { get; }
    public DateTime Start { get; }
    public DateTime End { get; }
    public IReadOnlyList<DateOnly> Holidays { get; }
    public IReadOnlyList<(DateTime Start, DateTime End)> Shutdowns { get; }

    public YearCalendar(DateTime referenceDate)
    {
        ReferenceDate = DateTime.SpecifyKind(referenceDate, DateTimeKind.Utc);
        // 6 months of history → 12 months ahead, anchored on the first of the reference month.
        // 19 months, not 18: the dashboard's default range ends at (today + 12 months), so a
        // window that stopped at month +11 left the final bucket empty and the chart fell off a
        // cliff to 0 % at its right edge.
        Start = new DateTime(ReferenceDate.Year, ReferenceDate.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(-6);
        End = Start.AddMonths(19);

        Holidays = BuildHolidays();
        Shutdowns = BuildShutdowns();
    }

    // Fixed public holidays applied to every calendar year the window touches, clipped to the window.
    private static readonly (int Month, int Day, string Name)[] HolidayDefs =
    [
        (1, 1, "New Year's Day"), (5, 1, "Labour Day"), (7, 4, "Independence Day"),
        (11, 27, "Thanksgiving"), (12, 25, "Christmas Day"), (12, 26, "Boxing Day"),
    ];

    private IReadOnlyList<DateOnly> BuildHolidays()
    {
        var list = new List<DateOnly>();
        for (var year = Start.Year; year <= End.Year; year++)
            foreach (var (m, d, _) in HolidayDefs)
            {
                var date = new DateTime(year, m, d, 0, 0, 0, DateTimeKind.Utc);
                if (date >= Start && date < End) list.Add(DateOnly.FromDateTime(date));
            }
        return list;
    }

    private IReadOnlyList<(DateTime, DateTime)> BuildShutdowns()
    {
        var list = new List<(DateTime, DateTime)>();
        for (var year = Start.Year; year <= End.Year; year++)
        {
            // Two-week summer maintenance shutdown (first half of August).
            Add(list, new DateTime(year, 8, 1, 0, 0, 0, DateTimeKind.Utc), TimeSpan.FromDays(14));
            // Year-end winter shutdown (Dec 24 → Jan 1).
            Add(list, new DateTime(year, 12, 24, 0, 0, 0, DateTimeKind.Utc), TimeSpan.FromDays(9));
        }
        return list;
    }

    private void Add(List<(DateTime, DateTime)> list, DateTime start, TimeSpan len)
    {
        var end = start + len;
        if (end > Start && start < End) list.Add((start, end));
    }

    public bool IsHoliday(DateOnly d) => Holidays.Contains(d);

    public bool IsShutdown(DateTime day) =>
        Shutdowns.Any(s => day >= s.Start && day < s.End);

    public bool IsWorkingDay(DateTime day) =>
        day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)
        && !IsHoliday(DateOnly.FromDateTime(day))
        && !IsShutdown(day);

    /// <summary>Random working day in [from, to); null if none found in a bounded number of tries.</summary>
    public DateTime? PickWorkingDay(DateTime from, DateTime to, Faker faker)
    {
        if (to <= from) return null;
        var spanDays = Math.Max(1, (int)(to - from).TotalDays);
        for (var attempt = 0; attempt < 30; attempt++)
        {
            var day = from.Date.AddDays(faker.Random.Int(0, spanDays - 1));
            if (day >= Start && day < End && IsWorkingDay(day)) return day;
        }
        return null;
    }

    /// <summary>A shift-aligned [start, end) on the given working day for a job of the given length.
    /// Authored in site-local hours and returned in UTC.</summary>
    public (DateTime Start, DateTime End) MakeSlot(DateTime day, int minHours, int maxHours, Faker faker)
    {
        var (shiftH, shiftLen) = Shifts[faker.Random.Int(0, Shifts.Length - 1)];
        var dur = Math.Clamp(faker.Random.Int(minHours, maxHours), 1, shiftLen);
        // Never start so late that the job would run past the working day.
        var latestStartHour = Math.Min(shiftH + (shiftLen - dur), WorkDayEndHour - dur);
        var startHour = faker.Random.Int(shiftH, Math.Max(shiftH, latestStartHour));
        var start = ToUtc(day, startHour);
        return (start, start.AddHours(dur));
    }

    /// <summary>The site's working window for a day, in UTC — the span Insights counts against.</summary>
    public (DateTime Start, DateTime End) WorkingWindow(DateTime day) =>
        (ToUtc(day, WorkDayStartHour), ToUtc(day, WorkDayEndHour));

    /// <summary>Site-local wall-clock hour on <paramref name="day"/>, as UTC.</summary>
    private static DateTime ToUtc(DateTime day, int localHour)
    {
        // Unspecified kind: ConvertTimeToUtc treats it as a wall-clock reading in the given zone.
        // Every hour used here is ≥ 06:00, so the spring-forward gap (02:00–03:00) is never hit.
        var local = new DateTime(day.Year, day.Month, day.Day, localHour, 0, 0, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(local, SiteTimeZone);
    }

    /// <summary>Every working day in the window, ascending — the spine of the demand loop.</summary>
    public IEnumerable<DateTime> WorkingDays()
    {
        for (var d = Start; d < End; d = d.AddDays(1))
            if (IsWorkingDay(d)) yield return d;
    }

    /// <summary>Each facility's seasonal campaign window (clipped to the calendar window).</summary>
    public (DateTime Start, DateTime End) CampaignWindow(string siteCode)
    {
        // Stagger campaigns relative to *now* (not the window edge, which is 6 months back), and
        // keep all three peaks inside the part of the dashboard a visitor actually looks at:
        // PMF's ran last quarter, FWF's is running now, PPF's starts next quarter. Pushing PPF
        // out to +5..+8 months used to park the only dense band at the far right of the chart.
        var monthBase = new DateTime(ReferenceDate.Year, ReferenceDate.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var offsetMonths = siteCode switch { "PMF" => -3, "FWF" => 0, _ => 3 };
        var s = monthBase.AddMonths(offsetMonths);
        var e = s.AddMonths(3);
        return (s < Start ? Start : s, e > End ? End : e);
    }

    /// <summary>Quarter/month anchors within the window for recurring (PM/QA) cadences.</summary>
    public IEnumerable<DateTime> MonthStarts()
    {
        for (var m = Start; m < End; m = m.AddMonths(1)) yield return m;
    }

    public string StatusFor(DateTime start, DateTime end, Faker faker)
    {
        if (end <= ReferenceDate)
            return faker.Random.Bool(0.04f) ? "cancelled" : "done";
        if (start <= ReferenceDate && end > ReferenceDate)
            return "in_progress";
        return faker.Random.Bool(0.03f) ? "cancelled" : "new";
    }
}
