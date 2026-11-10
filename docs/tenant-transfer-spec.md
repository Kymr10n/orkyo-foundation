# Tenant data transfer — specification and implementation plan

Status: **phases A and B (foundation backend and frontend) implemented**. Phases C to E are open. First draft 2026-07-15. Final revision 2026-11-10,
on branch `claude/tenant-db-export-import-y8wytv`. The first draft is archived at
`requirements/archive/superseded-docs/tenant-transfer-plan.md`. The git history of this file
records the corrections from the design reviews.

This feature moves a complete tenant dataset between Orkyo installations. A tenant admin
downloads the dataset as one archive. The admin imports the archive into a tenant of either
edition. The import erases the target data and loads the archive.

An archive loads only into an installation with the same schema version (section 4). The
feature therefore does not replace the operator backups.

Product decisions:

| Decision | Value |
|---|---|
| Directions | Community → SaaS and SaaS → Community. The archive format is edition-neutral. |
| Merge | None. The import erases the target tenant data first. One transaction makes this atomic. |
| Authorization | Tenant admin, in the Administration area (`RequireAdminArea()`). Every plan has the feature. |
| Identity | Keycloak accounts and control-plane memberships do not travel. Section 8 gives the rules. |
| Execution | Synchronous streaming endpoints. No job queue. A configured size ceiling applies. |
| Version gate | Equal schema version. Section 4 gives the algorithm. |
| Concurrency | One table lock inside the import transaction. The tenant is not suspended. |
| AI data | No AI table travels. |
| Tenant settings | `tenant_settings` stays on the target. `scheduling_settings` travels. |

## 1. Scope

In scope: export of all tenant domain data as one archive, validation of an archive against
a target, destructive import, the Administration UI for both, and operator documentation.

Out of scope:

- Keycloak accounts and credentials.
- Control-plane data: `tenants`, `tenant_memberships`, `user_identities`, control-plane
  `feedback`, subscriptions, and quota limits.
- Scheduled or automatic backups.
- Partial imports and imports that merge data.
- The semantic JSON export (`ExportService`). It stays as a separate feature with no importer.

## 2. Archive format (version 1)

The archive is a ZIP file, name `orkyo-transfer-<slug>-<yyyyMMdd-HHmmss>.orkyoexport`,
content type `application/zip`. The export writes the archive to a temporary file and then
sends the file. A failure during the export therefore gives an error response, not an
incomplete download.

Entries: `manifest.json`, `users.ndjson`, and one `tables/<table>.ndjson` per `Travels`
table. The manifest holds these fields:

- The format version, the source edition, and the app version.
- The export time, the source tenant id, and the source slug.
- The schema version: the sorted migration ids of section 4.
- The row count of each table.
- The list of encrypted columns.

Row encoding: one JSON object per line. PostgreSQL converts the values in both directions.
The export reads each row with `to_jsonb`. The import inserts batches of rows with
`jsonb_populate_recordset`. The application code therefore has no type table. A `bytea`
value travels in the PostgreSQL hex form (`\x…`). The tenant schema has 11 column types and
no arrays, enums, ranges, sequences or generated columns.

NDJSON is necessary because the import transforms rows: user-id remap, `tenant_id` rewrite,
and re-encryption. One batch holds 500 rows or 8 MiB of text, and is one `INSERT`.

The archive is external input. These rules apply to the reader:

- The archive must hold exactly the entries of the catalog. A different entry list causes
  rejection.
- Table names come from the catalog. Column names come from the live target schema. The
  import never builds SQL from archive text.
- A row whose keys differ from the target columns causes rejection.
- A row that names a user that the archive does not contain causes rejection.
- The reader counts the uncompressed data. A total of more than ten times the size
  ceiling causes rejection. This rule stops a decompression bomb.
- A row of more than 64 MiB of text causes rejection.

The export reads all tables in one `REPEATABLE READ` transaction. For every text value, the
export calls `IEncryptionService.IsProtected`. A protected value is decrypted with the source
tenant id and its column is recorded in the manifest. The import encrypts every value of a
recorded column with the target tenant id. Envelopes are self-describing, so this detection
does not need a hand-written column list. For `assets`, the import also sets `enc_algorithm`
and `enc_key_version` from the target encryption service. `size_bytes` and `checksum_sha256`
describe the plain text and do not change.

The archive therefore holds plain text, including floorplan images. The UI and the
documentation state that the archive is as sensitive as a database dump.

The `users` entry exports only `id`, `email`, `display_name` and `created_at`. Keycloak and
lifecycle columns never travel.

## 3. Table catalog

`TenantTransferTables` in foundation classifies **every** tenant-phase base table. The
import generates the lock statement, the erase statements and the load order from this
catalog. There is no second list.

Each entry has one class:

| Class | Export | Import |
|---|---|---|
| `Travels` | Exported. | Target rows erased, archive rows loaded. |
| `Merged` | Exported (only `users`). | Target rows stay. Section 8 gives the merge. |
| `Kept` | Not exported. | Target rows stay. |
| `Erased` | Not exported. | Target rows erased, nothing loaded. |

Tables that do not travel, with reasons. Every other table has class `Travels`.

| Table | Class | Reason |
|---|---|---|
| `orkyo_schema_migrations` | `Kept` | Journal. Travels as manifest metadata only. |
| `audit_events` | `Kept` | The target audit history must survive an import. |
| `user_preferences` | `Kept` | Preferences belong to the target account. |
| `tenant_settings` | `Kept` | Security limits, branding and terms text. An import skips the settings validator. |
| `ai_credentials` | `Kept` | Workspace API keys are secrets. |
| `ai_usage`, `ai_daily_usage` | `Kept` | Usage accounting of the target. An import must not reset it. |
| `ai_conversations` | `Kept` | Private user content. It must not appear in an admin download. |
| `ai_user_allowances`, `ai_daily_limits` | `Kept` | AI configuration belongs with the target API key. |
| `calendar_feed_tokens` | `Erased` | Token hashes are bearer credentials. Users mint new feed URLs after the import. |
| `search_documents` | `Erased` | Derived. The search triggers rebuild it during the load. |

The catalog also holds the user-id columns of each table. Five columns of `Travels` tables
reference `users.id` with a foreign key. `resource_scan_codes.created_by_user_id` references
it without one, on purpose. The import remaps these six columns (section 8).

Three tables need a special load order. The catalog records one column for each:

| Table and column | Problem | Treatment |
|---|---|---|
| `list_definitions.display_column_id` | A definition names one of its own columns. | Deferred column: loaded as `NULL`, then set with one `UPDATE` after the load. |
| `list_instances.field_id` | A lookup field names a shared instance, and a resource instance names its field. Check constraints forbid a `NULL` load. | Late rows: rows without a field load first, rows with a field load after `resource_custom_fields`. |
| `requests.parent_request_id` | A request names its parent request. | Parent column: the export writes parents before children. |

The `UPDATE` of the deferred column fires the `updated_at` trigger. The `updated_at` value
of a list definition with a display column is therefore the import time.

A conformance test (`TenantTransferTableConformanceTests`) compares the catalog against a
freshly migrated tenant database. It asserts these facts:

- The catalog names exactly the live base tables.
- Without the deferred and late columns, the load order is a valid foreign-key topological
  order.
- Late rows load after the table that they reference, and before every other table that
  references their table.
- Every deferred, late and parent column is nullable, on a table with the primary key `id`.
- No sequences and no generated columns exist. Only `assets` has a `tenant_id` column.
- A `Travels` table references only `Travels` tables and `users`.
- No `Kept` table has a foreign key to a `Travels` or `Erased` table.
- The user-column list equals the `Travels` columns that reference `users.id`. The test
  finds these columns by constraint and by the `*user_id*` name pattern.

The implementation plan puts this test before the catalog, so the catalog is correct by
construction.

## 4. Version gate

The import loads rows. It does not run migrations. The archive and the target must
therefore have the same schema version.

The schema version is the set of tenant-phase migration ids of the foundation module.
Scripts with `MigrationScope.TenantDatabaseOnly` (the feedback pair) are not part of the
set, because Community does not run them. Scripts of other modules (for example
`community`) are not part of the set, because those migrations create no tables and no
columns.

The gate reads the code-known set from the `IMigrationModule` set of the API process. Both
editions register their modules there. The gate has two more inputs: the journal of the
tenant database, and the schema version in the manifest.

1. The tenant journal must contain the code-known set. A database that is behind is
   rejected with error `transfer_schema_behind` and the message "run the migrator first".
   This check applies to the export and to the import.
2. The archive schema version must equal the code-known set. A different set is rejected
   with error `transfer_version_mismatch`. If the archive is older, the message is "upgrade
   the source installation and export again". If the archive is newer, the message is
   "upgrade this installation first".

The gate does not accept an older archive, for this reason. A migration class header
describes rollback safety. It does not show that a migration is safe to skip. Migrations
1720, 1750 and 1990 have class `expand`, and each one also writes rows. An older archive
that skips such a migration loads wrong data or fails a `NOT NULL` constraint.

A release without a tenant-phase foundation migration does not change the schema version.
An archive stays valid until the next such migration.

## 5. Import sequence

The endpoint copies the request body to a temporary file, reads the manifest, and runs the
gate. `POST /import/validate` stops here and returns the result. `POST /import` continues in
one transaction on the tenant connection. The app role has the `SELECT`, `INSERT`, `UPDATE`
and `DELETE` grants that the import needs.

1. Lock. `LOCK TABLE … IN EXCLUSIVE MODE` on `users` and on every `Travels` and `Erased`
   table, with a `lock_timeout` of 10 seconds. This mode blocks writers and a second import.
   Readers continue and see the old data until the commit. A lock timeout returns 409 with
   error `transfer_busy`.
2. Read the quota usage of the target (section 6).
3. Erase. `DELETE` from every `Erased` table, then from every `Travels` table in reverse
   load order. In this order, the foreign keys of the two cycles cascade or set `NULL`.
4. Merge the users (section 8).
5. Load each table in catalog order, in batches, with the row transforms (user-id remap,
   `assets.tenant_id` rewrite, re-encryption). The late rows of a table load after the table
   that they reference.
6. Set the deferred column from the archive values.
7. Compare the per-table row counts against the manifest. A mismatch throws.
8. Read the quota usage again and apply the quota limits (section 6). Commit.

Any failure before the commit rolls back completely. The target keeps its data. A client
disconnect cancels through the `CancellationToken` and rolls back. A test asserts this.
The endpoint deletes the temporary file in all cases.

After the commit, the import records the quota deltas and the audit event
`transfer.imported` through the tenant audit seam (`ITenantUserService.RecordAuditEventAsync`).
The event holds the source edition, app version, row count, matched users and placeholder
count. The export records `transfer.exported`. Both names go into `TenantAuditActions`.

The erase uses `DELETE`, not `TRUNCATE`, for three reasons. The app role has no `TRUNCATE`
grant, and `CreateMaintenanceConnectionForDatabase` is reserved for provisioning and purge.
`TRUNCATE` is not MVCC-safe, so an export during an import can read empty tables. `CASCADE`
erases tables that the catalog does not name.

The import does not suspend the tenant. The table lock gives the same protection inside the
transaction, and a stopped API process cannot leave a tenant suspended. During an import,
a write from another user waits for the lock. A write that waits longer than its command
timeout fails. The confirmation dialog states this.

The server does not create a safety copy. The single transaction is the safety mechanism.
The confirmation dialog offers an export of the current data.

## 6. Quotas

An import writes rows without the service layer, so the import applies the plan quotas
itself. Three quota keys count tenant data: `production_sites`, `spaces` and `storage_bytes`.
`active_seats` counts control-plane memberships, which an import does not change.

The import reads the three usage values on its own connection, before the erase and after
the load. The SQL text is shared with `SiteRepository.GetCountAsync`,
`ResourceRepository.GetPlaceableCountAsync` and `AssetRepository.GetTotalSizeBytesAsync`.
There is one query text for each value, with two callers.

Before the commit, the import calls `IQuotaEnforcer.EnsureWithinLimitAsync` for each key
whose usage increases. A value above the limit rolls back and returns the existing
`quota_exceeded` error. An import that does not increase a usage is not checked for that
key, which agrees with the service layer. After the commit, the import calls
`IQuotaUsageRollup.RecordDeltaAsync` with the difference of each key.

`POST /import/validate` does not report a quota blocker, because the usage is known only
after the load. Community uses the no-op enforcer, so the quota steps have no effect there.

## 7. API

| Route | Result |
|---|---|
| `GET /api/admin/transfer/export` | 200, `application/zip`, streamed. |
| `POST /api/admin/transfer/import/validate` | 200 with blockers, warnings, user-match preview, table counts. |
| `POST /api/admin/transfer/import?confirmation=<slug>` | 200 with the import result. |

The group has the shape of `ExportEndpoints.cs`: `/api/admin/transfer`,
`.RequireAuthorization()`, `.RequireAdminArea()`, and the rate-limit policy
`FoundationRateLimitPolicies.AdminOperations`. No route has a feature gate. The POST routes
add `.DenyLockedAccount()`.

The POST routes take the archive as the raw request body (`application/zip`). They do not
use multipart, because the form reader has a second size limit of 128 MB. The handler
compares `confirmation` against `OrgContext.OrgSlug`.

Status codes:

| Case | Status | Error code |
|---|---|---|
| Database behind, or different schema version | 422 | `transfer_schema_behind`, `transfer_version_mismatch` |
| Archive that breaks a reader rule | 422 | `transfer_archive_invalid` |
| Quota above the limit | Existing mapping | `quota_exceeded` |
| Lock timeout | 409 | `transfer_busy` |
| Wrong confirmation | 400 | |
| Upload above the size ceiling | 413 | `transfer_archive_too_large` |
| Size ceiling not set | 503 | `transfer_not_configured` |

Errors use `application/problem+json`. `TransferRejectedException` carries the status and
the code, and `AppExceptionHandler` maps it. The codes are in `ApiErrorCodes`.

The size ceiling is `ORKYO_TRANSFER_MAX_ARCHIVE_BYTES`. The value has no default in code.
The env templates carry the value, per the no-fallback convention. If the variable is not
set, the POST routes return 503 and the export continues to work. A value that is not a
positive integer stops the application at startup.

Both products cap Kestrel at 10 MB globally. The POST routes remove that cap for the
request and count the bytes during the copy to the temporary file. There is thus one
limit, and an upload above it returns 413 with the transfer error code.

The SaaS ingress sets two hard limits, because Cloudflare proxies the tenant hosts:

- Cloudflare rejects a request body larger than 100 MB on the plan of the `orkyo.com` zone.
  The SaaS env templates therefore set the size ceiling to 90 MiB (`94371840`). The
  Community template sets 1 GiB (`1073741824`).
- If the origin sends no response for 100 seconds, Cloudflare ends the request with status
  524. The import then sees a client disconnect and rolls back. The target keeps its data.

## 8. Identity

The tenant `users` table is merged, never erased. An imported user is mapped to a target
user in this order: first by equal id, then by equal email (case-insensitive). The id match
covers an import into the source installation after a user changed the email. Every user-id
column in the catalog is remapped through this map during the load.

An imported user without a match is inserted under its imported id as an inactive
placeholder. The insert follows the `ON CONFLICT` convention from `docs/conventions.md`.
A placeholder has these values:

- Email `transfer-placeholder+<id>@invalid.orkyo`, with the id as 32 hex digits.
- The imported display name. If the name is empty, the original email is the display name.
- `keycloak_id` null.
- `keycloak_metadata` = `{"transfer_placeholder": true, "original_email": "<email>"}`.
- `status='disabled'` where the column exists.

In Community, `users` is also the account table, so the `status` column exists there.
Placeholders keep history readable. They give no access: Community is invitation-only since
migration 3030, and SaaS access lives in the control plane, which the import never touches.

A placeholder is permanent. A person who joins the target after the import gets a new user
row, and the import history stays with the placeholder. The validation result therefore
lists the emails without a match. The admin can invite those users before the import, and
the import then maps them.

## 9. Security

- Export and import require the Admin role. Both are audited.
- The import runs with the app role. No request uses the maintenance connection.
- The archive is external input. No SQL text comes from the archive, and the reader bounds
  the uncompressed size (section 2).
- An import skips the request validators. Database constraints are the only row check. A
  crafted archive can therefore damage only the tenant that imports it. The tables with an
  effect outside the tenant data do not travel, and the quotas apply (section 6).
- The archive holds plain text, floorplans included. The UI says so at download time.
  Passphrase encryption of the archive was considered and rejected for complexity.
- Bearer credentials, API keys and private AI conversations never travel.
- The target audit history, settings and AI usage accounting survive an import.
- The import is wrapped in one transaction with one table lock. The confirmation value must
  equal the tenant slug.

## 10. User interface

A new tab "Data Transfer" (route `data-transfer`) in `TenantAdminPage.tsx`, lazy-routed in
`TenantApp.tsx` under `RequireTenantAdmin`. Every tenant admin sees the tab. The existing
semantic Export card in `OrganizationSettings.tsx` stays where it is.

The export card downloads through `apiRawFetch(...).blob()`, with the file name from the
server. It states that the archive is not encrypted.

The import card has these stages:

1. File selection.
2. Automatic validation, with the upload progress.
3. A summary: source workspace, edition, version, export time, row count, matched users,
   blockers, and the emails without a match.
4. A confirmation with `ConfirmDialog` and its `confirmPhrase`, which is the slug.
5. The import, with the upload progress.
6. A result message with a "Reload now" button, which calls `window.location.reload()`.

The upload sends the file as the request body with XMLHttpRequest, because `fetch` gives no
upload progress. A failure goes through `handleApiError`, so it is an `ApiError` with the
server code. The three mutations use `meta: { errorMessage }`. The toast description is the
reason from the server. The confirmation states that the import erases all current data,
and that other users cannot save changes during the import.

## 11. Implementation plan

Each step is one pull request. Every code change ships with ≥80% patch coverage, measured
with `scripts/ci/patch-coverage.sh` (foundation) or the product `check` scripts, per the
CLAUDE.md of each repo. Commits carry a `Docs-impact:` trailer. New endpoint groups get a
`RouteInventoryTests` entry in both products. `./scripts/test-downstream.sh` runs before the
foundation PR merges.

**Phase A — foundation backend** (`backend/core/Services/Transfer/`, the repository in
`backend/core/Repositories/`, endpoints in `backend/src/Endpoints/TransferEndpoints.cs`):

1. `TenantTransferTableConformanceTests`, then `TenantTransferTables`.
2. `TenantArchive`: the entry names, the writer, and the reader with its bounds.
3. `TenantTransferVersionGate`. `Orkyo.Foundation.Core` references
   `Orkyo.Migration.Abstractions` for the `IMigrationModule` contract.
4. `TransferUserMatcher`.
5. `TenantTransferRepository` and `TenantTransferSession`. They hold all SQL, because
   `ConventionContractTests` forbids SQL in services. The three usage queries are shared
   constants of the site, resource and asset repositories.
6. `TenantTransferService`, a concrete class without an interface.
7. `TransferEndpoints`, with wiring in `FoundationEndpointExtensions` and
   `FoundationServiceExtensions`.

Tests:

- Against two tenant databases, with the demo seed as the source data: round trip of every
  `Travels` table, the user merge, re-encryption, the `Kept` and `Erased` tables, and the
  search index.
- The gate: database behind, archive older, archive newer, archive different.
- The reader rules, each with a check that the target did not change.
- Rollback after a cancel, and rollback after a quota above the limit.
- A consistent export during an import that is not committed, and 409 for a second writer.
- The routes: admin only, the download, the size ceiling, and an import of the own archive.

**Phase B — foundation frontend**: `api-paths.ts` entries, `transfer-api.ts`,
`useTenantTransfer.ts`, the `DataTransferSettings` tab, the route, and vitest tests for the
API module and for the import stages. The user documentation is
`orkyo-documentation/src/content/docs/administration/data-transfer.md`.

**Release step**: foundation version bump. Products reference only the pinned package, so
phases C and D start after the auto-bump PRs land.

**Phase C — saas**: integration tests only. Round trip between two provisioned tenant
databases with the app role, the quota rollback, and the rollup deltas. No production code
and no migration are necessary.

**Phase D — community**: integration tests (control-plane tables survive, placeholders are
disabled and have no membership, a SaaS archive of the same schema version passes the
gate). The size variable goes into `release/compose.yml` and `release/generate-env.sh`. The
import routes get a larger `client_max_body_size` in `release/nginx/community.conf.example`.
A new section in `release/docs/OPERATIONS.md` (procedural STE) gives the backup, the
maintenance window, the size variable, and the archive sensitivity.

**Phase E — infra**:

- The size variable goes into `env/*.env.template`, into the backend environment in
  `compose/override.slot.yml`, and into the catalog `docs/deploy-env-vars.md`.
- `nginx/snippets/app-routing.conf` gets one exact-match location for each import route,
  after the pattern of `/api/ai/chat`. The location sets `client_max_body_size` to 100 MB
  and `proxy_read_timeout` to 100 seconds.
- Runbook `docs/runbooks/tenant-transfer.md` (procedural STE), linked from `docs/60-backups.md`.

## 12. Decisions and open questions

Decided:

1. **Plans (2026-11-10).** Every plan can export and import. The routes have no feature
   gate, and `FeatureKeys.DataExport` stays with the semantic JSON export only.
2. **AI data (2026-11-10).** No AI table travels.
3. **Tenant settings (2026-11-10).** `tenant_settings` stays on the target.
4. **Older archives (2026-11-09).** The gate accepts only the same schema version. Support
   for older archives needs a marker on every migration that shows it is safe to skip. That
   marker is a second classification to maintain, so it is not part of version 1.
5. **Tenant suspension (2026-11-09).** The import does not suspend the tenant. The table
   lock replaces the suspension window and the advisory lock of the earlier revision.
6. **Size ceiling (2026-11-10).** 90 MiB on SaaS, for the Cloudflare Free or Pro plan.

Open:

1. **Large tenants on SaaS.** The Cloudflare limits in section 7 bound the archive size and
   the import time. A tenant above these limits cannot import through the UI. Version 1 has
   no operator path for this case.
2. **Botched-import recovery on SaaS.** The transaction protects the data path. Recovery
   from a committed but unwanted import uses the operator backups
   (`orkyo-infra/docs/60-backups.md`), not this feature.
