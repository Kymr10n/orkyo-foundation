using Orkyo.Foundation.Seed.Profiles;
using Orkyo.Foundation.Seed.Scales;

namespace Orkyo.Foundation.Seed;

/// <summary>
/// The CLI options shared by every edition's seed tool. Each product's own options
/// class inherits this and adds only its edition-specific connection/tenant flags,
/// binding them alongside <see cref="BindShared"/>.
/// </summary>
public class SeedCliOptions
{
    /// <summary>Long-option names this base class binds. An edition appends its own.</summary>
    public static readonly string[] SharedOptionNames =
        ["profile", "scale", "mode", "seed", "random", "force-non-local", "floorplans"];

    /// <summary>Required. One of: generic, manufacturing, construction, camping, education.</summary>
    public string Profile { get; set; } = "";

    /// <summary>One of: tiny, small, medium, large, xlarge.</summary>
    public string Scale { get; set; } = "medium";

    /// <summary>reset (truncate tables before seeding) or append.</summary>
    public string Mode { get; set; } = "reset";

    /// <summary>Random seed for deterministic generation.</summary>
    public int RandomSeed { get; set; } = 1337;

    /// <summary>Use a fresh random seed instead of the fixed --seed value.</summary>
    public bool UseRandom { get; set; }

    /// <summary>Override the safety guard that refuses non-local connections.</summary>
    public bool ForceNonLocal { get; set; }

    /// <summary>
    /// Seed the curated floorplan-backed sites (image assets + geometry-bearing spaces)
    /// instead of scale-driven sites/spaces. On by default; pass --floorplans false to disable.
    /// </summary>
    public bool Floorplans { get; set; } = true;

    /// <summary>Help for the shared flags. An edition prints this plus its own.</summary>
    public const string SharedHelpText = """
          --profile          Required. One of: generic, manufacturing, construction, camping, education.
          --scale            One of: tiny, small, medium, large, xlarge. (Default: medium)
          --mode             reset (truncate tables before seeding) or append. (Default: reset)
          --seed             Random seed for deterministic generation. (Default: 1337)
          --random           Use a fresh random seed instead of the fixed --seed value.
          --force-non-local  Override the safety guard that refuses non-local connections.
          --floorplans       Seed the curated floorplan-backed sites. (Default: true; --floorplans false to disable)
        """;

    /// <summary>Bind the shared flags onto an edition's options instance.</summary>
    public void BindShared(SeedArgs args)
    {
        Profile = args.String("profile") ?? "";
        Scale = args.String("scale", "medium")!;
        Mode = args.String("mode", "reset")!;
        RandomSeed = args.Int("seed", 1337);
        UseRandom = args.Bool("random", false);
        ForceNonLocal = args.Bool("force-non-local", false);
        Floorplans = args.Bool("floorplans", true);
    }
}

/// <summary>
/// Shared seed-CLI plumbing: option validation, <see cref="SeedOptions"/> assembly, and
/// report printing — identical across editions. Each product's <c>Program</c> keeps only
/// its connection + tenant resolution and delegates the rest here.
/// </summary>
public static class SeedCliSupport
{
    /// <summary>
    /// Whether the caller asked for usage. Checked before parsing: help is not an option among
    /// the others, and reporting it as an unknown one would be a rude answer to a fair question.
    /// </summary>
    public static bool IsHelpRequested(string[] args)
        => args.Any(a => a is "--help" or "-h" or "-?");

    /// <summary>Validate profile/scale early. Returns a non-zero exit code (prints to stderr) on failure, else null.</summary>
    public static int? ValidateProfileAndScale(SeedCliOptions opts)
    {
        // Exit 1, not 2: a missing required option is a usage error, which is what the
        // previous parser returned for it. Exit 2 stays reserved for a well-formed command
        // line naming a profile or scale that does not exist.
        if (string.IsNullOrWhiteSpace(opts.Profile))
        {
            Console.Error.WriteLine("Required option '--profile' is missing.");
            return 1;
        }
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
        if (report.Tools + report.Capabilities + report.AvailabilityEvents > 0)
        {
            Console.WriteLine($"  Tools:              {report.Tools,8}");
            Console.WriteLine($"  Machine types:      {report.MachineTypes,8}");
            Console.WriteLine($"  Machines:           {report.Machines,8}");
            Console.WriteLine($"  Machine cells:      {report.MachineGroups,8}");
            Console.WriteLine($"  List rows:          {report.ListRows,8}");
            Console.WriteLine($"  Custom fields:      {report.CustomFields,8}");
            Console.WriteLine($"  Capabilities:       {report.Capabilities,8}");
            Console.WriteLine($"  Requirements:       {report.Requirements,8}");
            Console.WriteLine($"  Availability events:{report.AvailabilityEvents,8}");
            Console.WriteLine($"  Absences:           {report.Absences,8}");
            Console.WriteLine($"  Conflicts (seeded): {report.Conflicts,8}");
            Console.WriteLine($"  Dependencies:       {report.Dependencies,8}");
        }
    }
}
