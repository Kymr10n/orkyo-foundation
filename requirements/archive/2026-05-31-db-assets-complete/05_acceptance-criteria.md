# 05 — Acceptance Criteria

## Functional acceptance

- Users can upload a floorplan without any configured filesystem asset mount.
- Uploaded floorplans are stored in PostgreSQL.
- Users can view/download uploaded floorplans through the application.
- Users can replace an existing floorplan.
- Users can delete a floorplan.
- Existing floorplans continue to work during migration bridge phase.
- Migration job can import existing filesystem floorplans into PostgreSQL.

## Security acceptance

- Asset queries always include tenant filtering.
- Cross-tenant asset access is blocked.
- Users without read access to the owner cannot download the asset.
- Users without write/manage access to the owner cannot upload/delete the asset.
- Unsupported file types are rejected.
- Oversized files are rejected.
- Raw binary data is never returned in JSON responses.

## Operational acceptance

- Application can run without an asset/upload Docker volume mount.
- Portainer stack no longer requires an asset folder mount after cutover.
- PostgreSQL backup contains floorplan assets.
- Restore from PostgreSQL backup restores floorplans together with domain data.
- CI smoke tests do not depend on host filesystem asset permissions.

## Technical acceptance

- Generic asset abstraction exists.
- PostgreSQL-backed asset service exists.
- Asset validation is centralized and configurable.
- Existing floorplan-specific code uses the asset service.
- Obsolete filesystem path logic is removed after migration contract phase.
- Tests cover upload, download, delete, replacement, validation, and tenant isolation.

## Suggested test cases

### Backend unit/integration tests

1. Upload valid PNG floorplan.
2. Upload valid JPEG floorplan.
3. Reject unsupported MIME type.
4. Reject oversized file.
5. Replace existing floorplan for same owner.
6. Download existing floorplan.
7. Return 404 for unknown asset.
8. Return 403 or 404 for cross-tenant access.
9. Delete existing asset.
10. Verify checksum is generated.
11. Verify ETag uses checksum.

### Migration tests

1. Dry run reports candidates without writing assets.
2. Actual run imports valid files.
3. Missing files are reported but do not stop migration.
4. Invalid files are skipped.
5. Re-running migration is idempotent.
6. Existing DB assets are not overwritten unless explicitly requested.

### Frontend tests

1. Floorplan upload shows success and refreshes preview.
2. Invalid file type shows clear validation message.
3. Oversized file shows clear validation message.
4. Replace floorplan refreshes displayed image.
5. Delete floorplan removes preview.

## Done definition

This work is done when Orkyo no longer requires a filesystem mount for application-managed floorplans and all new floorplan uploads are persisted through the generic PostgreSQL-backed asset storage layer.
