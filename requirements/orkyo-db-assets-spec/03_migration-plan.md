# 03 — Filesystem to PostgreSQL Migration Plan

## Objective

Migrate existing floorplan files from filesystem storage into the new `assets` table while preserving user-facing behavior.

## Migration strategy

Use an expand/migrate/contract approach.

```text
1. Expand: add assets table and new asset service
2. Bridge: support reading old filesystem floorplans if DB asset missing
3. Migrate: import existing filesystem floorplans into DB
4. Cut over: frontend/API uses DB-backed asset endpoints
5. Contract: remove filesystem storage requirement and legacy path logic
```

## Phase 1 — Expand

- Add `assets` table.
- Add backend asset service.
- Add upload/download/delete endpoints.
- Keep old filesystem logic untouched initially.

Acceptance:

- new uploads can be stored in DB
- existing floorplans still work through old path

## Phase 2 — Bridge compatibility

Where the application currently resolves a floorplan URL/path:

- first check for DB-backed asset
- if missing, fall back to legacy filesystem path

This allows gradual migration and safe rollback.

Acceptance:

- existing floorplans remain visible
- new uploads use DB asset storage

## Phase 3 — Migration job

Create a one-off migration job/command/tool.

The job should:

1. enumerate existing records that reference filesystem floorplans
2. locate the physical files
3. validate file exists and is readable
4. infer content type safely
5. calculate checksum
6. insert or update asset row
7. mark/report migrated records
8. produce a summary

Do not delete source files during first migration run.

## Migration command behavior

The command should be idempotent.

Suggested CLI options:

```text
--dry-run
--tenant <tenant-id|slug>
--legacy-root-path <path>
--delete-after-success false
--overwrite-existing false
```

Defaults:

```text
dry-run = true unless explicitly disabled
delete-after-success = false
overwrite-existing = false
```

## Dry-run output

The dry run should report:

- number of candidate records
- files found
- files missing
- invalid content types
- oversized files
- records already migrated
- records requiring manual review

## Actual run output

The actual run should report:

- assets inserted
- assets updated
- skipped records
- failed records
- failure reasons

## Error handling

The migration must continue when individual files fail.

Do not abort the entire migration because one file is missing or invalid.

Log each failure with enough context:

- tenant
- owner type
- owner id
- old path
- reason

## Phase 4 — Cutover

After successful migration:

- frontend should use DB-backed asset endpoints
- backend should prefer DB assets
- old filesystem path should no longer be used for new uploads

Keep read fallback for one release if practical.

## Phase 5 — Contract

After validation:

- remove filesystem upload code
- remove mandatory Docker mount
- remove unused environment variables
- remove stale path fields if they are no longer needed
- update deployment documentation
- update Portainer stack documentation

Do not drop old path columns until there is confidence that migration has completed and rollback is no longer needed.

## Rollback

Rollback should be possible during bridge phase.

Before contract phase:

- old files still exist
- old paths still exist
- old code path can be restored if needed

After contract phase:

- rollback requires restoring database backup

## Backup requirement

Before running actual migration in production:

- take a PostgreSQL backup
- preserve the legacy asset folder
- record application version and migration command used

## Docker/Portainer impact

After contract phase, remove mandatory mounts such as:

```yaml
volumes:
  - ./assets:/app/assets
  - ./uploads:/app/uploads
```

Only keep volumes required for PostgreSQL itself.

## Self-hosted impact

This change improves self-hosting because a functional Orkyo instance now requires fewer host-level filesystem assumptions.
