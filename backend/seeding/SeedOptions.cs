namespace Orkyo.Foundation.Seed;

public enum SeedMode { Reset, Append }

public sealed record SeedOptions
{
    /// <summary>Profile slug — must match an entry in <see cref="ProfileCatalog"/>.</summary>
    public required string Profile { get; init; }

    /// <summary>Scale slug — must match an entry in <see cref="ScaleCatalog"/>.</summary>
    public required string Scale { get; init; }

    /// <summary>Reset truncates tenant tables first; Append leaves existing rows alone.</summary>
    public SeedMode Mode { get; init; } = SeedMode.Reset;

    /// <summary>Fixed Bogus seed for deterministic runs. Default 1337.</summary>
    public int RandomSeed { get; init; } = 1337;

    /// <summary>If true, ignores RandomSeed and uses a fresh random per run.</summary>
    public bool UseRandom { get; init; }

    /// <summary>Anchor for relative time generation. Defaults to UTC now at runtime.</summary>
    public DateTime ReferenceDate { get; init; } = DateTime.UtcNow;

    /// <summary>If true, the SafetyGuard refusal is bypassed. Off by default.</summary>
    public bool ForceNonLocal { get; init; }

    /// <summary>If true, the conflict injector is skipped.</summary>
    public bool NoConflicts { get; init; }
}
