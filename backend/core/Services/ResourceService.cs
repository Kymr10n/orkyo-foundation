using System.Text.Json;
using Api.Constants;
using Api.Models;
using Api.Repositories;
using Api.Security.Quotas;

namespace Api.Services;

/// <summary>
/// Service layer for resources of every type, spaces included. Validates allocation mode,
/// availability and placement constraints before persistence, and is the single place the
/// placeable-resource quota is enforced.
/// </summary>
public interface IResourceService
{
    /// <summary>Returns all resources matching the given filter.</summary>
    Task<List<ResourceInfo>> GetAllAsync(ResourceListFilter filter, CancellationToken ct = default);
    /// <summary>Returns the resource with the given ID, or <c>null</c> if not found.</summary>
    Task<ResourceInfo?> GetByIdAsync(Guid id, CancellationToken ct = default);
    /// <summary>Creates a new resource. Validates allocation mode and availability percent.</summary>
    Task<ResourceInfo> CreateAsync(CreateResourceRequest request, CancellationToken ct = default);
    /// <summary>
    /// Updates a resource. Returns <c>null</c> if not found.
    /// Throws <see cref="System.Collections.Generic.KeyNotFoundException"/> if the resource was deleted between the existence check and the update.
    /// </summary>
    Task<ResourceInfo?> UpdateAsync(Guid id, UpdateResourceRequest request, CancellationToken ct = default);
    /// <summary>Deactivates a resource. Returns <c>false</c> if not found.</summary>
    Task<bool> DeactivateAsync(Guid id, CancellationToken ct = default);
}

public class ResourceService(
    IResourceRepository resourceRepository,
    IResourceTypeRepository resourceTypeRepository,
    IQuotaEnforcer quotaEnforcer,
    IQuotaUsageRollup rollup) : IResourceService
{
    public Task<List<ResourceInfo>> GetAllAsync(ResourceListFilter filter, CancellationToken ct = default)
        => resourceRepository.GetAllAsync(filter, ct);

    public Task<ResourceInfo?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => resourceRepository.GetByIdAsync(id, ct);

    public async Task<ResourceInfo> CreateAsync(CreateResourceRequest request, CancellationToken ct = default)
    {
        Validate(request.AllocationMode, request.BaseAvailabilityPercent, request.Name);

        var resourceType = await resourceTypeRepository.GetByKeyAsync(request.ResourceTypeKey, ct)
            ?? throw new ArgumentException($"Resource type '{request.ResourceTypeKey}' not found");

        if (!resourceType.IsActive)
            throw new ArgumentException($"Resource type '{resourceType.Key}' is not active");

        ValidatePlacement(resourceType, request);

        // Keyed on the type flag, not on the space key: the quota counts what occupies a
        // floorplan, and any type can now declare that it does.
        if (resourceType.HasGeometry)
        {
            var currentCount = await resourceRepository.GetPlaceableCountAsync(ct);
            await quotaEnforcer.EnsureWithinLimitAsync(QuotaResourceTypes.Spaces, currentCount, 1, ct);
        }

        var resource = await resourceRepository.CreateAsync(resourceType.Id, request, ct);

        if (resourceType.HasGeometry)
            await rollup.RecordDeltaAsync(QuotaResourceTypes.Spaces, 1, ct);

        return resource;
    }

    public async Task<ResourceInfo?> UpdateAsync(Guid id, UpdateResourceRequest request, CancellationToken ct = default)
    {
        if (request.AllocationMode is not null)
            ValidateAllocationMode(request.AllocationMode);
        if (request.BaseAvailabilityPercent.HasValue)
            ValidateAvailabilityPercent(request.BaseAvailabilityPercent.Value);
        if (request.Name is not null && string.IsNullOrWhiteSpace(request.Name))
            throw new ArgumentException("Name cannot be blank");

        var existing = await resourceRepository.GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"Resource {id} not found");

        if (request.Code is not null || request.Geometry is not null || request.Properties is not null || request.Capacity.HasValue)
        {
            var resourceType = await resourceTypeRepository.GetByIdAsync(existing.ResourceTypeId, ct)
                ?? throw new InvalidOperationException($"Resource type {existing.ResourceTypeId} not found");
            if (!resourceType.HasGeometry)
                throw new ArgumentException($"Resource type '{resourceType.Key}' cannot be placed, so it has no code, geometry, properties or capacity");
        }

        return await resourceRepository.UpdateAsync(id, request, ct);
    }

    public async Task<bool> DeactivateAsync(Guid id, CancellationToken ct = default)
    {
        var existing = await resourceRepository.GetByIdAsync(id, ct);
        if (existing is null) return false;

        // Fires only on the active → inactive transition (the repository's WHERE guarantees it),
        // so the rollup mirrors the +1 that creating the resource recorded exactly once.
        if (!await resourceRepository.DeactivateAsync(id, ct)) return false;

        var resourceType = await resourceTypeRepository.GetByIdAsync(existing.ResourceTypeId, ct);
        if (resourceType is { HasGeometry: true })
            await rollup.RecordDeltaAsync(QuotaResourceTypes.Spaces, -1, ct);

        return true;
    }

    /// <summary>
    /// Placement values describe where a resource sits on a floorplan, which only means anything
    /// for a type that declares it has one. Rejecting them elsewhere keeps a "vehicle" from
    /// carrying a shape no page will ever draw.
    /// </summary>
    private static void ValidatePlacement(ResourceTypeInfo resourceType, CreateResourceRequest request)
    {
        if (!resourceType.HasGeometry)
        {
            if (request.Code is not null || request.IsPhysical || request.Geometry is not null
                || request.Properties is not null || request.Capacity != 1)
            {
                throw new ArgumentException($"Resource type '{resourceType.Key}' cannot be placed, so it has no code, geometry, properties or capacity");
            }

            return;
        }

        // Placed means anchored. A drawn shape belongs to one floorplan, and several rules
        // read that as given: the implicit site-on-schedule and the working-hours adjustment
        // both look for the first resource that cannot travel to decide where the work
        // happens, and would skip a placeable one that claimed it could. Unreachable while
        // only SpaceService could create a space; reachable now that /api/resources can.
        if (request.CrossSiteAllowed)
        {
            throw new ArgumentException(
                $"Resource type '{resourceType.Key}' is placed on a floorplan, so its resources belong to one site and cannot travel");
        }
    }

    private static void Validate(string allocationMode, int baseAvailabilityPercent, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name is required");
        ValidateAllocationMode(allocationMode);
        ValidateAvailabilityPercent(baseAvailabilityPercent);
    }

    private static void ValidateAllocationMode(string mode)
    {
        if (mode is not (AllocationModes.Exclusive or AllocationModes.Fractional or AllocationModes.ConcurrentCapacity))
            throw new ArgumentException($"Invalid allocation mode '{mode}'");
    }

    private static void ValidateAvailabilityPercent(int pct)
    {
        if (pct is < 0 or > 100)
            throw new ArgumentException("BaseAvailabilityPercent must be between 0 and 100");
    }
}
