using Bogus;

namespace Orkyo.Foundation.Seed.Distributions;

/// <summary>
/// Samples request time-ranges around a reference date. Distribution:
///   - 35% past + done
///   - 15% currently in-progress (overlaps reference date)
///   - 50% future planned
///   - 40% of total remain unscheduled (no start/end)
/// </summary>
public sealed class RequestTimeDistribution
{
    public enum Slot { Unscheduled, PastDone, InProgress, Future }

    private readonly DateTime _reference;
    private readonly int _windowDays;
    private readonly Faker _faker;

    public RequestTimeDistribution(DateTime reference, int windowDays, Faker faker)
    {
        _reference = reference;
        _windowDays = windowDays;
        _faker = faker;
    }

    public Slot Pick()
    {
        var roll = _faker.Random.Double();
        if (roll < 0.40) return Slot.Unscheduled;
        // Of the scheduled 60%:
        //  - 35% past+done  → 0.40..0.61
        //  - 15% in-progress → 0.61..0.70
        //  - 50% future      → 0.70..1.00
        if (roll < 0.61) return Slot.PastDone;
        if (roll < 0.70) return Slot.InProgress;
        return Slot.Future;
    }

    /// <summary>Generates (startUtc, endUtc) for a given slot. Returns null for Unscheduled.</summary>
    public (DateTime start, DateTime end)? Generate(Slot slot)
    {
        var halfWindow = _windowDays / 2;
        var durationMinutes = _faker.PickRandom(new[] { 30, 60, 60, 90, 120, 120, 180, 240, 480 });

        return slot switch
        {
            Slot.PastDone =>
                MakeRange(_reference.AddDays(-_faker.Random.Int(1, halfWindow)), durationMinutes),

            Slot.InProgress =>
                // Range straddles reference date.
                MakeRange(_reference.AddMinutes(-_faker.Random.Int(15, durationMinutes - 5)),
                          durationMinutes),

            Slot.Future =>
                MakeRange(_reference.AddDays(_faker.Random.Int(1, halfWindow)), durationMinutes),

            _ => null,
        };
    }

    public string StatusFor(Slot slot) => slot switch
    {
        Slot.PastDone => "done",
        Slot.InProgress => "in_progress",
        Slot.Future => "new",
        _ => "new", // unscheduled is just new + no times
    };

    private static (DateTime start, DateTime end) MakeRange(DateTime start, int durationMinutes)
    {
        var snapped = SnapTo15Min(start);
        return (snapped, snapped.AddMinutes(durationMinutes));
    }

    private static DateTime SnapTo15Min(DateTime t)
    {
        var minutes = (t.Minute / 15) * 15;
        return new DateTime(t.Year, t.Month, t.Day, t.Hour, minutes, 0, DateTimeKind.Utc);
    }
}
