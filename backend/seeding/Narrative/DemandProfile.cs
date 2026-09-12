namespace Orkyo.Foundation.Seed.Narrative;

/// <summary>
/// How busy the shop should be, as a function of how far a day sits from the reference date.
///
/// The seeder used to generate a fixed job COUNT (<c>scale.Requests</c>) and spread routine work
/// uniformly across the whole window. At medium that was ~18k lead-hours against ~337k available,
/// so every utilization chart read 5 % — a shop nobody would run. Demand is expressed as a target
/// occupancy instead, and the loop fills each day up to it.
///
/// The shape is a planner's horizon rather than a flat line, because that is what a real book of
/// work looks like and it is what makes the chart worth reading:
///   • history is done and dense — it happened, so it is full;
///   • the next quarter is committed — firm orders, nearly as full;
///   • beyond that the book thins out to roughly a third, because nobody has sold that month yet.
/// A campaign quarter modulates its own site ±10 % on top, so each facility has a visible peak.
/// </summary>
public static class DemandProfile
{
    /// <summary>Occupancy of people in the completed past.</summary>
    public const double PeopleHistory = 0.88;

    /// <summary>Occupancy of people across the committed near term (0–3 months out).</summary>
    public const double PeopleNear = 0.85;

    /// <summary>Occupancy of people at the far edge of the window (+12 months).</summary>
    public const double PeopleFar = 0.35;

    /// <summary>Months from the near-term plateau to the far edge.</summary>
    public const double TaperMonths = 9.0;

    /// <summary>Months of committed work before the taper starts.</summary>
    public const double NearMonths = 3.0;

    /// <summary>
    /// Stations sit below people on purpose: a machine idle between set-ups is normal, and a shop
    /// whose every station is pinned at capacity has no slack for the scheduling story to show.
    /// Expressed as a ratio so the two curves keep their shape when the people target moves.
    /// </summary>
    public const double StationRatio = 0.75 / PeopleHistory;

    /// <summary>A site's campaign quarter runs hotter; the rest of its year runs slightly cooler.</summary>
    public const double CampaignBoost = 1.10;
    public const double OffCampaign = 0.97;

    /// <summary>Never target saturation — a shop with zero slack cannot absorb the demo's own
    /// injected conflicts, and the last few per cent cost disproportionate seeding effort.</summary>
    public const double Cap = 0.95;

    /// <summary>Average days per month, for converting a day offset into a smooth month offset.</summary>
    public const double DaysPerMonth = 30.4375;

    /// <summary>
    /// What one resource reaches on a day it is working: a single 6–8 hour job in an 8-hour day.
    /// Work comes in whole jobs, so a resource's day is essentially all-or-nothing — which means a
    /// target below this cannot be met by giving someone a shorter day. It is met by fewer
    /// resources working that day, which is also what a thinning order book actually looks like.
    /// </summary>
    public const double FullDayFill = 0.875;

    /// <summary>The chance a given resource has work at all on a day with this target.</summary>
    public static double WorkProbability(double target) => Math.Clamp(target / FullDayFill, 0, 1);

    /// <summary>Months from the reference date; negative in the past.</summary>
    public static double MonthsFrom(DateTime referenceDate, DateTime day) =>
        (day - referenceDate).TotalDays / DaysPerMonth;

    /// <summary>Target occupancy for people at <paramref name="months"/> from the reference date.</summary>
    public static double People(double months)
    {
        if (months < 0) return PeopleHistory;
        if (months < NearMonths) return PeopleNear;
        var t = Math.Clamp((months - NearMonths) / TaperMonths, 0, 1);
        return PeopleNear + (PeopleFar - PeopleNear) * t;
    }

    /// <summary>Target occupancy for a machine pool at <paramref name="months"/>.</summary>
    public static double Stations(double months) => People(months) * StationRatio;

    /// <summary>Applies the site's campaign modulation and the saturation cap.</summary>
    public static double WithCampaign(double target, bool inCampaignWindow) =>
        Math.Min(Cap, target * (inCampaignWindow ? CampaignBoost : OffCampaign));
}
