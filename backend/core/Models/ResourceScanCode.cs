namespace Api.Models;

/// <summary>A QR code linked to a resource: the decoded sticker text, unique per tenant.</summary>
public record ResourceScanCodeInfo
{
    public required Guid Id { get; init; }
    public required Guid ResourceId { get; init; }
    public required string Code { get; init; }
    public DateTime CreatedAt { get; init; }
}

public record LinkResourceScanCodeRequest
{
    public required string Code { get; init; }
    /// <summary>
    /// When the code already names a different resource: true moves the link here, false
    /// refuses with 409. A link never moves silently.
    /// </summary>
    public bool MoveFromOtherResource { get; init; }
}

/// <summary>The resource a code names, as much as a scan needs to open it.</summary>
public record ScanCodeResourceRef
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required string ResourceTypeKey { get; init; }
    public required bool IsActive { get; init; }
}

public static class ScanCodeLookupStatus
{
    public const string Linked = "linked";
    public const string Unknown = "unknown";
    /// <summary>Linked, but the type has scanning off: the resource is not named.</summary>
    public const string TypeDisabled = "type_disabled";
}

public record ScanCodeLookupResult
{
    /// <summary>One of <see cref="ScanCodeLookupStatus"/>.</summary>
    public required string Status { get; init; }
    /// <summary>Set only when <see cref="Status"/> is <see cref="ScanCodeLookupStatus.Linked"/>.</summary>
    public ScanCodeResourceRef? Resource { get; init; }
}

/// <summary>A stored code joined with its resource and the type switch, for a lookup.</summary>
public sealed record ScanCodeMatch(ResourceScanCodeInfo Code, ScanCodeResourceRef Resource, bool ScanCodesEnabled);
