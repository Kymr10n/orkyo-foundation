using CommandLine;
using Orkyo.Foundation.Seed.Profiles;
using Orkyo.Foundation.Seed.Scales;

namespace Orkyo.Foundation.Seed;

/// <summary>
/// The CLI options shared by every edition's seed tool. Each product's own options
/// class inherits this and adds only its edition-specific connection/tenant flags
/// (CommandLineParser reads inherited [Option] properties).
/// </summary>
public class SeedCliOptions
{
    [Option("profile", Required = true,
        HelpText = "Required. One of: generic, manufacturing, construction, camping, education.")]
    public string Profile { get; init; } = "";

    [Option("scale", Default = "medium",
        HelpText = "One of: tiny, small, medium, large, xlarge.")]
    public string Scale { get; init; } = "medium";

    [Option("mode", Default = "reset",
        HelpText = "reset (truncate tables before seeding) or append.")]
    public string Mode { get; init; } = "reset";

    [Option("seed", Default = 1337,
        HelpText = "Random seed for deterministic generation.")]
    public int RandomSeed { get; init; } = 1337;

    [Option("random", Default = false,
        HelpText = "Use a fresh random seed instead of the fixed --seed value.")]
    public bool UseRandom { get; init; }

    [Option("force-non-local", Default = false,
        HelpText = "Override the safety guard that refuses non-local connections.")]
    public bool ForceNonLocal { get; init; }

    [Option("floorplans", Default = true,
        HelpText = "Seed the curated floorplan-backed sites (with image assets + geometry-bearing spaces) instead of scale-driven sites/spaces. Requires a profile with a floorplan set (manufacturing). Pass --floorplans false to disable.")]
    public bool Floorplans { get; init; }

    [Option("tools", Default = false,
        HelpText = "Also seed tool/equipment resources and their criteria. Off by default — the demo seeds people and spaces only.")]
    public bool Tools { get; init; }
}

/// <summary>
/// Shared seed-CLI plumbing: option validation, <see cref="SeedOptions"/> assembly, and
/// report printing — identical across editions. Each product's <c>Program</c> keeps only
/// its connection + tenant resolution and delegates the rest here.
/// </summary>
public static class SeedCliSupport
{
    /// <summary>Validate profile/scale early. Returns a non-zero exit code (prints to stderr) on failure, else null.</summary>
    public static int? ValidateProfileAndScale(SeedCliOptions opts)
    {
        try { _ = ProfileCatalog.Resolve(opts.Profile); _ = ScaleCatalog.Resolve(opts.Scale); }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 2;
        }
        return null;
    }

    /// <summary>Assemble <see cref="SeedOptions"/> from the shared CLI flags plus the edition-resolved tenant id.</summary>
    public static SeedOptions BuildSeedOptions(SeedCliOptions opts, Guid tenantId) => new()
    {
        Profile = opts.Profile,
        Scale = opts.Scale,
        Mode = opts.Mode.Equals("append", StringComparison.OrdinalIgnoreCase) ? SeedMode.Append : SeedMode.Reset,
        RandomSeed = opts.RandomSeed,
        UseRandom = opts.UseRandom,
        ForceNonLocal = opts.ForceNonLocal,
        UseFloorplans = opts.Floorplans,
        TenantId = tenantId,
        ResourceTypes = opts.Tools ? SeedResourceTypes.All : SeedResourceTypes.Default,
    };

    /// <summary>Print the full seed report (every counter) — the single source so no edition silently omits rows.</summary>
    public static void PrintReport(SeedReport report)
    {
        Console.WriteLine();
        Console.WriteLine($"Seeded in {report.Duration.TotalSeconds:F1}s:");
        Console.WriteLine($"  Sites:              {report.Sites,8}");
        Console.WriteLine($"  Spaces:             {report.Spaces,8}");
        Console.WriteLine($"  Floorplan assets:   {report.FloorplanAssets,8}");
        Console.WriteLine($"  Space groups:       {report.SpaceGroups,8}");
        Console.WriteLine($"  Space members:      {report.SpaceGroupMembers,8}");
        Console.WriteLine($"  Job titles:         {report.JobTitles,8}");
        Console.WriteLine($"  Departments:        {report.Departments,8}");
        Console.WriteLine($"  People:             {report.People,8}");
        Console.WriteLine($"  Person groups:      {report.PersonGroups,8}");
        Console.WriteLine($"  Group members:      {report.PersonGroupMembers,8}");
        Console.WriteLine($"  Criteria:           {report.Criteria,8}");
        Console.WriteLine($"  Requests:           {report.Requests,8}");
        Console.WriteLine($"  Assignments:        {report.Assignments,8}");
        if (report.Tools + report.Capabilities + report.AvailabilityEvents + report.Templates > 0)
        {
            Console.WriteLine($"  Tools:              {report.Tools,8}");
            Console.WriteLine($"  Capabilities:       {report.Capabilities,8}");
            Console.WriteLine($"  Requirements:       {report.Requirements,8}");
            Console.WriteLine($"  Availability events:{report.AvailabilityEvents,8}");
            Console.WriteLine($"  Absences:           {report.Absences,8}");
            Console.WriteLine($"  Templates:          {report.Templates,8}");
            Console.WriteLine($"  Conflicts (seeded): {report.Conflicts,8}");
        }
    }
}
