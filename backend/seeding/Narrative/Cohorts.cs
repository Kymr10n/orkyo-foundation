using Orkyo.Foundation.Seed.Factories;
using Orkyo.Foundation.Seed.Floorplans;

namespace Orkyo.Foundation.Seed.Narrative;

/// <summary>
/// A facility's working set: its site, the people working there (a slice of the global people),
/// its tools, and its rooms-as-spaces keyed by room code. People and tools are tenant-global rows;
/// this in-memory partition is what makes the demo's work facility-coherent.
/// </summary>
public sealed record FacilityCohort(
    Facility Facility,
    Guid SiteId,
    IReadOnlyList<PeopleFactories.SeededPerson> People,
    IReadOnlyList<ToolFactory.SeededTool> Tools,
    IReadOnlyDictionary<string, SpaceFactories.SeededSpace> SpaceByRoomCode);

public static class Cohorts
{
    public static IReadOnlyList<FacilityCohort> Build(
        IReadOnlyList<Facility> facilities,
        IReadOnlyList<SpaceFactories.SeededSite> sites,
        IReadOnlyList<SpaceFactories.SeededSpace> spaces,
        IReadOnlyList<PeopleFactories.SeededPerson> people,
        IReadOnlyList<ToolFactory.SeededTool> tools)
    {
        var floorplans = FloorplanCatalog.ForProfile("manufacturing");
        var per = Math.Max(1, people.Count / facilities.Count);
        var cohorts = new List<FacilityCohort>(facilities.Count);

        for (var i = 0; i < facilities.Count; i++)
        {
            var f = facilities[i];
            var site = sites.First(s => s.Code == f.SiteCode);
            var floorplan = floorplans.First(fp => fp.Code == f.SiteCode);

            // room code → seeded space (match by room name within the site; FloorplanFactory names
            // the space resource after the room).
            var spaceByRoom = new Dictionary<string, SpaceFactories.SeededSpace>();
            foreach (var room in floorplan.Rooms)
            {
                var space = spaces.FirstOrDefault(sp => sp.SiteId == site.Id && sp.Name == room.Name);
                if (space is not null) spaceByRoom[room.Code] = space;
            }

            var start = i * per;
            var count = i == facilities.Count - 1 ? people.Count - start : per;
            var cohortPeople = people.Skip(start).Take(Math.Max(0, count)).ToList();
            var cohortTools = tools.Where(t => t.SiteCode == f.SiteCode).ToList();

            cohorts.Add(new FacilityCohort(f, site.Id, cohortPeople, cohortTools, spaceByRoom));
        }
        return cohorts;
    }
}
