using Bogus;

namespace Orkyo.Foundation.Seed.Narrative;

/// <summary>
/// The 18-month time scaffold around the reference date (~6 months of history → ~12 months ahead),
/// so the demo shows real completed history AND upcoming/planned work — letting the Insights
/// dashboard populate both backward- and forward-looking ranges. Provides working-day logic
/// (Mon–Fri, minus public holidays and plant shutdowns), two shifts, job-slot generation,
/// per-facility campaign windows, and time→status. All times UTC.
/// </summary>
public sealed class YearCalendar
{
    private static readonly (int H, int Len)[] Shifts = [(6, 8), (14, 8)]; // A 06–14, B 14–22

    public DateTime ReferenceDate { get; }
    public DateTime Start { get; }
    public DateTime End { get; }
    public IReadOnlyList<DateOnly> Holidays { get; }
    public IReadOnlyList<(DateTime Start, DateTime End)> Shutdowns { get; }

    public YearCalendar(DateTime referenceDate)
    {
        ReferenceDate = DateTime.SpecifyKind(referenceDate, DateTimeKind.Utc);
        // 6 months of history → 12 months ahead, anchored on the first of the reference month.
        Start = new DateTime(ReferenceDate.Year, ReferenceDate.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(-6);
        End = Start.AddMonths(18);

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

    /// <summary>A shift-aligned [start, end) on the given working day for a job of the given length.</summary>
    public (DateTime Start, DateTime End) MakeSlot(DateTime day, int minHours, int maxHours, Faker faker)
    {
        var (shiftH, shiftLen) = Shifts[faker.Random.Int(0, Shifts.Length - 1)];
        var dur = Math.Clamp(faker.Random.Int(minHours, maxHours), 1, shiftLen);
        var latestStartOffset = shiftLen - dur;
        var startHour = shiftH + faker.Random.Int(0, Math.Max(0, latestStartOffset));
        var start = new DateTime(day.Year, day.Month, day.Day, startHour, 0, 0, DateTimeKind.Utc);
        return (start, start.AddHours(dur));
    }

    /// <summary>Each facility's seasonal campaign window (clipped to the calendar window).</summary>
    public (DateTime Start, DateTime End) CampaignWindow(string siteCode)
    {
        // Stagger campaigns relative to *now* (not the window edge, which is 6 months back):
        // PMF is active now (started last month), FWF kicks off in 2 months, PPF's holiday surge
        // is planned 5 months out.
        var monthBase = new DateTime(ReferenceDate.Year, ReferenceDate.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var offsetMonths = siteCode switch { "PMF" => -1, "FWF" => 2, _ => 5 };
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
