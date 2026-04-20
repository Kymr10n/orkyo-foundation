namespace Api.Models;

public record SiteInfo
{
    public Guid Id { get; init; }
    public required string Name { get; init; }
    public string? Code { get; init; }
    public string? Description { get; init; }
    public string? Address { get; init; }
    public string? Attributes { get; init; } // JSON
    public FloorplanMetadata? Floorplan { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}

public record FloorplanMetadata
{
    public required string ImagePath { get; init; }
    public required string MimeType { get; init; }
    public long FileSizeBytes { get; init; }
    public int WidthPx { get; init; }
    public int HeightPx { get; init; }
    public DateTime UploadedAt { get; init; }
    public Guid? UploadedByUserId { get; init; }
}

public record UploadFloorplanRequest
{
    public required IFormFile File { get; init; }
}
