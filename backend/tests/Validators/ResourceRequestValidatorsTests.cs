using Api.Constants;
using Api.Models;
using Api.Validators;
using FluentValidation;

namespace Orkyo.Foundation.Tests.Validators;

/// <summary>
/// Validator cases the resource endpoint tests do not reach. The valid and invalid create shapes
/// (virtual, physical, missing geometry, bad geometry, empty name) are covered end to end in
/// <c>SpaceEndpointsTests</c> and <c>ResourceEndpointTests</c>.
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
