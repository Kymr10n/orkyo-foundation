# Tenant data transfer — specification and implementation plan

Status: **not implemented**. First draft 2026-07-15. Revised 2026-11-09 after a design review,
on branch `claude/tenant-db-export-import-y8wytv`. The first draft is archived at
`requirements/archive/superseded-docs/tenant-transfer-plan.md`. Section 1 records the
corrections from the review.

This feature moves a complete tenant dataset between Orkyo installations. A tenant admin
downloads the dataset as one archive. The admin imports the archive into a tenant of either
edition. The import erases the target data and loads the archive. The feature also works as a
self-service backup inside one installation.

Product decisions:

| Decision | Value |
|---|---|
| Directions | Community → SaaS and SaaS → Community. The archive format is edition-neutral. |
| Merge | None. The import erases the target tenant data first. One transaction makes this atomic. |
| Authorization | Tenant admin, in the Administration area (`RequireAdminArea()`), behind `IFeatureGate`. |
| Identity | Keycloak accounts and control-plane memberships do not travel. Section 8 gives the rules. |
| Execution | Synchronous streaming endpoints. No job queue. A configured size ceiling applies. |
| Version gate | Class-based. Section 5 gives the algorithm. |

## 1. Corrections from the 2026-11 review

The first draft targeted the July 2026 codebase. A drift audit and an independent design
review found these defects. This revision applies all of them.

1. **The version gate had a corruption path.** The draft compared the archive only against
   the code. It then ran the migrator after the import "to repair a behind tenant". A tenant
   that is behind the code passes that gate. The migrator then re-applies a data migration
   over rows that already contain its effect. The fix: the gate now compares the target
   database journal against the code first. A tenant that is behind is rejected with "run the
   migrator first". The post-import migrator run is deleted. The transfer service no longer
   references `Orkyo.Migrator`.
2. **"Every table travels" was a security defect.** `calendar_feed_tokens` stores token
   hashes that work as bearer credentials. An import would arm every feed URL of the source
   against the target. `audit_events` must also survive an import, or a tenant admin can
   erase the audit history. The table catalog now classifies each table instead of forcing
   travel (section 4).
3. **Encryption reaches beyond `assets`.** `resources.notes`, `sites.description`,
   `sites.address` and `ai_credentials.api_key_ciphertext` hold envelopes bound to the tenant
   id. The export now detects envelopes with `IsProtected` and decrypts them. The import
   re-encrypts them under the target tenant id (section 3).
4. **`pg_constraint` discovery misses user-id columns.** The AI tables, `resource_scan_codes`
   and `calendar_feed_tokens` reference `users.id` without a foreign key, on purpose. The
   catalog now lists the user-id columns per table. A conformance test guards the list
   (section 4).
5. **The SaaS app role has no `TRUNCATE` grant.** Provisioning now splits a DDL-owner role
   from the app role. The import runs on `IDbConnectionFactory.CreateMaintenanceConnectionForDatabase`.
6. **The Community import hook is dead.** Community migration 3030 dropped
   `community_auto_grant_membership_trigger`. The planned `ITenantTransferImportHook` and its
   cleanup are deleted from the design.
7. **The "irrelevant migration ids" constant duplicated a foundation fact.** Foundation now
   stamps the feedback pair (1240, 1630) with `MigrationScope.TenantDatabaseOnly`. The gate
   reads that scope instead of owning a second list.
8. **Two migrator mechanisms were unhandled.** The checksum comparison now accepts the
   current checksum or any entry in `MigrationScript.SupersededChecksums`. A delta script
   without a `-- @migration-class:` header counts as `data` and causes rejection (19 baseline
   files have no header). Both cases are in the gate test matrix.
9. **The feature gate wiring was half-specified, and duplicated.** The draft added a new
   feature key but not its `FeatureKeys.Enforced`, `plans.ts` and plan-matrix entries, so the
   tab would never appear. The revision reuses `FeatureKeys.DataExport`, which the plan
   matrix already labels "Data export / import" (open question 1). The planned
   `useDataTransferAvailable` hook is replaced by the existing `useFeatureEnabled`.
10. **Dead tables and stale anchors.** `invites`, `request_templates` and
    `request_template_requirements` are dropped (1860), so the invite-link warning is gone.
    The draft's coverage recipe is replaced by `scripts/ci/patch-coverage.sh`. Semantic
    validation failures return 422, not 409, which matches `AppExceptionHandler`. The SaaS
    migration id 2200 is taken, and this revision needs no SaaS migration at all.
11. **Simplifications.** `TenantTransferService` is a concrete class without an interface.
    `ITenantTransferGuard` stays, because tenant suspension has no Community analogue. SQL
    lives in `TenantTransferRepository`, because services do not write SQL
    (`ConventionContractTests`).

## 2. Scope

In scope: export of all tenant domain data as one archive, validation of an archive against
a target, destructive import, the Administration UI for both, and operator documentation.

Out of scope: Keycloak accounts and credentials, control-plane data (`tenants`,
`tenant_memberships`, `user_identities`, control-plane `feedback`, subscriptions, quotas),
scheduled or automatic backups, partial or merging imports, and the semantic JSON export
(`ExportService`), which stays as a separate interchange feature with no importer.

## 3. Archive format (version 1)

The archive is a ZIP file, name `orkyo-transfer-<slug>-<yyyyMMdd-HHmmss>.orkyoexport`,
content type `application/zip`. ZIP streams to the non-seekable response body. The upload is
spooled to a temporary file, which gives the seekable stream that reading needs.

Entries: `manifest.json` first, then one `tables/<NNN>_<table>.ndjson` per table in catalog
order. The manifest holds the format version, source edition, `APP_VERSION`, export time,
source tenant id and slug, the tenant-phase rows of `orkyo_schema_migrations` (id, module,
checksum), per-table row counts and column types, and the list of encrypted columns.

Row encoding: one JSON object per row. Strings for uuid, text, timestamptz (ISO-8601 UTC)
and numeric. Native JSON for boolean and integers. Inline JSON for jsonb. Base64 for bytea.
NDJSON is required because the import transforms rows: user-id remapping, `tenant_id`
rewriting, and re-encryption. The import loads each table with one
`COPY … FROM STDIN (FORMAT BINARY)` through `NpgsqlBinaryImporter`.

The export reads all tables in one `REPEATABLE READ` transaction. For every text value, the
export runs `IEncryptionService.IsProtected`. A protected value is decrypted with the source
tenant id and its column is recorded in the manifest. The import re-encrypts the recorded
columns with the target tenant id. Envelopes are self-describing, so this detection does not
need a hand-written column list. The archive therefore holds plain text, including floorplan
images. The UI and the documentation state that the archive is as sensitive as a database dump.

The `users` entry exports only `id`, `email`, `display_name` and `created_at`. Keycloak and
lifecycle columns never travel.

## 4. Table catalog

`TenantTransferTables` in foundation classifies **every** tenant-phase base table. Each
entry is `Travels`, `Merged` (only `users`), or `Excluded` with a reason. The catalog also
lists each table's user-id columns, including those without a foreign key. The import
generates the erase statements and the load order from this catalog. There is no second list.

Excluded, with reasons:

| Table | Reason |
|---|---|
| `orkyo_schema_migrations` | Journal. Travels as manifest metadata only. |
| `audit_events` | The target audit history must survive an import. Not erased, not exported. |
| `calendar_feed_tokens` | Token hashes are bearer credentials. Users mint new feed URLs on the target. |
| `ai_credentials` | Workspace API keys are secrets. Admins enter keys again on the target. |
| `ai_usage`, `ai_daily_usage` | Usage accounting of the source. Not meaningful on the target. |
| `search_documents` | Derived. The search triggers rebuild it during the load. |

A conformance test (`TenantTransferTableConformanceTests`) keeps the catalog honest against
a freshly migrated tenant database. It asserts: every live base table appears in the catalog,
the travel order is a valid foreign-key topological order, no sequences exist, the
`TenantReset` table list is a subset of the catalog, and every column that references
`users.id` (by constraint or by the `*user_id*` / `invited_by` naming pattern) appears in the
catalog's user-column list. The test is written first, so the catalog is correct by
construction. Write this test before the catalog.

## 5. Version gate

Inputs: the manifest journal entries, the target database journal, and the tenant-phase
`MigrationScript` set from the registered `IMigrationModule`s. Scripts with
`MigrationScope.TenantDatabaseOnly` (the feedback pair) are ignored on both sides, read from
the unfiltered `FoundationMigrationModule`.

1. The target journal must equal the code-known set. A target that is behind is rejected
   with error `transfer_target_behind` and the message "run the migrator first".
2. An archive entry whose module is not registered locally (for example `community` inside
   SaaS) produces a warning. Its migrations are seed-only, and step 4 protects the schema.
3. An archive entry with an id the code does not know causes rejection
   (`transfer_archive_incompatible`). An entry whose checksum is not the current checksum
   and not in `MigrationScript.SupersededChecksums` causes the same rejection.
4. Delta = code-known foundation ids minus archive ids. Each delta script's
   `-- @migration-class:` header must be `none` or `expand`. A `data` or `contract` class,
   or a missing header, causes rejection (`transfer_archive_too_old`) with the message
   "upgrade the source installation and export again".
5. Every archive table must be in the catalog and in the live target schema. An archive
   column that the target does not have causes rejection. A target column that the archive
   does not have is allowed and produces a warning. The COPY column list is the intersection.

The gate accepts archives from an equal or older installation when the gap is additive only.
A gap that contains a backfill or a destructive migration cannot replay against restored
rows, so the source must upgrade first. The source migrator applies those migrations there.

## 6. Import sequence

The endpoint spools the upload, reads the manifest, and runs the full gate. `POST
/import/validate` stops here and returns the result. `POST /import` continues:

1. `ITenantTransferGuard.BeginAsync`. The SaaS implementation saves `status`,
   `suspension_reason` and `suspended_at`, sets `status='suspended'` with reason
   `manual_admin`, and calls `ITenantResolver.InvalidateCache`. The reason must not be
   `inactivity`, because that reason permits self-service reactivation and ages into
   deletion. The default implementation does nothing (Community).
2. `pg_advisory_lock` on a dedicated maintenance connection, key
   `orkyo:transfer:<db_identifier>`. This serializes transfers per tenant.
3. One transaction on the maintenance connection (the app role has no `TRUNCATE` grant):
   erase all traveling tables except `users` with `TRUNCATE … CASCADE`, run the user merge
   (section 8), then load each table in catalog order with COPY and the row transforms
   (user-id remap, `assets.tenant_id` rewrite, re-encryption). Compare per-table row counts
   against the manifest. A mismatch throws. Commit.
4. Any failure before the commit rolls back completely. The target keeps its data. A client
   disconnect cancels through the `CancellationToken` and rolls back. A test asserts this.
5. `guard.EndAsync` restores the saved tenant status and invalidates the cache again.
6. The import records `tenant.transfer.import` through the tenant audit seam
   (`ITenantUserService.RecordAuditEventAsync`), with source edition, app version, row count,
   matched users and placeholder count. The export records `tenant.transfer.export`.

Note: the resolver cache is per process. With more than one API replica, a replica blocks
the tenant only after its cache entry expires. The production stack runs one API process, so
this window is accepted and documented (open question 3).

The server does not create a safety copy. The single transaction is the safety mechanism,
and the UI forces an export download before the import unlocks.

## 7. API

| Route | Result |
|---|---|
| `GET /api/admin/transfer/export` | 200, `application/zip`, streamed. |
| `POST /api/admin/transfer/import/validate` | 200 with blockers, warnings, version delta, user-match preview, table counts. |
| `POST /api/admin/transfer/import` | 200 with the import result. |

The group copies `ExportEndpoints.cs`: `/api/admin/transfer`, `.RequireAdminArea()`, the
feature gate, and rate-limit policy `FoundationRateLimitPolicies.AdminOperations`. The POST
routes copy the `FloorplanEndpoints` multipart pattern, with `.DenyLockedAccount()`. Gate
blockers on `POST /import` return 422 as `application/problem+json` with one code from
`ApiErrorCodes` (lowercase snake case). The body points the client to `/import/validate` for
the full blocker list. A wrong confirmation returns 400. An oversized upload returns 413.

The import routes raise `IHttpMaxRequestBodySizeFeature.MaxRequestBodySize` to
`ORKYO_TRANSFER_MAX_ARCHIVE_BYTES` in an endpoint filter, before the body is read. Both
products cap Kestrel at 10 MB globally, and this is the first per-endpoint raise. The value
has no default in code. The env templates carry the default (1 GiB), per the no-fallback
convention. The request validator checks shape only. The handler compares `confirmation`
against `OrgContext.OrgSlug`.

## 8. Identity

The tenant `users` table is merged, never erased. An imported user whose email matches a
target user (case-insensitive) is mapped to the target user id. Every user-id column in the
catalog is remapped through this map during the load. An imported user without a match is
inserted under its imported id as an inactive placeholder: email
`transfer-placeholder+<first8-of-id>@invalid.orkyo`, display name kept, `keycloak_id` null,
`keycloak_metadata` = `{"transfer_placeholder": true, "original_email": "<email>"}`, and
`status='disabled'` where the column exists. The insert follows the `ON CONFLICT` convention
from `docs/conventions.md`. Placeholders keep history readable. They give no access:
Community is invitation-only since migration 3030, and SaaS access lives in the control
plane, which the import never touches.

## 9. Security

- Export and import require the Admin role and the feature entitlement. Both are audited.
- The archive holds plain text, floorplans included. The UI says so at download time.
  Passphrase encryption of the archive was considered and rejected for complexity.
- Bearer credentials (`calendar_feed_tokens`) and secrets (`ai_credentials`) never travel.
- The target audit history survives an import.
- The import is wrapped in one transaction, one advisory lock, and, on SaaS, a suspension
  window. The confirmation field must equal the tenant slug.

## 10. User interface

A new tab "Backup & transfer" (route `data-transfer`) in `TenantAdminPage.tsx`, lazy-routed
in `TenantApp.tsx` under `RequireTenantAdmin`, shown when
`useFeatureEnabled(FeatureKeys.DataExport)` is true. The existing semantic Export card in
`OrganizationSettings.tsx` stays where it is.

The export card downloads through `apiRawFetch(...).blob()`. The import flow is staged: file
selection, automatic validation, a summary (source edition, version, date, row counts,
matched users, placeholder count, warnings), a required backup download, a type-the-slug
confirmation dialog, upload with progress (XHR + FormData, as `floorplan-api.ts`), then a
completion dialog and `window.location.reload()`. The confirmation dialog is one of the
genuinely-special dialogs that `FormDialog` does not cover. The import mutation uses
`meta: { errorMessage }`. Tests render through `createTestQueryWrapper({ feedback: true })`.

## 11. Implementation plan

Each step is one pull request. Every code change ships with ≥80% patch coverage, measured
with `scripts/ci/patch-coverage.sh` (foundation) or the product `check` scripts, per each
repo's CLAUDE.md. Commits carry a `Docs-impact:` trailer. New endpoint groups get a
`RouteInventoryTests` entry in both products. `./scripts/test-downstream.sh` runs before the
foundation PR merges.

**Phase A — foundation backend** (`backend/core/Services/Transfer/`, endpoints in
`backend/src/Endpoints/TransferEndpoints.cs`):
conformance test first, then `TenantTransferTables`, `TransferSchemaInspector`,
`TenantArchiveWriter`/`TenantArchiveReader`, `TenantTransferVersionGate` (references
`Orkyo.Migration.Abstractions` only), `TransferUserRelinker`, `TenantTransferRepository`
(all SQL), `TenantTransferService` (concrete), `ITenantTransferGuard` with a no-op default,
a small advisory-lock helper (the migrator's `AdvisoryLock` is not referenced), endpoint
wiring in `FoundationEndpointExtensions`/`FoundationServiceExtensions`, and the test matrix
from section 5 plus round-trip, mid-failure rollback, re-encryption and remap tests against
`DatabaseFixture`. Confirm that `FeatureKeys.DataExport` is in `FeatureKeys.Enforced`.

**Phase B — foundation frontend**: `api-paths.ts` entries, `transfer-api.ts`, the
`DataTransferSettings` tab, routes, and vitest tests for the staged flow.

**Release step**: foundation version bump. Products reference only the pinned package, so
phases C and D start after the auto-bump PRs land.

**Phase C — saas**: `SaasTenantTransferGuard` (suspend, restore, cache invalidation),
registered in `Program.cs` per the explicit-registration rule. Integration tests: round trip
between two provisioned tenant databases, 403 while suspended, status and reason restored,
feature gate off for the Free tier. No migration is needed (the `DataExport` tier rows exist).

**Phase D — community**: integration tests only (control-plane tables survive, placeholders
are disabled and have no membership, a SaaS-shaped archive passes the gate). A new section
in `release/docs/OPERATIONS.md` (procedural STE): backup first, maintenance window, the size
variable, archive sensitivity, and `client_max_body_size` in `release/nginx/community.conf.example`.

**Phase E — infra**: runbook `docs/runbooks/tenant-transfer.md` (procedural STE), linked
from `docs/60-backups.md`. Raise `client_max_body_size` and `proxy_read_timeout` for the
import route on the SaaS ingress. Set the size variable on the API containers.

## 12. Decisions and open questions

1. **Feature key.** This spec reuses `FeatureKeys.DataExport` ("Data export / import", paid
   tiers since SaaS migration 2210). A separate key would need tier rows, an `Enforced`
   entry, `plans.ts` and plan-matrix changes. The product owner must confirm the reuse, and
   whether the Free tier keeps export-only access in some form.
2. **AI tables.** `ai_conversations`, `ai_user_allowances` and `ai_daily_limits` travel as
   user content and configuration. `ai_usage` and `ai_daily_usage` do not. Confirm.
3. **Replica window.** Suspension blocks other API replicas only after their resolver cache
   expires. The current single-process deployment makes this window zero. Revisit before a
   multi-replica deployment.
4. **Botched-import recovery on SaaS.** The transaction protects the data path. Recovery
   from a committed but unwanted import uses the operator backups
   (`orkyo-infra/docs/60-backups.md`), not this feature.
