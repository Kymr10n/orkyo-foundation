namespace Api.Models;

public record SiteInfo
{
    public Guid Id { get; init; }
    public required string Name { get; init; }
    public string? Code { get; init; }
    public string? Description { get; init; }
    public string? Address { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}

public record UploadFloorplanRequest
{
    public required Stream Content { get; init; }
    public required string FileName { get; init; }
    public required string ContentType { get; init; }
    public long ContentLength { get; init; }
}
