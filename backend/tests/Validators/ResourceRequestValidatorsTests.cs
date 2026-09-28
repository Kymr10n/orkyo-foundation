using Api.Constants;
using Api.Models;
using Api.Validators;
using FluentValidation;

namespace Orkyo.Foundation.Tests.Validators;

/// <summary>
/// Shape rules of the resource requests. <c>ResourceEndpointTests</c> keeps one HTTP 400 to prove
/// the create validator is wired; the rules live here.
/// </summary>
public class ResourceRequestValidatorsTests
{
    private readonly IValidator<CreateResourceRequest> _createValidator = new CreateResourceRequestValidator();
    private readonly IValidator<UpdateResourceRequest> _updateValidator = new UpdateResourceRequestValidator();

    [Fact]
    public void Create_ConcurrentCapacity_Fails()
    {
        // Accepted at create, then refused on every assignment: not offered until implemented.
        var request = new CreateResourceRequest
        {
            ResourceTypeKey = ResourceTypeKeys.Space,
            AllocationMode = AllocationModes.ConcurrentCapacity,
            Name = "Line 1",
            IsPhysical = false
        };

        Assert.False(_createValidator.Validate(request).IsValid);
    }

    [Fact]
    public void Create_NullName_Fails()
    {
        var request = new CreateResourceRequest
        {
            ResourceTypeKey = ResourceTypeKeys.Space,
            AllocationMode = AllocationModes.Exclusive,
            Name = null!,
            IsPhysical = false
        };

        Assert.False(_createValidator.Validate(request).IsValid);
    }

    private static readonly CreateResourceRequest Room = new()
    {
        ResourceTypeKey = ResourceTypeKeys.Space,
        AllocationMode = AllocationModes.Exclusive,
        Name = "Room",
        IsPhysical = false,
    };

    [Theory]
    [InlineData("circle", 1)]    // a centre without a rim point
    [InlineData("rectangle", 1)] // a rectangle is two corners
    [InlineData("polygon", 2)]   // a polygon needs at least three points
    [InlineData("hexagon", 2)]   // a type the allow-list has never heard of
    public void Create_InvalidGeometry_Fails(string type, int pointCount) =>
        Assert.False(_createValidator.Validate(Room with
        {
            Geometry = new ResourceGeometry
            {
                Type = type,
                Coordinates = Enumerable.Range(0, pointCount).Select(i => new Coordinate { X = i * 10, Y = i * 10 }).ToList(),
            },
        }).IsValid);

    [Fact]
    public void Create_PhysicalWithoutGeometry_Fails() =>
        Assert.False(_createValidator.Validate(Room with { IsPhysical = true }).IsValid);

    [Fact]
    public void Create_EmptyName_Fails() =>
        Assert.False(_createValidator.Validate(Room with { Name = "" }).IsValid);

    [Fact]
    public void Create_AvailabilityOver100_Fails() =>
        Assert.False(_createValidator.Validate(Room with { BaseAvailabilityPercent = 150 }).IsValid);

    [Fact]
    public void Create_ZeroCapacity_Fails() =>
        Assert.False(_createValidator.Validate(Room with { Capacity = 0 }).IsValid);

    [Fact]
    public void Update_NameOnly_Passes()
    {
        Assert.True(_updateValidator.Validate(new UpdateResourceRequest { Name = "Updated Name" }).IsValid);
    }

    [Fact]
    public void Update_InvalidGeometry_Fails()
    {
        var request = new UpdateResourceRequest
        {
            Geometry = new ResourceGeometry
            {
                Type = "invalid",
                Coordinates = new List<Coordinate> { new() { X = 0, Y = 0 } }
            }
        };

        Assert.False(_updateValidator.Validate(request).IsValid);
    }
}
