namespace Orkyo.Foundation.Seed.Floorplans;

/// <summary>
/// Curated floorplan fixtures keyed by profile slug. A profile with a populated set produces
/// fixed, geometry-bearing sites/spaces when the seeder runs with <c>--floorplans</c>; profiles
/// without a set return empty (and <c>--floorplans</c> is rejected for them). Manufacturing is the
/// first populated set — the three demo facilities. Room rectangles are authored in each image's
/// 1536×1024 pixel space.
/// </summary>
public static class FloorplanCatalog
{
    public static IReadOnlyList<FloorplanSite> ForProfile(string profileSlug) =>
        profileSlug.Equals("manufacturing", StringComparison.OrdinalIgnoreCase)
            ? Manufacturing
            : Array.Empty<FloorplanSite>();

    private static readonly IReadOnlyList<FloorplanSite> Manufacturing = new[]
    {
        new FloorplanSite("Precision Manufacturing", "PMF", "precision-manufacturing.png", 1536, 1024, new[]
        {
            // Left office strip
            new FloorplanRoom("Office",                "OFC",   4,  44,  96, 206,  80),
            new FloorplanRoom("Conference",            "CONF", 12,  44, 184, 206, 116),
            new FloorplanRoom("HR / Admin",            "HR",    6,  44, 310, 206,  78),
            new FloorplanRoom("Lobby / Reception",     "LOBBY", 8,  44, 398, 206, 117),
            new FloorplanRoom("Break Room",            "BREAK",20,  44, 558, 206, 132),
            new FloorplanRoom("Restrooms",             "WC",    6,  44, 698, 206,  60),
            new FloorplanRoom("Janitor",               "JAN",   1,  44, 762, 206,  58),
            new FloorplanRoom("Electrical",            "ELEC",  1,  44, 824, 206,  68),
            // Main floor
            new FloorplanRoom("CNC Machining",         "CNC",   8, 430, 150, 450, 290),
            new FloorplanRoom("Assembly",              "ASSY", 12, 930, 150, 510, 290),
            new FloorplanRoom("Quality Control",       "QC",    6, 540, 468, 330,  92),
            new FloorplanRoom("Raw Material Storage",  "RAW",   4, 430, 585, 315, 280, "ConcurrentCapacity"),
            new FloorplanRoom("Finished Goods Storage","FIN",   4, 750, 585, 315, 280, "ConcurrentCapacity"),
        }),

        new FloorplanSite("Fabrication & Welding", "FWF", "fabrication-welding.png", 1536, 1024, new[]
        {
            new FloorplanRoom("Office",                "OFC",   4,  44,  96, 206,  80),
            new FloorplanRoom("Conference",            "CONF", 12,  44, 184, 206, 110),
            new FloorplanRoom("Planning",              "PLAN",  6,  44, 302, 206,  80),
            new FloorplanRoom("Lobby / Reception",     "LOBBY", 8,  44, 392, 206,  95),
            new FloorplanRoom("Lockers",               "LOCK", 12,  44, 512, 206,  95),
            new FloorplanRoom("Restrooms",             "WC",    6,  44, 612, 206,  70),
            new FloorplanRoom("Shower",                "SHWR",  2,  44, 688, 206,  60),
            new FloorplanRoom("Janitor",               "JAN",   1,  44, 754, 206,  58),
            new FloorplanRoom("Electrical",            "ELEC",  1,  44, 818, 206,  70),
            new FloorplanRoom("Fabrication",           "FAB",  10, 430, 150, 420, 340),
            new FloorplanRoom("Welding",               "WELD", 12, 870, 150, 580, 340),
            new FloorplanRoom("Paint Booth",           "PAINT", 3, 430, 560, 175, 165),
            new FloorplanRoom("Quality Control",       "QC",    6, 610, 560, 250, 165),
            new FloorplanRoom("Finishing / Grinding",  "GRIND", 4, 875, 560, 205, 165),
            new FloorplanRoom("Material Storage",      "MAT",   4,1150, 560, 295, 165, "ConcurrentCapacity"),
        }),

        new FloorplanSite("Production & Packaging", "PPF", "production-packaging.png", 1536, 1024, new[]
        {
            new FloorplanRoom("Office",                "OFC",   4,  44, 100, 206,  76),
            new FloorplanRoom("Conference",            "CONF", 12,  44, 188, 206, 112),
            new FloorplanRoom("Operations",            "OPS",   6,  44, 312, 206,  78),
            new FloorplanRoom("Lobby / Reception",     "LOBBY", 8,  44, 400, 206, 116),
            new FloorplanRoom("Break Room",            "BREAK",20,  44, 548, 206, 102),
            new FloorplanRoom("Restrooms",             "WC",    6,  44, 660, 206,  68),
            new FloorplanRoom("Janitor",               "JAN",   1,  44, 740, 206,  65),
            new FloorplanRoom("Electrical",            "ELEC",  1,  44, 812, 206,  72),
            new FloorplanRoom("Production Line",       "PROD", 16, 430, 110,1000, 180),
            new FloorplanRoom("Packaging",             "PKG",   8, 430, 340, 270, 160),
            new FloorplanRoom("Quality Control",       "QC",    6, 800, 340, 160, 160),
            new FloorplanRoom("Maintenance",           "MAINT", 4,1030, 340, 170, 160),
            new FloorplanRoom("Tool Room",             "TOOL",  3,1250, 340, 170, 160),
            new FloorplanRoom("Warehouse / Storage",   "WHSE",  6, 430, 540, 650, 320, "ConcurrentCapacity"),
            new FloorplanRoom("Raw Material Storage",  "RAW",   4,1110, 540, 320, 320, "ConcurrentCapacity"),
        }),
    };
}
