using System.Text.Json;
using Orkyo.Foundation.Seed.Factories;
using Orkyo.Foundation.Seed.Floorplans;
using Xunit;

namespace Orkyo.Foundation.Seed.Tests;

/// <summary>
/// Unit guards for the curated floorplan fixtures: the manufacturing set is well-formed (unique
/// codes, in-bounds rectangles), its images are embedded and decodable, and the geometry JSON the
/// factory writes matches the PascalCase shape the API serializes (so the read path binds it).
/// </summary>
public class FloorplanCatalogTests
{
    private static readonly IReadOnlyList<FloorplanSite> Manufacturing =
        FloorplanCatalog.ForProfile("manufacturing");

    [Fact]
    public void Manufacturing_HasThreeSites_With43Rooms()
    {
        Assert.Equal(3, Manufacturing.Count);
        Assert.Equal(43, Manufacturing.Sum(s => s.Rooms.Count));
        Assert.Equal(new[] { "PMF", "FWF", "PPF" }, Manufacturing.Select(s => s.Code).ToArray());
    }

    [Theory]
    [InlineData("camping")]
    [InlineData("generic")]
    [InlineData("construction")]
    public void NonManufacturingProfile_HasNoFloorplans(string profile)
    {
        Assert.Empty(FloorplanCatalog.ForProfile(profile));
    }

    [Fact]
    public void EverySite_HasUniqueRoomCodes()
    {
        foreach (var site in Manufacturing)
        {
            var codes = site.Rooms.Select(r => r.Code).ToList();
            Assert.Equal(codes.Count, codes.Distinct().Count());
        }
    }

    [Fact]
    public void EveryRoom_HasPositiveSize_InBounds_AndCapacity()
    {
        foreach (var site in Manufacturing)
        {
            Assert.Equal(1536, site.WidthPx);
            Assert.Equal(1024, site.HeightPx);
            foreach (var r in site.Rooms)
            {
                Assert.True(r.W > 0 && r.H > 0, $"{site.Code}/{r.Code} must have positive size");
                Assert.True(r.Capacity > 0, $"{site.Code}/{r.Code} must have positive capacity");
                Assert.True(r.X >= 0 && r.Y >= 0, $"{site.Code}/{r.Code} origin must be non-negative");
                Assert.True(r.X + r.W <= site.WidthPx, $"{site.Code}/{r.Code} must fit image width");
                Assert.True(r.Y + r.H <= site.HeightPx, $"{site.Code}/{r.Code} must fit image height");
            }
        }
    }

    [Fact]
    public void EverySite_Image_IsEmbedded_AndIsPng()
    {
        foreach (var site in Manufacturing)
        {
            var bytes = FloorplanFactory.ReadEmbeddedImage(site.ImageFileName);
            Assert.NotEmpty(bytes);
            // PNG magic: 89 50 4E 47
            Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, bytes.Take(4).ToArray());
        }
    }

    [Fact]
    public void RectangleGeometryJson_IsPascalCaseTwoCornerRectangle()
    {
        var room = new FloorplanRoom("Test", "T", 4, 100, 200, 50, 60);
        var json = FloorplanFactory.RectangleGeometryJson(room);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("rectangle", root.GetProperty("Type").GetString());
        var coords = root.GetProperty("Coordinates");
        Assert.Equal(2, coords.GetArrayLength());
        Assert.Equal(100, coords[0].GetProperty("X").GetInt32());
        Assert.Equal(200, coords[0].GetProperty("Y").GetInt32());
        Assert.Equal(150, coords[1].GetProperty("X").GetInt32()); // X + W
        Assert.Equal(260, coords[1].GetProperty("Y").GetInt32()); // Y + H
    }
}
