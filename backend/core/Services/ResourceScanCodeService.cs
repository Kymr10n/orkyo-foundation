using Api.Helpers;
using Api.Models;
using Api.Repositories;

namespace Api.Services;

/// <summary>
/// Links QR sticker codes to resources and resolves a scanned code to its resource
/// (docs/qr-resource-linking-spec.md). The type switch
/// <see cref="ResourceTypeInfo.ScanCodesEnabled"/> gates both directions.
/// </summary>
public interface IResourceScanCodeService
{
    /// <summary>
    /// Resolves a scanned code. A code whose type has scanning off reports
    /// <see cref="ScanCodeLookupStatus.TypeDisabled"/> without naming the resource.
    /// </summary>
    Task<ScanCodeLookupResult> LookupAsync(string code, CancellationToken ct = default);
    /// <summary>The codes a resource carries, or null when the resource does not exist.</summary>
    Task<List<ResourceScanCodeInfo>?> GetByResourceAsync(Guid resourceId, CancellationToken ct = default);
    /// <summary>
    /// Links a code to a resource and returns the link; a code already on that resource returns
    /// the existing link. Throws <see cref="ConflictException"/> when another resource carries
    /// the code and the request does not move it, and <see cref="ArgumentException"/> when the
    /// resource's type has scanning off.
    /// </summary>
    Task<ResourceScanCodeInfo> LinkAsync(Guid resourceId, LinkResourceScanCodeRequest request, Guid? userId, CancellationToken ct = default);
    Task<bool> UnlinkAsync(Guid resourceId, Guid codeId, CancellationToken ct = default);
}

public class ResourceScanCodeService(
    IResourceScanCodeRepository repository,
    IResourceRepository resourceRepository,
    IResourceTypeRepository resourceTypeRepository) : IResourceScanCodeService
{
    public async Task<ScanCodeLookupResult> LookupAsync(string code, CancellationToken ct = default)
    {
        var match = await repository.GetByCodeAsync(code.Trim(), ct);
        if (match is null)
            return new ScanCodeLookupResult { Status = ScanCodeLookupStatus.Unknown };
        if (!match.ScanCodesEnabled)
            return new ScanCodeLookupResult { Status = ScanCodeLookupStatus.TypeDisabled };
        return new ScanCodeLookupResult { Status = ScanCodeLookupStatus.Linked, Resource = match.Resource };
    }

    public async Task<List<ResourceScanCodeInfo>?> GetByResourceAsync(Guid resourceId, CancellationToken ct = default)
    {
        if (await resourceRepository.GetByIdAsync(resourceId, ct) is null) return null;
        return await repository.GetByResourceAsync(resourceId, ct);
    }

    public async Task<ResourceScanCodeInfo> LinkAsync(
        Guid resourceId, LinkResourceScanCodeRequest request, Guid? userId, CancellationToken ct = default)
    {
        var resource = await resourceRepository.GetByIdAsync(resourceId, ct)
            ?? throw new NotFoundException("Resource", resourceId);
        var type = await resourceTypeRepository.GetByIdAsync(resource.ResourceTypeId, ct)
            ?? throw new InvalidOperationException($"Resource type {resource.ResourceTypeId} not found");
        if (!type.ScanCodesEnabled)
            throw new ArgumentException("QR codes are turned off for this resource type.");

        var code = request.Code.Trim();
        if (await repository.UpsertAsync(resourceId, code, userId, request.MoveFromOtherResource, ct) is { } written)
            return written;

        // The code exists and this request does not move it: answer from its owner.
        var existing = await repository.GetByCodeAsync(code, ct)
            ?? throw new ConflictException("This code changed while it was being linked. Scan it again.");
        return existing.Resource.Id == resourceId
            ? existing.Code
            : throw new ConflictException($"This code is already linked to '{existing.Resource.Name}'.");
    }

    public Task<bool> UnlinkAsync(Guid resourceId, Guid codeId, CancellationToken ct = default)
        => repository.DeleteAsync(resourceId, codeId, ct);
}
