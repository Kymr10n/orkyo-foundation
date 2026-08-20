using Api.Constants;
using Orkyo.Foundation.Seed.Narrative;
using Xunit;

namespace Orkyo.Foundation.Tests.Seeding;

/// <summary>
/// The seeder's machine types against the product's own type catalog.
///
/// The seeding project carries no reference to the API core on purpose, so its copy of the field
/// definitions is a copy — and copies drift. This one did: the seed wrote `spindle_max_rpm` where
/// the catalog wrote `spindle_speed_max`, so a mill in a seeded demo and a mill somebody activated
/// from the catalog disagreed on a field key. Nothing failed; the two simply meant different
/// things. This test is what the missing project reference does instead.
/// </summary>
public class ResourceTypeCatalogParityTests
{
    /// <summary>
    /// Fields the seeder adds beyond the catalog. Both bind to a list, and the catalog cannot ship
    /// a field pointing at a list definition — the definition does not exist until a workspace
    /// makes one.
    /// </summary>
    private static readonly HashSet<string> SeederOnlyFields = ["tooling", "maintenance_log"];

    [Fact]
    public void EverySeededMachineType_IsACatalogEntry()
    {
        foreach (var type in MachineCatalog.Types)
        {
            var entry = ResourceTypeCatalog.Types.SingleOrDefault(e => e.Key == type.Key);
            Assert.True(entry is not null, $"'{type.Key}' is seeded but is not in the type catalog");

            Assert.Equal(entry!.DisplayName, type.DisplayName);
            Assert.Equal(entry.DisplayNamePlural, type.DisplayNamePlural);
            Assert.Equal(entry.Description, type.Description);
            Assert.Equal(entry.Icon, type.Icon);
            // Every machine stands somewhere and belongs to one cell.
            Assert.True(entry.HasGeometry, $"'{type.Key}' must be placeable");
            Assert.True(entry.SingleGroupMembership, $"'{type.Key}' must belong to one group");
        }
    }

    [Fact]
    public void EveryCatalogField_IsSeededWithTheSameKeyAndType()
    {
        foreach (var type in MachineCatalog.Types)
        {
            var entry = ResourceTypeCatalog.Types.Single(e => e.Key == type.Key);
            foreach (var field in entry.Fields)
            {
                var seeded = type.Fields.SingleOrDefault(f => f.Key == field.Key);
                Assert.True(seeded is not null,
                    $"'{type.Key}' is missing catalog field '{field.Key}' — a seeded resource and an "
                    + "activated one would disagree on where that value lives");
                Assert.Equal(field.DataType, seeded!.DataType);
            }
        }
    }

    [Fact]
    public void TheSeederAddsNothingBeyondTheTwoListFields()
    {
        foreach (var type in MachineCatalog.Types)
        {
            var catalogKeys = ResourceTypeCatalog.Types.Single(e => e.Key == type.Key)
                .Fields.Select(f => f.Key).ToHashSet();

            var extras = type.Fields.Select(f => f.Key).Where(k => !catalogKeys.Contains(k)).ToList();
            Assert.True(extras.Count == 0,
                $"'{type.Key}' seeds fields the catalog does not ship: {string.Join(", ", extras)}");
        }

        // The two live on the type specs' behalf but are added by the list factory, not here, so
        // this pins the contract rather than the mechanism.
        Assert.Equal(["maintenance_log", "tooling"], SeederOnlyFields.Order());
    }
}
