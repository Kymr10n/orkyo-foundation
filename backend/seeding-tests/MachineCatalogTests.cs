using Orkyo.Foundation.Seed.Factories;
using Orkyo.Foundation.Seed.Floorplans;
using Orkyo.Foundation.Seed.Narrative;
using Xunit;

namespace Orkyo.Foundation.Seed.Tests;

/// <summary>
/// The machine catalog is authored by hand against room rectangles authored by hand in another
/// file. Nothing at runtime checks that a machine landed inside its room — it would simply be drawn
/// somewhere odd — so these are the tests that keep the two in step.
/// </summary>
public class MachineCatalogTests
{
    private static FloorplanSite SiteFor(string code) =>
        FloorplanCatalog.ForProfile("manufacturing").First(s => s.Code == code);

    private static FloorplanRoom RoomFor(MachineSpec machine) =>
        SiteFor(machine.SiteCode).Rooms.First(r => r.Code == machine.RoomCode);

    /// <summary>The machines that were drawn. An unplaced one has no room to be inside of.</summary>
    private static IEnumerable<MachineSpec> Placed =>
        MachineCatalog.All.Where(m => m.Geometry is not null);

    [Fact]
    public void EveryMachine_ReferencesARoomThatExists()
    {
        foreach (var machine in Placed)
        {
            var site = FloorplanCatalog.ForProfile("manufacturing")
                .FirstOrDefault(s => s.Code == machine.SiteCode);
            Assert.True(site is not null, $"Machine {machine.Code} names site {machine.SiteCode}, which has no floorplan.");
            Assert.True(site!.Rooms.Any(r => r.Code == machine.RoomCode),
                $"Machine {machine.Code} names room {machine.RoomCode}, which {machine.SiteCode} does not have.");
        }
    }

    [Fact]
    public void EveryMachine_FitsInsideItsRoom()
    {
        // A machine drawn outside its room is not an error anywhere in the stack — it just looks
        // wrong on the plan, which is the kind of thing nobody notices until a demo.
        foreach (var machine in Placed)
        {
            var room = RoomFor(machine);
            var (minX, minY, maxX, maxY) = Extent(machine.Geometry!);

            Assert.True(minX >= room.X, $"{machine.Code} starts left of room {room.Code}.");
            Assert.True(minY >= room.Y, $"{machine.Code} starts above room {room.Code}.");
            Assert.True(maxX <= room.X + room.W, $"{machine.Code} extends right of room {room.Code}.");
            Assert.True(maxY <= room.Y + room.H, $"{machine.Code} extends below room {room.Code}.");
        }
    }

    [Fact]
    public void MachineCodes_AreUnique()
    {
        var codes = MachineCatalog.All.Select(m => m.Code).ToList();
        Assert.Equal(codes.Count, codes.Distinct().Count());
    }

    [Fact]
    public void MachineTypeKeys_MatchTheKeyFormatTheApiEnforces()
    {
        foreach (var type in MachineCatalog.Types)
            Assert.Matches("^[a-z][a-z0-9_]{0,49}$", type.Key);
    }

    [Fact]
    public void EveryMachine_NamesADeclaredType()
    {
        var keys = MachineCatalog.Types.Select(t => t.Key).ToHashSet();
        Assert.All(MachineCatalog.All, m => Assert.Contains(m.TypeKey, keys));
    }

    [Fact]
    public void EveryCircle_HasAPositiveRadius()
    {
        // Stored as centre plus a rim point, so a zero radius would collapse the two and make the
        // shape unrecoverable from its own coordinates.
        foreach (var circle in MachineCatalog.All.Select(m => m.Geometry).OfType<MachineGeometry.Circle>())
            Assert.True(circle.Radius > 0, "A circle stored as centre plus rim needs a positive radius.");
    }

    [Fact]
    public void EveryMachineRole_AnArchetypeAsksFor_HasAMachineAtThatFacility()
    {
        // The narrative's PickMachine returns null when a facility owns none of the role, and the
        // job would quietly go unassigned rather than failing.
        foreach (var facility in FacilityModel.All)
        {
            var available = MachineCatalog.ForSite(facility.SiteCode).Select(m => m.Role).ToHashSet();
            foreach (var role in FacilityModel.RequiredMachineRoles(facility))
                Assert.True(available.Contains(role),
                    $"{facility.SiteCode} has work needing a '{role}' machine but owns none.");
        }
    }

    [Fact]
    public void ConvertedTools_AreGoneFromTheToolCatalogue()
    {
        // Mills, lathes and assembly stations became placed machines. Leaving the tools behind
        // would show the demo the same equipment twice, once schedulable from a list and once
        // from the plan.
        var toolNames = FacilityModel.All.SelectMany(f => f.Tools).Select(t => t.Name).ToList();

        Assert.DoesNotContain("CNC Mill", toolNames);
        Assert.DoesNotContain("Assembly Station", toolNames);
        Assert.DoesNotContain("CNC Lathe", toolNames);
        Assert.DoesNotContain("Line Station", toolNames);

        // What is left is genuinely fetched rather than stood at.
        Assert.Contains("Band Saw", toolNames);
        Assert.Contains("Overhead Crane", toolNames);
    }

    [Fact]
    public void EverySiteRunsRealStations()
    {
        // Two of the three sites used to have almost nothing on the floor — one machine at FWF,
        // none at PPF — so the whole stations story lived at one site. Each site now runs the
        // machines its own work needs.
        var byType = MachineCatalog.All
            .GroupBy(m => m.SiteCode)
            .ToDictionary(g => g.Key, g => g.Select(m => m.TypeKey).ToHashSet());

        Assert.Superset(new HashSet<string> { "mill", "drill", "lathe", "cnc" }, byType["PMF"]);
        Assert.Superset(new HashSet<string> { "drill" }, byType["FWF"]);
        Assert.Superset(new HashSet<string> { "assembly_station", "test_station" }, byType["PPF"]);
    }

    [Fact]
    public void EverySiteOwnsMachinesNobodyHasDrawnYet()
    {
        // The floorplan's place-an-existing-resource flow has nothing to offer on a plan where
        // every machine is already drawn, so each site keeps a few waiting.
        foreach (var site in new[] { "PMF", "FWF", "PPF" })
        {
            var unplaced = MachineCatalog.ForSite(site).Count(m => m.Geometry is null);
            Assert.InRange(unplaced, 2, 3);
        }
    }

    [Fact]
    public void AnUnplacedMachine_HasNeitherRoomNorShape()
    {
        // Half-placed is the one state that would break the plan: a room with no shape draws
        // nothing, and a shape with no room sits outside every wall.
        foreach (var machine in MachineCatalog.All)
            Assert.Equal(machine.RoomCode is null, machine.Geometry is null);
    }

    [Fact]
    public void EveryMachineRole_IsWorkSomeFacilityActuallySchedules()
    {
        // A machine nobody books is a row in a list. FWF used to own exactly one drill with a
        // comment explaining that it was never scheduled; now its work asks for it.
        var scheduledRoles = FacilityModel.All
            .SelectMany(f => f.Archetypes)
            .Select(a => a.MachineRole)
            .Where(r => r is not null)
            .ToHashSet();

        foreach (var role in MachineCatalog.All.Select(m => m.Role).Distinct())
            Assert.Contains(role, scheduledRoles);
    }

    [Fact]
    public void GeometryJson_UsesThePascalCaseShapeTheApiReads()
    {
        var rect = MachineFactory.GeometryJson(new MachineGeometry.Rect(10, 20, 30, 40));
        Assert.Equal("""{"Type":"rectangle","Coordinates":[{"X":10,"Y":20},{"X":40,"Y":60}]}""", rect);

        // Centre first, then a rim point one radius away — the distance between them is the radius.
        var circle = MachineFactory.GeometryJson(new MachineGeometry.Circle(100, 100, 25));
        Assert.Equal("""{"Type":"circle","Coordinates":[{"X":100,"Y":100},{"X":125,"Y":100}]}""", circle);
    }

    [Fact]
    public void EveryMachine_BelongsToACell()
    {
        Assert.All(MachineCatalog.All, m => Assert.False(string.IsNullOrWhiteSpace(m.GroupName)));
    }

    [Fact]
    public void NoCellSpansTwoMachineTypes()
    {
        // Groups carry a resource_type_id, so a cell holding both a mill and a drill could not be
        // written at all — the seeder would key two different type ids to one group name.
        foreach (var cell in MachineCatalog.All.GroupBy(m => m.GroupName))
        {
            Assert.True(cell.Select(m => m.TypeKey).Distinct().Count() == 1,
                $"Cell '{cell.Key}' mixes machine types: {string.Join(", ", cell.Select(m => m.TypeKey).Distinct())}.");
        }
    }

    [Fact]
    public void EveryMachineType_HasAtLeastOneCell()
    {
        var typesWithCells = MachineCatalog.All.Select(m => m.TypeKey).ToHashSet();
        foreach (var type in MachineCatalog.Types)
            Assert.Contains(type.Key, typesWithCells);
    }

    private static (decimal MinX, decimal MinY, decimal MaxX, decimal MaxY) Extent(MachineGeometry g) => g switch
    {
        MachineGeometry.Rect r => (r.X, r.Y, r.X + r.W, r.Y + r.H),
        MachineGeometry.Circle c => (c.CentreX - c.Radius, c.CentreY - c.Radius,
                                     c.CentreX + c.Radius, c.CentreY + c.Radius),
        _ => throw new InvalidOperationException($"Unhandled geometry {g.GetType().Name}"),
    };
}
