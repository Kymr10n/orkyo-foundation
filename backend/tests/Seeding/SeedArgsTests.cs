using Orkyo.Foundation.Seed;

namespace Orkyo.Foundation.Tests.Seeding;

/// <summary>
/// Tests for the seed CLIs' argument parser, which replaced CommandLineParser.
///
/// The seeders run unattended in the production demo-reset workflow, so the behaviours that
/// matter most are the refusals: a mistyped flag must stop the run rather than silently seed
/// the wrong shape, and a default-on flag must still be switchable off.
/// </summary>
public class SeedArgsTests
{
    private static readonly string[] Known =
        [.. SeedCliOptions.SharedOptionNames, "tenant", "control-plane-connection"];

    private static SeedArgs Parse(params string[] args)
    {
        var parsed = SeedArgs.Parse(args, Known, out var error);
        parsed.Should().NotBeNull(because: error);
        return parsed!;
    }

    [Fact]
    public void ReadsValuesGivenAsSeparateTokens()
    {
        var args = Parse("--profile", "manufacturing", "--scale", "large", "--seed", "99");

        args.String("profile").Should().Be("manufacturing");
        args.String("scale", "medium").Should().Be("large");
        args.Int("seed", 1337).Should().Be(99);
    }

    [Fact]
    public void ReadsValuesGivenWithEquals()
    {
        var args = Parse("--profile=manufacturing", "--seed=7");

        args.String("profile").Should().Be("manufacturing");
        args.Int("seed", 1337).Should().Be(7);
    }

    [Fact]
    public void FallsBackToDefaultsForAbsentOptions()
    {
        var args = Parse("--profile", "generic");

        args.String("scale", "medium").Should().Be("medium");
        args.Int("seed", 1337).Should().Be(1337);
        args.Bool("random", false).Should().BeFalse();
        // --floorplans defaults ON, which is the case a naive parser gets wrong.
        args.Bool("floorplans", true).Should().BeTrue();
    }

    [Fact]
    public void TreatsABareFlagAsTrue()
    {
        var args = Parse("--profile", "generic", "--random", "--force-non-local");

        args.Bool("random", false).Should().BeTrue();
        args.Bool("force-non-local", false).Should().BeTrue();
    }

    [Fact]
    public void AllowsADefaultOnFlagToBeSwitchedOff()
    {
        // Documented usage: `--floorplans false`.
        Parse("--profile", "generic", "--floorplans", "false").Bool("floorplans", true).Should().BeFalse();
        Parse("--profile", "generic", "--floorplans=false").Bool("floorplans", true).Should().BeFalse();
    }

    [Fact]
    public void ABareFlagFollowedByAnotherOptionStaysTrue()
    {
        // The next token starts with --, so it is the next option and not this flag's value.
        var args = Parse("--random", "--profile", "generic");

        args.Bool("random", false).Should().BeTrue();
        args.String("profile").Should().Be("generic");
    }

    [Fact]
    public void RefusesAnUnknownOption()
    {
        var parsed = SeedArgs.Parse(["--profile", "generic", "--floorplan", "true"], Known, out var error);

        // A near-miss typo is exactly what must not be ignored: seeding the wrong shape into a
        // demo tenant looks like a product bug later.
        parsed.Should().BeNull();
        error.Should().Contain("--floorplan");
    }

    [Fact]
    public void RefusesAPositionalArgument()
    {
        var parsed = SeedArgs.Parse(["manufacturing"], Known, out var error);

        parsed.Should().BeNull();
        error.Should().Contain("manufacturing");
    }

    [Fact]
    public void RefusesAnEmptyOptionName()
    {
        SeedArgs.Parse(["--"], Known, out var error).Should().BeNull();
        error.Should().NotBeNull();
    }

    [Fact]
    public void ParsesAnEmptyCommandLine()
    {
        var args = Parse();

        args.Has("profile").Should().BeFalse();
        args.String("profile").Should().BeNull();
    }

    [Fact]
    public void OptionNamesAreCaseSensitive()
    {
        // Matches the previous parser's default. Accepting "--Profile" would be a behaviour
        // change, and a silently-accepted variant spelling is how a flag ends up unread.
        SeedArgs.Parse(["--Profile", "generic"], Known, out var error).Should().BeNull();
        error.Should().Contain("--Profile");
    }

    [Fact]
    public void BindSharedAppliesEveryDefaultAndOverride()
    {
        var options = new SeedCliOptions();
        options.BindShared(Parse("--profile", "manufacturing", "--mode", "append", "--floorplans", "false"));

        options.Profile.Should().Be("manufacturing");
        options.Mode.Should().Be("append");
        options.Floorplans.Should().BeFalse();
        options.Scale.Should().Be("medium");
        options.RandomSeed.Should().Be(1337);
        options.UseRandom.Should().BeFalse();
        options.ForceNonLocal.Should().BeFalse();
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    [InlineData("-?")]
    public void HelpIsRecognisedRatherThanRejectedAsAnUnknownOption(string flag)
    {
        // It is checked before parsing: help is not an option among the others, and answering a
        // fair question with "Unknown option '--help'" is a rude way to greet someone.
        SeedCliSupport.IsHelpRequested([flag]).Should().BeTrue();
        SeedCliSupport.IsHelpRequested(["--profile", "generic", flag]).Should().BeTrue();
    }

    [Fact]
    public void AnOrdinaryCommandLineIsNotAHelpRequest() =>
        SeedCliSupport.IsHelpRequested(["--profile", "generic"]).Should().BeFalse();

    [Fact]
    public void ValidateProfileAndScaleRejectsAMissingProfile()
    {
        // CommandLineParser enforced Required=true; that check now lives here.
        var options = new SeedCliOptions();
        options.BindShared(Parse("--scale", "large"));

        // 1 = usage error, matching what the previous parser returned for a missing required
        // option. 2 is reserved for a named-but-unknown profile or scale.
        SeedCliSupport.ValidateProfileAndScale(options).Should().Be(1);
    }

    [Fact]
    public void ValidateProfileAndScaleAcceptsAValidPair()
    {
        var options = new SeedCliOptions();
        options.BindShared(Parse("--profile", "manufacturing", "--scale", "large"));

        SeedCliSupport.ValidateProfileAndScale(options).Should().BeNull();
    }
}
