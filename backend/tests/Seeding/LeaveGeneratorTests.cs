using AwesomeAssertions;
using Bogus;
using Orkyo.Foundation.Seed.Factories;
using Xunit;

namespace Orkyo.Foundation.Tests.Seeding;

/// <summary>
/// The demo's leave generator, tested without a database so it can run over hundreds of people.
/// The seeding integration test cannot defend these: its fixture holds too few people for a
/// defect that shows up in a few of every three hundred to appear at all.
/// </summary>
public class LeaveGeneratorTests
{
    private static readonly DateTime CalStart = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    // Eighteen months, so the generator has to grant a second, partial leave year — the case
    // where a block once drifted across the year boundary into the next year's first block.
    private static readonly DateTime CalEnd = CalStart.AddDays(547);

    private static Faker Faker(int seed) => new() { Random = new Randomizer(seed) };

    [Fact]
    public void APersonsAbsencesNeverOverlapEachOther()
    {
        for (var person = 0; person < 300; person++)
        {
            var leave = AvailabilityFactory.BuildLeave(Faker(person), CalStart, CalEnd)
                .OrderBy(l => l.Start)
                .ToList();

            for (var i = 1; i < leave.Count; i++)
                leave[i].Start.Should().BeOnOrAfter(leave[i - 1].End,
                    $"absence {i} of person {person} starts before the previous one ends");
        }
    }

    [Fact]
    public void EveryLeaveWindowStaysInsideTheCalendar()
    {
        for (var person = 0; person < 100; person++)
            foreach (var leave in AvailabilityFactory.BuildLeave(Faker(person), CalStart, CalEnd))
            {
                leave.Start.Should().BeOnOrAfter(CalStart);
                leave.End.Should().BeOnOrBefore(CalEnd);
                leave.End.Should().BeAfter(leave.Start);
            }
    }

    [Fact]
    public void AFullTimeYearCarriesRoughlyTwentyFiveWorkingDaysOfVacation()
    {
        // The entitlement the demo claims to model. Averaged over many people: an individual
        // year varies, and a trailing part-year earns only its share.
        var (vacation, sickness, training) = AverageWorkingDaysPerYear(people: 300);

        vacation.Should().BeInRange(20, 27, "a full-time employee takes about 25 days off");
        // Not a token absence each: a shop floor is ill and does train.
        sickness.Should().BeInRange(4, 12);
        training.Should().BeInRange(1.5, 5);
    }

    private static (double Vacation, double Sickness, double Training) AverageWorkingDaysPerYear(int people)
    {
        var totals = new Dictionary<string, int> { ["vacation"] = 0, ["sickness"] = 0, ["training"] = 0 };

        for (var person = 0; person < people; person++)
            foreach (var leave in AvailabilityFactory.BuildLeave(Faker(person), CalStart, CalEnd))
                for (var day = leave.Start; day < leave.End; day = day.AddDays(1))
                    if (day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
                        totals[leave.Type]++;

        var personYears = people * (CalEnd - CalStart).TotalDays / 365.0;
        return (totals["vacation"] / personYears, totals["sickness"] / personYears, totals["training"] / personYears);
    }
}
