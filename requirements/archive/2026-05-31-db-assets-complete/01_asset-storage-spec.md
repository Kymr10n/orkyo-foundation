# 01 — Asset Storage Specification

## Objective

Move Orkyo-managed assets, initially floorplans, from Docker-mounted filesystem storage to PostgreSQL-backed storage.

The primary driver is operational simplification:

- no mandatory asset volume mounts
- no container filesystem permission handling
- simpler Portainer/self-hosted deployment
- easier backup and restore consistency
- better tenant isolation
- cleaner CI/CD and smoke-test setup

## Scope

### In scope

- floorplan images currently stored on disk
- uploaded layout images associated with site/location/space hierarchy nodes
- future reusable asset model for thumbnails/previews/documents
- backend storage abstraction
- migration path from filesystem to PostgreSQL
- API changes for upload/download/delete

### Out of scope for first implementation

- S3/MinIO implementation
- CDN integration
- large media/document management
- image editing or transformation pipelines
- deduplication across tenants

## Architecture decision

Use PostgreSQL as the default asset storage backend.

Use a generic `Asset` aggregate/table instead of embedding binary columns directly into domain tables such as site, space, or location nodes.

The asset model must support a future external storage backend without requiring changes to business entities.

## Storage strategy

Initial implementation:

```text
storage_kind = postgres
data = bytea
```

Future-compatible implementation:

```text
storage_kind = external
external_uri = s3://... or minio://...
data = null
```

Do not expose storage implementation details to the frontend.

## Proposed database model

Create a generic asset table.

```sql
CREATE TABLE assets (
    id UUID PRIMARY KEY,
    tenant_id UUID NOT NULL,
    owner_type TEXT NOT NULL,
    owner_id UUID NOT NULL,
    asset_type TEXT NOT NULL,
    file_name TEXT NOT NULL,
    content_type TEXT NOT NULL,
    size_bytes BIGINT NOT NULL,
    checksum_sha256 TEXT NOT NULL,
    storage_kind TEXT NOT NULL,
    data BYTEA NULL,
    external_uri TEXT NULL,
    created_at TIMESTAMPTZ NOT NULL,
    updated_at TIMESTAMPTZ NOT NULL,
    created_by_user_id UUID NULL,
    updated_by_user_id UUID NULL
);
```

Recommended indexes:

```sql
CREATE INDEX ix_assets_tenant_owner
    ON assets (tenant_id, owner_type, owner_id);

CREATE INDEX ix_assets_tenant_asset_type
    ON assets (tenant_id, asset_type);

CREATE UNIQUE INDEX ux_assets_owner_asset_type_current
    ON assets (tenant_id, owner_type, owner_id, asset_type)
    WHERE asset_type IN ('floorplan');
```

The unique index is optional. Use it only if the domain allows exactly one current floorplan per owner.

## Field semantics

### `tenant_id`

Must always be present. Assets are tenant-bound domain data.

### `owner_type`

String enum identifying the owning aggregate.

Suggested initial values:

- `site`
- `location_node`
- `space`

Use existing Orkyo naming conventions. If the current hierarchy model uses another term, align with that model.

### `owner_id`

The ID of the owning domain aggregate.

### `asset_type`

String enum identifying the role of the asset.

Suggested initial values:

- `floorplan`
- `thumbnail`
- `attachment`

Only `floorplan` is required initially.

### `content_type`

Must be validated server-side. Do not trust the browser-supplied value only.

Allowed initial MIME types:

- `image/png`
- `image/jpeg`
- `image/svg+xml`
- optionally `application/pdf`, only if floorplan PDFs are already supported or explicitly desired

### `size_bytes`

Used for validation, quotas, and operational diagnostics.

Recommended first limit:

```text
25 MB per asset
```

Make this configurable.

### `checksum_sha256`

Used for integrity validation, diagnostics, and future deduplication.

### `storage_kind`

Initial allowed value:

```text
postgres
```

Future values may include:

```text
s3
minio
external
```

### `data`

Binary payload when `storage_kind = postgres`.

### `external_uri`

Reserved for future external object storage.

Must be null for PostgreSQL-backed assets.

## Domain ownership

Business entities should reference assets by asset ID or query assets by owner.

Avoid adding binary columns to site/space/location tables.

Recommended pattern:

```text
LocationNode/Site/Space
  -> has zero or one floorplan asset
Asset
  -> belongs to tenant
  -> belongs to owner_type + owner_id
```

## Access control

Asset access must follow the same tenant and authorization rules as the owning object.

Rules:

- user must belong to the tenant
- user must have read permission for the owner object to download/view
- user must have edit/manage permission for the owner object to upload/delete
- cross-tenant access must be impossible even if asset IDs are guessed

## API behavior

The frontend must not receive raw `bytea` values in JSON.

Use dedicated binary endpoints for download/streaming.

Recommended response headers:

- `Content-Type`
- `Content-Length`
- `ETag` based on checksum
- `Cache-Control` according to authorization model

For authenticated tenant data, use conservative caching first:

```http
Cache-Control: private, max-age=300
```

## Operational model

PostgreSQL backup now includes both structured data and assets.

This simplifies restore consistency:

```text
restore database => restore domain data and floorplans together
```

No Docker volume mount is required for application-managed assets.

## Risks and mitigations

### Database growth

Risk: large binary data increases database size.

Mitigation:

- enforce upload size limits
- restrict content types
- keep first scope to floorplans
- monitor DB size
- retain storage abstraction for future object storage

### API memory pressure

Risk: loading large files fully into memory.

Mitigation:

- keep size limits modest
- use streaming where practical
- avoid returning binaries in JSON

### SVG security

Risk: SVG can contain script or external references.

Mitigation options:

- disallow SVG initially, or
- sanitize SVG server-side, or
- serve SVG with strict content security headers

Recommended default for first implementation: allow PNG and JPEG only unless SVG support is already required.
