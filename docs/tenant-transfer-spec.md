# Tenant data transfer — specification and implementation plan

Status: **phases A and B (foundation backend and frontend) implemented**. Phases C to E are open. Section 13 (import progress) is specified and not implemented. First draft 2026-07-15. Final revision 2026-11-10,
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

Entries: `manifest.json`, `users.ndjson`, one `tables/<table>.ndjson` per `Travels`
table, and one `blobs/<table>/<row id>` per binary value. The manifest holds these fields:

- The format version, the source edition, and the app version.
- The export time, the source tenant id, and the source slug.
- The schema version: the sorted migration ids of section 4.
- The row count of each table.

Row encoding: one JSON object per line. PostgreSQL converts the values in both directions.
The export reads each row with `to_jsonb`. The import inserts batches of rows with
`jsonb_populate_recordset`. The application code therefore has no type table. The tenant
schema has 11 column types and no arrays, enums, ranges, sequences or generated columns.

A binary value does not travel in its row. The catalog names the binary column of a table
(`assets.data`). The row omits this column, and the value is a separate entry of bytes. A
row with a binary value loads in a batch of one row. A measurement gave the reason: a
floorplan of 10 MiB as text in the row used 760 MiB of memory during an import. As a
separate entry, the same floorplan uses 75 MiB.

NDJSON is necessary because the import transforms rows: user-id remap, `tenant_id` rewrite,
and re-encryption. One batch holds 500 rows or 8 MiB of text, and is one `INSERT`.

The archive is external input. These rules apply to the reader:

- The archive must hold exactly the entries of the catalog, and binary values. A different
  entry list causes rejection. A binary value that no row names causes rejection.
- Table names come from the catalog. Column names come from the live target schema. The
  import never builds SQL from archive text.
- A row whose keys differ from the target columns causes rejection.
- A row that names a user that the archive does not contain causes rejection.
- The reader counts the uncompressed data. A total of more than ten times the size
  ceiling causes rejection. This rule stops a decompression bomb.
- A row of more than 4 MiB of text causes rejection. A binary value of more than 64 MiB
  causes rejection.

The export reads all tables in one `REPEATABLE READ` transaction. The catalog names the
columns that the application encrypts: `sites.description`, `sites.address`,
`resources.notes` and `assets.data`. The export decrypts the values of these columns with
the source tenant id. The import encrypts them with the target tenant id. No other column
is read as an envelope. Text in a different column that starts with the envelope prefix
therefore travels unchanged. A new encrypted column must be added to the catalog. For
`assets`, the import also sets `enc_algorithm`
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

## 13. Import progress

Status: specified 2026-11-12, not implemented. Sections 5, 7 and 10 describe the current
behavior. The implementation of this section updates them.

### 13.1 Problem and goal

The import card shows the upload percentage only. After the upload, the server loads the
archive in one request and sends one response at the end. The admin sees no change for up
to a minute.

The goal is one progress bar with the name of the current step. The bar shows a measured
percentage where a measurement exists. It shows no invented percentage.

This section does not change the import transaction, the archive format, or the checks.

### 13.2 Design decisions

| Decision | Value | Reason |
|---|---|---|
| Transport | Server-Sent Events on the response of `POST /import` | WHATWG standard. `/api/ai/chat` uses the same mechanism through the same proxies. |
| Background job with a status route | Rejected | It needs a job identifier, stored state and a second route for a request of about one minute. |
| WebSocket | Rejected | The stream has one direction. The application has no WebSocket infrastructure. |
| Error format | RFC 9457 problem document, as today | One error contract before and during the stream. |
| Validation route | Unchanged, JSON | It reads the manifest and the users only. It has no long step. |
| Progress unit | Rows loaded of the manifest row total | The manifest gives the total before the load starts. |
| Keep-alive | SSE comment every 15 seconds | The proxies measure idle time. The comment resets it. |

### 13.3 Steps

The client knows the first two steps. The server reports the other three.

| Step | Source | Bar | Label |
|---|---|---|---|
| `upload` | Upload progress of the browser | Bytes sent of the file size | "Uploading: 40%" |
| `check` | The upload is complete and no event is there | Indeterminate | "Checking the archive" |
| `erase` | Server event | Indeterminate | "Removing the current data" |
| `load` | Server event | Rows loaded of the row total | "Loading the data: 40%" |
| `finish` | Server event | Indeterminate | "Finishing" |

The step `erase` starts after the lock (section 5, step 1). It includes the user merge.
The step `load` is section 5, step 5. The step `finish` includes the deferred column, the
row count comparison, the quota check, the commit, the quota deltas and the audit event.

The bar shows one step at a time. It does not combine the steps into one total percentage,
because the durations of the steps have no fixed ratio.

### 13.4 Wire contract

The response of `POST /api/admin/transfer/import` changes. The request does not change.

Before the first event, the response is the same as today. Each of these checks fails with
its HTTP status and a problem document: the size ceiling, the confirmation, the manifest,
the version gate and the lock. Section 7 gives the codes.

After the lock, the response has status 200 and the content type `text/event-stream`. It
carries these events:

| Event | Data | Count |
|---|---|---|
| `progress` | `{ "step": "erase" }`, `{ "step": "load", "percent": 40 }`, `{ "step": "finish" }` | Many |
| `result` | The `TransferImportResult` of today | One, last |
| `error` | The problem document, with its `status` | One, last |

Exactly one of `result` and `error` ends the stream. A stream that ends without one of them
is a lost connection. The transaction then rolls back, as today.

An `error` event means that the import did not change the data. The server sends `result`
only after the commit.

When the whole percentage changes, the server sends a `load` event. One import thus sends
100 `load` events at most.

When the stream is silent for 15 seconds, the server sends a comment line (`: keep-alive`).

### 13.5 Backend

**Shared SSE helper.** The functions `WriteEventAsync`, `WriteHeartbeatAsync` and
`StreamWithHeartbeatAsync` are private parts of `AiChatEndpoints` today. They move to one
static class `ServerSentEvents` in `backend/src/Helpers/`. The class also sets the three
response headers (`Content-Type`, `Cache-Control: no-cache`, `X-Accel-Buffering: no`).
`StreamWithHeartbeatAsync` becomes generic in the event type and takes the function that
writes one event. `AiChatEndpoints` uses the class. Its behavior and its tests do not change.

**Service.** `TenantTransferService.ImportAsync` gets one optional parameter,
`Action<TransferProgress>? report`. `TransferProgress` is a record with `Step` and a nullable
`Percent`, in `backend/core/Models/Transfer.cs`. The service calls `report` at the start of
each server step and after each batch of the load. It holds the last reported percentage
and skips a call that does not change it. The row total is the sum of `manifest.Tables`.
The service has no knowledge of HTTP. A caller without `report` gets the behavior of today.

**Endpoint.** The handler of `POST /import` does this sequence:

1. It receives the archive, as today.
2. It creates an unbounded `Channel<TransferProgress>` and starts `ImportAsync`. The
   `report` function writes to the channel. When the import task ends, the handler
   completes the channel.
3. It waits for the first item of the channel. If the channel completes with no item, the
   handler awaits the task. The exception goes to `AppExceptionHandler`, and the response
   has the HTTP status of today.
4. With the first item, it starts the stream through `ServerSentEvents`. It writes each
   item as a `progress` event, with the keep-alive.
5. It awaits the task. It writes `result`, or it writes `error`.

Only the handler writes to the response, so the stream needs no lock. The handler always
awaits the import task before it returns, so the request scope outlives the import.

**Errors during the stream.** `AppExceptionHandler.Map` is the one place that maps an
exception to a problem document. It becomes `internal`, and the handler calls it for the
`error` event. A cancelled request writes nothing, because no client is there. The
`TransferRejectedException` and the quota exception thus have the same code and the same
message before and during the stream.

**OpenAPI.** The route declares `text/event-stream` for status 200.

### 13.6 Frontend

**Shared SSE reader.** `ai-api.ts` holds the code that splits a stream into events. That
code moves to `lib/core/sse.ts` as one function. The function takes the buffered text and
returns the complete events (`name`, `data`) and the remaining text. `streamAiChat` uses
the function.

**Upload helper.** `uploadWithProgress` in `lib/core/upload.ts` gets one option,
`onEvent(name, data)`. The request stays an XMLHttpRequest, because `fetch` gives no upload
progress. If the response has the content type `text/event-stream`, the helper reads
`responseText` in each `progress` event of the request. It gives the new events to
`onEvent`. The promise resolves with the data of the `result` event. An `error` event goes
through `handleApiError`, with a `Response` that the helper builds from the problem
document. The caller thus gets the same `ApiError` as for an HTTP error. A stream with no
last event rejects with the message for a lost connection. A JSON response behaves as today.

**API and hook.** `transfer-api.ts` defines one type:

```ts
export type TransferProgress =
  | { step: 'upload'; percent: number }
  | { step: 'check' | 'erase' | 'finish' }
  | { step: 'load'; percent: number };
```

`importTenantArchive` and `validateTenantArchive` take `onProgress(progress: TransferProgress)`.
They report `upload` from the upload percentage, `check` at 100 percent, and the server
steps from the `progress` events. `useTenantTransfer.ts` passes the callback through.

**Component.** `DataTransferSettings` holds one `TransferProgress` state in place of the
number. A small `TransferProgressBar` in the same file shows the label of section 13.3 and
a native `<progress>` element. The element has `value` and `max` for a step with a
percentage, and no `value` for an indeterminate step. The native element gives the
`progressbar` role and needs no new dependency. The label stays in the `role="status"`
paragraph of today. The validation and the import use the same bar.

### 13.7 Ingress

The stream removes the time limit of section 7. Cloudflare and nginx measure the time
without data, and the events and the keep-alive reset that time. The size limit of 100 MB
stays.

- The import locations in `orkyo-infra/nginx/snippets/app-routing.conf` need no new
  directive. The response carries `X-Accel-Buffering: no`, and `text/event-stream` is not in
  `gzip_types`. The comment of the locations changes, because the 100 seconds are now idle
  time.
- `orkyo-infra/docs/runbooks/tenant-transfer.md` changes its "Import time" row and its
  status 524 row.
- The Community example `release/nginx/community.conf.example` (phase D) needs no buffering
  directive for the same reason.
- Open question 1 of section 12 keeps the size part only.

### 13.8 Tests

Backend:

- `ServerSentEvents`: the existing heartbeat tests of the AI endpoint move with the code.
- The service reports `erase`, then `load` with a percentage that only increases, then
  `finish`. The last `load` event has 100 percent. An archive with no rows reports no
  `load` percentage below 100.
- The route, on the Kestrel host of `TransferArchiveUploadKestrelTests`: an import of the
  own archive returns `text/event-stream` with `progress` events and one `result`.
- Each check of section 13.4 returns its HTTP status and a problem document, not a stream.
- A failure after the lock (a row count that does not match the manifest, a quota above
  the limit) returns status 200 with one `error` event. The target data does not change.
- A cancel during the stream rolls back. The existing test covers the service. A new test
  covers the route.

Frontend:

- `sse.ts`: complete events, an event split across two chunks, a comment line.
- `upload.ts`: a JSON response as today, a stream with `result`, a stream with `error`
  as an `ApiError`, a stream with no last event.
- `DataTransferSettings`: the label and the bar for each step, and the bar for validation.

### 13.9 Implementation order

The work goes on the open branches, with the rules of section 11.

1. Backend, on `claude/tenant-transfer-backend` (#321): `ServerSentEvents` with the move
   of the AI code, then the `report` parameter, then the endpoint.
2. Frontend, on `claude/tenant-transfer-frontend` (#322): `sse.ts` with the move of the AI
   code, then the `onEvent` option, then the API, the hook and the bar. Sections 5, 7, 10
   and 12 of this file change in the same pull request.
3. Infra (#83): the comment and the runbook rows of section 13.7.
4. Documentation (#42): the import stages in `data-transfer.md`.

### 13.10 Out of scope

- Progress of the export. The browser shows the download.
- Progress inside the validation, after the upload.
- A cancel button. A closed tab cancels the import, as today.
- A resume after a lost connection. The import starts again from the file.
