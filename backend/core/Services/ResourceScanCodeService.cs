using Api.Constants;
using Api.Helpers;
using Api.Models;
using Api.Repositories;

namespace Api.Services;

public static class ResourceScanCodeRules
{
    /// <summary>A code is stored and matched as the sticker text without surrounding blanks.</summary>
    public static string Normalize(string code) => code.Trim();
}

public enum LinkScanCodeOutcome
{
    Linked,
    AlreadyLinked,
    Moved,
    ResourceNotFound,
    TypeDisabled,
    OwnedByOther,
}

public sealed record LinkScanCodeResult(
    LinkScanCodeOutcome Outcome,
    ResourceScanCodeInfo? Code = null,
    ScanCodeResourceRef? OtherResource = null);

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
    Task<LinkScanCodeResult> LinkAsync(Guid resourceId, LinkResourceScanCodeRequest request, Guid? userId, CancellationToken ct = default);
    Task<bool> UnlinkAsync(Guid resourceId, Guid codeId, CancellationToken ct = default);
}

public class ResourceScanCodeService(
    IResourceScanCodeRepository repository,
    IResourceRepository resourceRepository,
    IResourceTypeRepository resourceTypeRepository) : IResourceScanCodeService
{
    public async Task<ScanCodeLookupResult> LookupAsync(string code, CancellationToken ct = default)
    {
        var normalized = ResourceScanCodeRules.Normalize(code);
        var match = normalized.Length is 0 or > DomainLimits.ResourceScanCodeMaxLength
            ? null
            : await repository.GetByCodeAsync(normalized, ct);

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

    public async Task<LinkScanCodeResult> LinkAsync(
        Guid resourceId, LinkResourceScanCodeRequest request, Guid? userId, CancellationToken ct = default)
    {
        var resource = await resourceRepository.GetByIdAsync(resourceId, ct);
        if (resource is null) return new LinkScanCodeResult(LinkScanCodeOutcome.ResourceNotFound);

        var type = await resourceTypeRepository.GetByIdAsync(resource.ResourceTypeId, ct);
        if (type is null || !type.ScanCodesEnabled) return new LinkScanCodeResult(LinkScanCodeOutcome.TypeDisabled);

        var code = ResourceScanCodeRules.Normalize(request.Code);
        var existing = await repository.GetByCodeAsync(code, ct);
        if (existing is null)
        {
            if (await repository.InsertAsync(resourceId, code, userId, ct) is { } inserted)
                return new LinkScanCodeResult(LinkScanCodeOutcome.Linked, inserted);
            // Lost a race: a concurrent request linked the code first. Answer as if we had read it.
            existing = await repository.GetByCodeAsync(code, ct) ?? throw RaceConflict();
        }

        if (existing.Resource.Id == resourceId)
            return new LinkScanCodeResult(LinkScanCodeOutcome.AlreadyLinked, existing.Code);

        if (!request.MoveFromOtherResource)
            return new LinkScanCodeResult(LinkScanCodeOutcome.OwnedByOther, OtherResource: existing.Resource);

        // A code unlinked between the read and the move is free: link it fresh.
        var moved = await repository.ReassignAsync(existing.Code.Id, resourceId, userId, ct)
            ?? await repository.InsertAsync(resourceId, code, userId, ct)
            ?? throw RaceConflict();
        return new LinkScanCodeResult(LinkScanCodeOutcome.Moved, moved);
    }

    private static ConflictException RaceConflict() =>
        new("This code changed while it was being linked. Scan it again.");

    public Task<bool> UnlinkAsync(Guid resourceId, Guid codeId, CancellationToken ct = default)
        => repository.DeleteAsync(resourceId, codeId, ct);
}
