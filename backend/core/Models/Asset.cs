namespace Api.Models;

public static class AssetOwnerTypes
{
    public const string Site = "site";
}

public static class AssetTypes
{
    public const string Floorplan = "floorplan";
}

public static class AssetStorageKinds
{
    public const string Postgres = "postgres";
}

public sealed record AssetInfo
{
    public required Guid Id { get; init; }
    public required Guid TenantId { get; init; }
    public required string OwnerType { get; init; }
    public required Guid OwnerId { get; init; }
    public required string AssetType { get; init; }
    public required string FileName { get; init; }
    public required string ContentType { get; init; }
    public required long SizeBytes { get; init; }
    public required string ChecksumSha256 { get; init; }
    public int? WidthPx { get; init; }
    public int? HeightPx { get; init; }
    public required string StorageKind { get; init; }
    public string? ExternalUri { get; init; }
    public required DateTime CreatedAt { get; init; }
    public required DateTime UpdatedAt { get; init; }
    public Guid? CreatedByUserId { get; init; }
    public Guid? UpdatedByUserId { get; init; }
}

public sealed record AssetDownloadInfo
{
    public required AssetInfo Metadata { get; init; }
    public required byte[] Data { get; init; }
}

public sealed record FloorplanMetadataInfo
{
    public required Guid AssetId { get; init; }
    public required string FileName { get; init; }
    public required string MimeType { get; init; }
    public required long FileSizeBytes { get; init; }
    public required string ChecksumSha256 { get; init; }
    public required int WidthPx { get; init; }
    public required int HeightPx { get; init; }
    public required DateTime UploadedAt { get; init; }
    public Guid? UploadedByUserId { get; init; }
}
