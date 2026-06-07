namespace Orkyo.Foundation.Seed.Floorplans;

/// <summary>
/// A labeled room on a floorplan. <see cref="X"/>/<see cref="Y"/>/<see cref="W"/>/<see cref="H"/>
/// are a pixel rectangle in the parent <see cref="FloorplanSite"/>'s image space
/// (<see cref="FloorplanSite.WidthPx"/> × <see cref="FloorplanSite.HeightPx"/>). The seeder turns
/// this into a physical space whose <c>geometry</c> is a two-corner rectangle
/// <c>[{X,Y},{X+W,Y+H}]</c> — the exact shape the frontend overlay renders.
/// </summary>
public sealed record FloorplanRoom(
    string Name, string Code, int Capacity, int X, int Y, int W, int H,
    string AllocationMode = "Exclusive");

/// <summary>
/// One demo site backed by one floorplan image plus the rooms drawn on it.
/// <see cref="ImageFileName"/> is matched (by suffix) against the assembly's embedded
/// resources, so the image ships inside the seed container and is available on every reset.
/// </summary>
public sealed record FloorplanSite(
    string Name,
    string Code,
    string ImageFileName,
    int WidthPx,
    int HeightPx,
    IReadOnlyList<FloorplanRoom> Rooms);
