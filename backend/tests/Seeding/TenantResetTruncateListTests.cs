using System.Reflection;
using Orkyo.Foundation.Seed;

namespace Orkyo.Foundation.Tests.Seeding;

/// <summary>
/// Guards the TenantReset.TablesToTruncate list against accidental omissions.
///
/// Why: the nightly demo reset (and any tenant wipe) silently skips tables not
/// in this list. Missing a table leaks demo data across resets and can mask bugs
/// that rely on a clean state. Tests here catch regressions before deployment.
/// </summary>
public class TenantResetTruncateListTests
{
    private static readonly string[] TablesToTruncate = GetTablesToTruncate();

    private static string[] GetTablesToTruncate()
    {
        var field = typeof(TenantReset).GetField(
            "TablesToTruncate",
            BindingFlags.Static | BindingFlags.NonPublic);

        field.Should().NotBeNull(
            "TenantReset.TablesToTruncate must exist as a private static field");

        return (string[])field!.GetValue(null)!;
    }

    [Fact]
    public void TablesToTruncate_IsNotEmpty()
    {
        TablesToTruncate.Should().NotBeEmpty();
    }

    // ── Tables that must always be present ───────────────────────────────────

    [Theory]
    [InlineData("assets")]          // uploaded floorplan images — demo reset fix
    [InlineData("resources")]       // spaces + person_profiles folded in here (migration 1700)
    [InlineData("requests")]
    [InlineData("resource_assignments")]
    [InlineData("criteria")]
    [InlineData("templates")]
    [InlineData("resource_capabilities")]   // skill/spec capabilities — narrative demo
    [InlineData("availability_events")]     // holidays / shutdowns — narrative demo
    [InlineData("resource_absences")]       // vacation / sickness / training — narrative demo
    // Tenant-defined shape seeded alongside the machines. None of these is a child of a table
    // already listed — a shared list instance has no resource_id — so leaving one out means the
    // next `--mode reset` run dies on a unique constraint rather than starting clean.
    [InlineData("resource_custom_fields")]
    [InlineData("list_definitions")]
    [InlineData("list_columns")]
    [InlineData("list_instances")]
    [InlineData("list_rows")]
    public void TablesToTruncate_ContainsExpectedTable(string tableName)
    {
        TablesToTruncate.Should().Contain(tableName,
            $"'{tableName}' must be in TablesToTruncate so tenant resets wipe it");
    }

    [Fact]
    public void TablesToTruncate_ContainsNoNullOrWhitespaceEntries()
    {
        TablesToTruncate.Should().AllSatisfy(t =>
            t.Should().NotBeNullOrWhiteSpace("table names must be non-empty strings"));
    }

    [Fact]
    public void TablesToTruncate_ContainsNoDuplicates()
    {
        TablesToTruncate.Should().OnlyHaveUniqueItems("duplicate table names waste a TRUNCATE round-trip");
    }
}
