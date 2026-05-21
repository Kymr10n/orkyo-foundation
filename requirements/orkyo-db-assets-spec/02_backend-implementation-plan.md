# 02 — Backend Implementation Plan

## Goal

Introduce a generic PostgreSQL-backed asset storage layer and migrate floorplan handling to it.

Copilot should implement this incrementally, keeping existing behavior stable while removing the requirement for filesystem-based asset storage.

## Principles

- Keep the domain model clean.
- Do not embed binary data directly in site/space/location tables.
- Use an asset abstraction so a future S3/MinIO provider can be added later.
- Do not expose storage internals to the frontend.
- Enforce tenant isolation in every query.
- Remove obsolete filesystem-path logic once migration is complete.

## Proposed backend components

Adjust names/namespaces to match the existing solution structure.

```text
Backend/
  Assets/
    Asset.cs
    AssetStorageKind.cs
    AssetType.cs
    AssetOwnerType.cs
    AssetDbContextConfiguration.cs
    IAssetStorageService.cs
    PostgresAssetStorageService.cs
    AssetValidationOptions.cs
    AssetValidator.cs
    AssetController.cs
    AssetDtos.cs
```

If Orkyo already has a feature/module structure, place these files according to existing conventions.

## Entity model

Create an `Asset` entity with:

```csharp
public sealed class Asset
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string OwnerType { get; set; } = default!;
    public Guid OwnerId { get; set; }
    public string AssetType { get; set; } = default!;
    public string FileName { get; set; } = default!;
    public string ContentType { get; set; } = default!;
    public long SizeBytes { get; set; }
    public string ChecksumSha256 { get; set; } = default!;
    public string StorageKind { get; set; } = "postgres";
    public byte[]? Data { get; set; }
    public string? ExternalUri { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public Guid? UpdatedByUserId { get; set; }
}
```

Use enums if the current codebase uses enum conversions consistently. Otherwise use string constants to avoid brittle migrations.

## EF Core mapping

Configure:

- table name: `assets`
- `data` as `bytea`
- required fields
- indexes for tenant/owner lookup
- optional uniqueness for one floorplan per owner

Example mapping intent:

```csharp
builder.HasIndex(x => new { x.TenantId, x.OwnerType, x.OwnerId });
builder.HasIndex(x => new { x.TenantId, x.AssetType });
```

If enforcing one floorplan per owner:

```csharp
builder.HasIndex(x => new { x.TenantId, x.OwnerType, x.OwnerId, x.AssetType })
    .IsUnique()
    .HasFilter("asset_type = 'floorplan'");
```

## Service interface

Introduce a storage service that hides persistence details.

```csharp
public interface IAssetStorageService
{
    Task<AssetMetadataDto> UploadAsync(
        Guid tenantId,
        string ownerType,
        Guid ownerId,
        string assetType,
        IFormFile file,
        Guid? userId,
        CancellationToken cancellationToken);

    Task<AssetDownloadDto?> GetAsync(
        Guid tenantId,
        Guid assetId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<AssetMetadataDto>> ListForOwnerAsync(
        Guid tenantId,
        string ownerType,
        Guid ownerId,
        CancellationToken cancellationToken);

    Task<bool> DeleteAsync(
        Guid tenantId,
        Guid assetId,
        Guid? userId,
        CancellationToken cancellationToken);
}
```

Suggested DTOs:

```csharp
public sealed record AssetMetadataDto(
    Guid Id,
    string OwnerType,
    Guid OwnerId,
    string AssetType,
    string FileName,
    string ContentType,
    long SizeBytes,
    string ChecksumSha256,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record AssetDownloadDto(
    Guid Id,
    string FileName,
    string ContentType,
    long SizeBytes,
    string ChecksumSha256,
    Stream Content);
```

For PostgreSQL-backed implementation, a `MemoryStream` over `byte[]` is acceptable with the configured asset size limit.

## Validation

Create centralized validation.

Configurable options:

```json
{
  "Assets": {
    "MaxUploadBytes": 26214400,
    "AllowedContentTypes": [
      "image/png",
      "image/jpeg"
    ]
  }
}
```

Validation rules:

- file must exist
- file size must be greater than zero
- file size must not exceed configured maximum
- content type must be allowed
- file extension should match allowed type
- filename must be sanitized
- checksum must be calculated server-side

SVG should not be enabled by default unless there is already a safe sanitizer or clear product requirement.

## Controller endpoints

Use existing routing and tenant-resolution conventions.

Recommended endpoints:

```http
POST   /api/assets/{ownerType}/{ownerId}/{assetType}
GET    /api/assets/{assetId}
GET    /api/assets/{ownerType}/{ownerId}
DELETE /api/assets/{assetId}
```

Alternative owner-specific routes are also acceptable if the existing API prefers nested resources:

```http
POST   /api/sites/{siteId}/floorplan
GET    /api/sites/{siteId}/floorplan
DELETE /api/sites/{siteId}/floorplan
```

Preferred approach:

- implement generic asset service internally
- expose narrow, domain-specific endpoints first if that keeps the frontend simpler

## Upload behavior

For `floorplan`, decide whether the owner can have one or many.

Recommended first behavior:

```text
one current floorplan per owner
```

On upload:

- validate authorization against owner
- validate file
- calculate checksum
- replace existing floorplan asset for same owner, or update existing record
- return metadata

Replacement options:

1. Delete old asset and insert new one.
2. Update existing asset row in place.

Recommendation: update existing row in place for stable asset references, unless version history is desired.

## Download behavior

On download:

- resolve tenant from request context
- load asset by `tenant_id + asset_id`
- verify authorization against owner
- return `File(stream, contentType, fileName)`
- set ETag from checksum

Do not allow direct lookup without tenant filter.

## Delete behavior

On delete:

- resolve tenant
- load asset by `tenant_id + asset_id`
- verify authorization against owner
- delete asset
- return `204 No Content`

## Integration with existing floorplan logic

Find all current usages of filesystem floorplan paths.

Likely areas:

- upload endpoint
- static file serving
- Docker volume assumptions
- environment variables for asset paths
- compose files
- Portainer stack files
- frontend URL construction
- tests expecting local file paths

Replace with asset service usage.

## Configuration cleanup

Remove or deprecate variables such as:

```text
ASSET_STORAGE_PATH
FLOORPLAN_STORAGE_PATH
UPLOADS_PATH
```

Do not break existing deployments immediately if migration compatibility is needed. Mark them as deprecated and unused after migration.

## Tests

Add backend tests for:

- valid upload
- invalid content type
- oversized file
- download by authorized tenant user
- cross-tenant access blocked
- delete asset
- replacement upload for existing floorplan
- checksum and ETag behavior

## Logging

Log asset operations at information level without logging binary content.

Recommended fields:

- tenant id
- asset id
- owner type
- owner id
- asset type
- size bytes
- content type

Do not log file contents.
