using Orkyo.Migrations.Abstractions;
using Orkyo.Migrator;

namespace Orkyo.Foundation.Tests.Migrations;

/// <summary>
/// Contract / drift guards for the migration abstractions. These types are referenced
/// across both products + the migration runner, so wire-level changes (enum reordering,
/// record-shape changes, default-value drift) need to fail fast.
/// </summary>
public sealed class MigrationAbstractionsContractTests
{
    [Fact]
    public void MigrationTargetDatabase_HasExpectedMembers()
    {
        var members = Enum.GetNames<MigrationTargetDatabase>();
        members.Should().BeEquivalentTo(new[] { "ControlPlane", "Tenant" });
    }

    [Fact]
    public void MigrationExecutionMode_HasExpectedMembers_AndApplyIsZero()
    {
        var members = Enum.GetNames<MigrationExecutionMode>();
        members.Should().BeEquivalentTo(new[] { "Apply", "DryRun", "ValidateOnly" });
        ((int)MigrationExecutionMode.Apply).Should().Be(0,
            "Apply must be the default-zero value so MigrationOptions defaults to Apply when uninitialized");
    }

    [Fact]
    public void MigrationOutcome_HasExpectedMembers()
    {
        var members = Enum.GetNames<MigrationOutcome>();
        members.Should().BeEquivalentTo(new[] { "Applied", "Skipped", "Failed", "DryRunSucceeded", "Validated" });
    }

    [Fact]
    public void MigrationOptions_DefaultsToApplyMode_60sLockTimeout_NoFilter()
    {
        var options = new MigrationOptions();

        options.Mode.Should().Be(MigrationExecutionMode.Apply);
        options.LockTimeoutSeconds.Should().Be(60);
        options.TargetFilter.Should().BeNull();
        options.AppliedByVersion.Should().BeNull();
    }

    [Fact]
    public void MigrationScript_RecordEquality_ConsidersAllFields()
    {
        var deps = new[] { "V001__init" };
        var a = new MigrationScript("V002__x", "saas-cp", MigrationTargetDatabase.ControlPlane, "SELECT 1;", "abc", deps, []);
        var b = new MigrationScript("V002__x", "saas-cp", MigrationTargetDatabase.ControlPlane, "SELECT 1;", "abc", deps, []);

        a.Should().Be(b, "records must compare by value for the dedup logic in the runner");
    }

    [Fact]
    public void MigrationScript_DiffersWhenChecksumDiffers()
    {
        var deps = Array.Empty<string>();
        var a = new MigrationScript("V002__x", "m", MigrationTargetDatabase.Tenant, "SELECT 1;", "abc", deps, []);
        var b = a with { Checksum = "abd" };

        a.Should().NotBe(b);
    }

    [Fact]
    public void MigrationResult_FailedRecordCarriesErrorMessage()
    {
        var script = new MigrationScript("V001__x", "m", MigrationTargetDatabase.ControlPlane, "SELECT 1;", "x", Array.Empty<string>(), Array.Empty<string>());
        var result = new MigrationResult(script, MigrationOutcome.Failed, ExecutionMs: 12, ErrorMessage: "boom");

        result.Outcome.Should().Be(MigrationOutcome.Failed);
        result.ErrorMessage.Should().Be("boom");
        result.ExecutionMs.Should().Be(12);
    }

    // ── superseded checksums ──────────────────────────────────────────────────
    // Applied migrations are immutable, and an unannounced edit must still fail. The one
    // sanctioned exception is a file that names the exact hash it replaces.

    [Fact]
    public void SupersededChecksums_DefaultToEmpty_SoOrdinaryScriptsStayImmutable()
    {
        var script = new MigrationScript(
            "V001__x", "m", MigrationTargetDatabase.Tenant, "SELECT 1;", "abc",
            Array.Empty<string>(), Array.Empty<string>());

        script.SupersededChecksums.Should().BeEmpty();
    }

    [Theory]
    [InlineData("-- @supersedes-checksum: a1b2c3", "a1b2c3")]
    [InlineData("--@supersedes-checksum:A1B2C3", "A1B2C3")]
    [InlineData("   -- @supersedes-checksum:   deadbeef   ", "deadbeef")]
    public void SupersedesDirective_IsParsedFromTheFile(string line, string expected)
    {
        var found = EmbeddedSqlLoader.ParseSupersededChecksums($"{line}\nSELECT 1;");

        found.Should().ContainSingle().Which.Should().Be(expected);
    }

    [Theory]
    [InlineData("SELECT 1; -- @supersedes-checksum: abc")]   // not a whole-line comment
    [InlineData("-- supersedes-checksum: abc")]              // missing the @
    [InlineData("-- @supersedes-checksum: not-hex-zz")]
    public void NonDirectiveLines_AreIgnored(string line)
    {
        var found = EmbeddedSqlLoader.ParseSupersededChecksums($"{line}\nSELECT 1;");

        found.Should().BeEmpty();
    }
}
