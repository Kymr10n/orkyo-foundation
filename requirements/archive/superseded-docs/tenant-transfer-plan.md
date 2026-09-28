# Tenant database export/import ("Data transfer") — spec & implementation plan

> Status: **approved plan, not yet implemented.** This document is the executable spec for an
> implementing agent/engineer. Work happens on branch `claude/tenant-db-export-import-y8wytv`
> in all four repos (foundation, saas, community, infra).

## Context

Orkyo ships in two editions: self-hosted single-tenant **Community** and hosted multi-tenant
**SaaS**. There is currently no supported way to move an organisation's data between them —
onboarding a self-hoster to SaaS (or off-boarding a SaaS tenant to self-host) requires manual DB
surgery. This feature adds a tenant-admin self-service export of the complete tenant dataset as a
downloadable archive, and a wipe-and-replace import of such an archive into a tenant of either
edition.

**Product decisions (confirmed with the product owner):**

- **Bidirectional**: Community → SaaS and SaaS → Community.
- **No merge**: import wipes the target tenant's domain data and replaces it. Explicit warning +
  type-the-slug confirmation.
- **Auth**: tenant-admin self-service in the tenant Administration area (`RequireAdminArea()`),
  feature-gated via `IFeatureGate`.
- **Identity**: Keycloak accounts and control-plane memberships do NOT travel. Imported tenant-DB
  user rows are re-linked to target users **by email**; unmatched users become inactive
  placeholders so historical references stay intact.
- **Execution**: synchronous streaming endpoints, no job queue. Documented size ceiling
  (default 1 GiB, configurable).
- **Version gate — class-based** (confirmed deviation from a naive "restore then migrate
  forward"): archives are loaded into the target's already-current schema, so gap migrations can't
  literally re-run. Accept archives whose version gap contains only `none`/`expand` migrations;
  **reject** if the gap contains `data`/`contract` migrations with "archive too old — upgrade the
  source installation and re-export". Never silently corrupts data.

## Why a raw pg_dump is not the transfer format (verified against the code)

- SaaS is database-per-tenant; Community is ONE Postgres DB where control-plane and tenant
  migration phases both apply to `public` (`CommunityTenantRegistry`, both phases run against the
  same connection string).
- Community filters out foundation tenant-phase `feedback` migrations 1240/1630
  (`CommunityFoundationMigrationModule`) because control-plane `feedback` (1170) lives in the same
  schema. Verified: `1240.foundation.feedback.sql` creates and `1630.foundation.drop_feedback.sql`
  drops `public.feedback` — a net-zero pair present in SaaS tenant journals, absent from
  Community's.
- Community's `3020.community.tenant_seed.sql` installs trigger
  `community_auto_grant_membership_trigger` (AFTER INSERT ON `public.users` → inserts an admin
  `tenant_memberships` row). Bulk user inserts on import WILL fire it.
- Therefore the transfer format is a **logical, table-level export of the foundation tenant-phase
  tables** + a manifest, not a physical dump.

Other verified facts the design relies on:

- "Schema version" = the set of applied ids in `orkyo_schema_migrations` (id text PK, module,
  target_database, checksum, applied_at, applied_by_version, …) — no numeric version. Owned by
  `backend/migrator-runtime/MigrationHistory.cs`; checksums are SHA-256 of LF-normalized SQL
  (`ChecksumPolicy`).
- Blobs (site floorplans) live in-DB as `assets.data bytea` (tenant migration 1500), possibly
  envelope-encrypted with a **deployment-scoped** master key and **tenant-id-bound AAD** (1540,
  `AesGcmEncryptionService`) — export must decrypt, import must re-encrypt under the target key +
  target tenant id.
- Data access is raw Npgsql (no EF/Dapper). No job queue exists (worker = polling
  `BackgroundService`).
- Existing precedents to mirror: `ExportEndpoints.cs` (POST /api/admin/export, `RequireAdminArea`
  + `IFeatureGate FeatureKeys.DataExport`), `TenantReset.TruncateAllAsync` (FK-safe wipe
  preserving auth tables), SaaS `TenantProvisioningService` (tenant DB lifecycle),
  `FloorplanEndpoints` + `floorplan-api.ts` (multipart upload / blob download patterns).

---

## Spec

### What travels

Every foundation tenant-phase BASE table (the "transfer table set"), including `assets` blobs
(decrypted in the archive). What does NOT travel: Keycloak accounts, control-plane
`users`/`user_identities`/`tenant_memberships`, control-plane `feedback`, the
`orkyo_schema_migrations` journal (tenant-phase rows travel as manifest metadata only), views
(`request_assignments_view`, insights views from 1600).

### Archive format (format version 1)

**ZIP** (`System.IO.Compression.ZipArchive`), filename
`orkyo-transfer-<slug>-<yyyyMMdd-HHmmss>.orkyoexport`, content type `application/zip`. ZIP over
tar.gz because Create-mode ZipArchive writes to the non-seekable HTTP response stream (tar needs
entry sizes upfront). Entries:

```
manifest.json                       — written first, read first
tables/<NNN>_<table>.ndjson         — NNN = FK-order index, one JSON object per row
```

`manifest.json`: `formatVersion`, `sourceEdition` ("community"|"saas"), `appVersion` (APP_VERSION
env — same source as the migrator's `applied_by_version`), `exportedAt`, `sourceTenant {id, slug}`,
`schema.tenantMigrations` (all `orkyo_schema_migrations` rows with `target_database='Tenant'`:
id, module, checksum), `tables` (name, rowCount, columns with pg types), `assetsDecrypted: true`.

Row encoding: uuid/text/timestamptz(ISO-8601 UTC)/numeric → JSON string; bool/int → native;
jsonb → inline JSON; **bytea → base64**; NULL → null. NDJSON (not COPY passthrough) because import
must transform rows: user-id remap, `assets.tenant_id` rewrite + re-encryption, placeholder email
rewrite, column-set intersection for expand-delta archives. Import loads via
`NpgsqlBinaryImporter` (COPY FROM STDIN FORMAT BINARY), one COPY per table — fast despite JSON.

Export reads all tables in one `REPEATABLE READ` transaction (consistent snapshot). Column
metadata comes from a live `information_schema.columns` query, never hard-coded per table.

**Assets**: export decrypts rows with `enc_algorithm IS NOT NULL` via `IEncryptionService` (AAD
binds the source tenant id); the archive stores plaintext. Import re-encrypts with the target
service and **target** tenant id, rewrites `tenant_id`. Document: the archive contains plaintext
blobs — as sensitive as a DB dump.

**users entry**: exported columns limited to `id, email, display_name, created_at`. Never export
`keycloak_id`/`keycloak_metadata`/`synced_at`/lifecycle columns.

### Transfer table set — single source of truth

Static, explicitly FK-ordered list in foundation (`TenantTransferTables.cs`) with per-table flags
(`users` special: merged, never truncated). Static-in-code, not schema-derived, because (a)
Community export must exclude control-plane tables sharing `public`, (b) deterministic load order,
(c) every new table forces a conscious "does it travel?" decision. The exact current membership is
derived by the implementer from a freshly migrated tenant DB — **write the conformance test
first** (see Test plan) so the list is correct by construction. Ordering reference:
`TenantReset.cs` / `SeedRunner`. Also assert no sequences exist in the tenant schema (all PKs are
`gen_random_uuid()` today; if that changes, add a `setval` fixup step).

### Version-gate algorithm (precise)

Inputs: `archive.schema.tenantMigrations`; `known` = tenant-phase `MigrationScript`s from the
DI-registered `IEnumerable<IMigrationModule>` (product-composed set: SaaS = unfiltered foundation;
Community = feedback-filtered foundation + community module).

Foundation owns a constant **transfer-irrelevant id set**:
`{"1240.foundation.feedback", "1630.foundation.drop_feedback"}` (net-zero pair; makes
SaaS↔Community journals comparable).

1. For each archive entry: skip if in irrelevant set; skip with warning if its module isn't
   registered locally (e.g. `community` module imported into SaaS — verified schema-neutral:
   3000 placeholder / 3010 demo rows / 3020 seed+trigger create no tables; the table gate below
   independently protects); **REJECT** if id unknown to this build
   (`transfer_archive_incompatible` — newer/divergent source); **REJECT** on checksum mismatch
   (divergent history).
2. `delta` = known **foundation-module** ids − archive ids − irrelevant set.
3. If delta nonempty: parse each delta script's `-- @migration-class:` header from
   `MigrationScript.Sql`. Allow iff all ∈ {`none`, `expand`}. Any `data`/`contract` → **REJECT**
   (`transfer_archive_too_old`, message: upgrade source and re-export).
4. Table/column gate: every archive table must be in `TenantTransferTables` AND exist in the live
   target schema; archive columns missing on the target → REJECT; target columns missing from the
   archive → allowed (COPY omits them; expand guarantees nullable/default), surfaced as warnings.
5. After the data phase commits, run
   `MigrationRunner.RunAsync(tenantConn, Tenant, "orkyo:tenant:<db_identifier>")` — normally a
   no-op, but repairs a behind tenant. Must run AFTER the data transaction and after releasing the
   transfer advisory lock (DbUp takes its own connections/lock).

### Import sequence (exact)

```
0. RequireAdminArea group + EnsureEnabledAsync(FeatureKeys.TenantTransfer);
   validator: confirmation == tenant slug (slug equality checked in handler vs OrgContext —
   FluentValidation does shape only, per docs/validation.md);
   raise IHttpMaxRequestBodySizeFeature.MaxRequestBodySize from config
   (ORKYO_TRANSFER_MAX_ARCHIVE_BYTES, default 1 GiB) BEFORE reading the body
   (both products cap Kestrel globally at 10 MB); spool multipart upload to temp file.
1. Read manifest → full version-gate validation → 409 with blockers on failure.
   (POST /import/validate stops here: returns summary + user-match preview.)
2. guard = ITenantTransferGuard.BeginAsync(orgContext)
   - SaaS: set tenants.status='suspended' (remember prior), ITenantResolver.InvalidateCache(slug)
     → tenant traffic 403s during import.
   - Community: no-op (advisory lock + TRUNCATE ACCESS EXCLUSIVE locks suffice; docs advise
     a maintenance window).
3. pg_advisory_lock('orkyo:transfer:<db identifier>') on a dedicated connection
   (distinct key from the migrator's lock).
4. ONE transaction on the tenant DB:
   a. TRUNCATE all transfer tables EXCEPT users, CASCADE (mirrors TenantReset.TruncateAllAsync;
      users is only a parent — cascade never reaches it; in Community this preserves
      tenants/tenant_memberships/user_identities/feedback in the shared schema).
   b. Users merge (TransferUserRelinker):
      - match by lower(email) against target users → map importedId→targetId (target row
        untouched);
      - unmatched → INSERT placeholder under imported id:
        email = 'transfer-placeholder+<first8-of-id>@invalid.orkyo' (never collides with
        TenantUserService stub creation / community JIT email matching),
        display_name preserved, keycloak_id NULL,
        keycloak_metadata = {"transfer_placeholder": true, "original_email": "<email>"};
        Community extra: status='disabled'.
      - Community hook (ITenantTransferImportHook.AfterUsersInsertedAsync): DELETE the
        tenant_memberships rows auto-granted by community_auto_grant_membership_trigger for
        placeholder ids — inside the same transaction, so placeholders never gain admin access.
   c. Per table in list order: stream NDJSON → NpgsqlBinaryImporter COPY (archive∩target columns).
      Row transforms: every column FK-referencing users.id (discovered once via pg_constraint)
      remapped through the user map; assets: tenant_id := target OrgId,
      data := ProtectBytes(plaintext, targetOrgId), enc columns from target service.
   d. Verify per-table row counts vs manifest; mismatch → throw (rollback).
   e. COMMIT.   ← any failure before this leaves the target completely untouched.
5. Release advisory lock; run MigrationRunner.RunAsync (belt-and-braces). Failure here → 500
   with remediation ("re-run the migrator"); data is consistent.
6. finally: guard.EndAsync (SaaS: restore prior status + InvalidateCache again).
7. Audit: IAdminAuditService "tenant.transfer.import" (sourceEdition, sourceSlug, appVersion,
   rowsImported, usersMatched, placeholders). Export audits "tenant.transfer.export".
```

Domain `memberships` (tenant migration 1220, per-site roles) travel with user-id remapping — they
FK `sites` ON DELETE CASCADE so the target's rows die in the wipe anyway. `invites` travel; the
validate response warns imported invite links point at the source deployment. No server-side
auto-snapshot: single-transaction rollback covers the dangerous window; the **UI forces
downloading a fresh export before the import step unlocks**.

### API

- `GET  /api/admin/transfer/export` → 200 `application/zip`, streamed to `Response.Body`.
- `POST /api/admin/transfer/import/validate` → multipart, 200 `TransferValidationResult`
  (blockers/warnings/version delta/user-match preview/table counts).
- `POST /api/admin/transfer/import` → multipart (`file`, `confirmation`), 200
  `TransferImportResult`; 400 bad confirmation/malformed archive; 409 gate blockers
  (`transfer_archive_incompatible`/`transfer_archive_too_old` in `ApiErrorCodes`); 413 over
  ceiling.
- Both POSTs: `.DisableAntiforgery().Accepts<IFormFile>("multipart/form-data")` (mirror
  `FloorplanEndpoints.cs`); admin rate-limit policy; exactly ONE authorization convention on the
  group (`RequireAdminArea()`) — the conformance test enforces this.

---

## Implementation order & work breakdown

### Phase A — orkyo-foundation backend

New folder `backend/core/Services/Transfer/`:

1. `TenantTransferModels.cs` — manifest records, `TransferValidationResult`,
   `TransferImportResult`, `TransferImportRequest { Confirmation }`.
2. `TenantTransferTables.cs` — static ordered table set + flags.
3. `TransferSchemaInspector.cs` — live column metadata + users-FK column discovery
   (`information_schema.columns`, `pg_constraint`).
4. `TenantArchiveWriter.cs` / `TenantArchiveReader.cs` — ZIP + NDJSON codec (writer → output
   Stream; reader ← seekable Stream).
5. `TenantTransferVersionGate.cs` — gate algorithm; takes `IEnumerable<IMigrationModule>`; owns
   the irrelevant-ids constant; parses `@migration-class` headers. (Needs core →
   `Orkyo.Migration.Abstractions` reference — verify no csproj cycle; if core→`Orkyo.Migrator` is
   undesirable for `MigrationRunner`, invoke the runner from the Web-layer endpoint instead.)
6. `TransferUserRelinker.cs` — email matching + placeholder SQL; edition differences via column
   existence (inspector reports whether `users.status` exists), not edition switches.
7. `ITenantTransferService.cs` + `TenantTransferService.cs` — orchestrates export/validate/import;
   deps: `IOrgDbConnectionFactory`, `IEncryptionService`, `MigrationRunner`, modules, guard +
   hook, `IAdminAuditService`.
8. Product abstractions with no-op defaults registered in `AddFoundationServices`:
   `ITenantTransferGuard { BeginAsync(OrgContext) → IAsyncDisposable }`,
   `ITenantTransferImportHook { AfterUsersInsertedAsync(conn, tx, org, placeholderIds) }`.
9. `FeatureKeys.TenantTransfer = "tenant_transfer_enabled"`; audit action constants; `ConfigKeys`
   entry for `ORKYO_TRANSFER_MAX_ARCHIVE_BYTES`.
10. `backend/core/Validators/TransferImportRequestValidator.cs` (shape only).
11. `backend/src/Endpoints/TransferEndpoints.cs` — `MapTransferEndpoints()` per API spec above;
    wire into `FoundationEndpointExtensions.cs` + registrations into
    `FoundationServiceExtensions.cs`.

### Phase B — orkyo-foundation frontend (`@kymr10n/foundation`)

12. `lib/core/api-paths.ts` — `ADMIN.TRANSFER_EXPORT/_IMPORT_VALIDATE/_IMPORT`.
13. `lib/api/transfer-api.ts` — download via `apiRawFetch().blob()`; validate/import via XHR
    FormData with progress (both patterns from `floorplan-api.ts`).
14. `components/settings/DataTransferSettings.tsx` — Export card (download + "archive contains all
    tenant data incl. floorplans — store securely"); Import card staged flow: pick file →
    auto-validate → summary (source edition/version/date, row counts, users matched/N
    placeholders, warnings) → **Step 1: download a backup (required to unlock Step 2)** →
    type-the-slug confirm dialog → import with progress → completion dialog →
    `window.location.reload()` (honest cache strategy after full replacement). Confirm dialog is a
    "genuinely-special" dialog (exempt from `FormDialog`); import mutation still uses
    `meta: { errorMessage }` for failure toasts.
15. `hooks/useDataTransferAvailable.ts` — mirror `useAuditLogAvailable` (tier gating; backend
    enforces regardless).
16. `pages/TenantAdminPage.tsx` — add `data-transfer` tab; `components/auth/TenantApp.tsx` — lazy
    route under `tenant-admin` (inside `RequireTenantAdmin`).

### Phase C — orkyo-saas

17. `backend/src/Services/SaasTenantTransferGuard.cs` — suspend (control-plane UPDATE, capture
    prior status) + `ITenantResolver.InvalidateCache(slug)`; restore in `EndAsync`. Register in
    `Program.cs` (explicit-registration rule).
18. New migration `backend/migrations/sql/controlplane/2200.saas.tenant_transfer_feature.sql`
    (`-- @migration-class: data`): `tenant_transfer_enabled` rows per tier, mirroring
    `data_export_enabled` in 2120 (recommend `true` on all tiers — data portability). Never edit
    2120 (immutability).
19. Tests: round-trip integration (provision second tenant DB, export A → import B: row counts,
    user re-link, asset re-encryption under B's id, 403 during import, status restored);
    feature-gate 403.

### Phase D — orkyo-community

20. `backend/src/Services/CommunityTenantTransferImportHook.cs` — trigger-cleanup DELETE; register
    in `Program.cs`.
21. Tests: import round-trip on shared DB asserting control-plane survival (tenants, existing
    memberships, user_identities, control-plane feedback untouched); placeholders get
    `status='disabled'` and NO membership; SaaS-shaped archive (built via unfiltered
    `FoundationMigrationModule` on a scratch Testcontainers DB) passes the gate despite journal
    ids 1240/1630; community-module journal entries don't block.
22. `release/docs/OPERATIONS.md` — "Moving your data to/from Orkyo SaaS": pg_dump first,
    maintenance window, size env var, archive sensitivity.

### Phase E — orkyo-infra (docs only)

23. New runbook `docs/runbooks/tenant-transfer.md` + cross-link from `docs/60-backups.md`: size
    ceiling env var on API containers, **nginx `client_max_body_size` must be ≥ ceiling for the
    import route** (verify current nginx config), backup before assisted imports,
    suspension-window behavior.

### Foundation docs

24. `orkyo-foundation/docs/tenant-transfer.md` — user/operator-facing format spec, table-set
    rules, gate algorithm, identity rules, provenance, size ceiling, plaintext-blob security note
    (this plan document can then be trimmed or superseded).

---

## Test plan

Foundation unit: manifest round-trip; NDJSON codec per pg type (bytea/jsonb/numeric/timestamptz);
**gate matrix** (equal / expand-delta accept / data-delta reject / contract-delta reject /
unknown-id reject / checksum-mismatch reject / feedback-pair irrelevance both directions /
unknown-module warning); relinker (case-insensitive match, placeholder email rewrite, remap map).

Foundation integration (DatabaseFixture, migrated tenant DB): full export → wipe → import round
trip with table-dump byte-compare; mid-import failure (poisoned row count) leaves target intact;
asset encryption round trip across two tenant ids; user-FK remap across ALL
pg_constraint-discovered columns; **`TenantTransferTableConformanceTests`** —
`information_schema` BASE TABLEs minus `{orkyo_schema_migrations}` == static set exactly, list
order is a valid FK topological order, no sequences exist. This test is the drift guard for every
future migration — write it first.

Foundation endpoint tests: viewer/editor 403 (RequireAdminArea), feature-gate 403, confirmation
mismatch 400, oversize 413.

Products: Phase C/D tests above. Community `RouteInventoryTests.cs` needs the three new routes
acknowledged.

Frontend: vitest for `transfer-api`; `DataTransferSettings` staged flow (validate-before-import,
backup-step gating, type-to-confirm disabled until slug matches);
`createFeedbackTestQueryWrapper` for mutation feedback.

Cross-repo: `orkyo-foundation/scripts/test-downstream.sh` is **mandatory** (new `Map*Endpoints` +
DI surface).

### Coverage requirement (≥80% on all new transfer code)

All new transfer code must reach **≥80% line coverage**, verified locally before push — not just
via Codecov's existing 80% patch gate. In scope: backend `backend/core/Services/Transfer/*` and
`backend/src/Endpoints/TransferEndpoints.cs` (foundation), the product guard/hook classes
(`SaasTenantTransferGuard`, `CommunityTenantTransferImportHook`); frontend
`lib/api/transfer-api.ts`, `components/settings/DataTransferSettings.tsx`,
`hooks/useDataTransferAvailable.ts`. Pure registration glue (`FoundationServiceExtensions`
additions) is exempt — `codecov.yml` already ignores DI/extension files; everything with logic is
not exempt.

Verification recipes:

- **Backend**: `dotnet test --collect:"XPlat Code Coverage"`, then ReportGenerator as a dev-only
  local dotnet tool (do NOT add it as a package reference) with a class filter on the Transfer
  namespace + `TransferEndpoints`; the Lines column must be ≥80%.
- **Frontend**: `npm test -- --run --coverage` (foundation) / `npm run test:coverage` (products);
  the per-file table must show ≥80% lines for each new file. The foundation frontend now enforces
  thresholds (lines 80 / statements 80 / branches 70 / functions 80) in `vitest.config.ts`, same
  bar as the saas/community frontends, so the suite fails locally and in CI if the bar is missed.

## Verification (end-to-end)

1. `dotnet format` + builds in all repos; foundation suite; `./scripts/test-downstream.sh`.
2. Both dev stacks side-by-side (community :5002/:5174, saas :5001/:5173): create data + upload a
   floorplan in Community → export → import into a SaaS tenant → floorplan renders (proves
   re-encryption), users matched by email, placeholder shows disabled/unlinked in Users; then the
   reverse direction.
3. SaaS suspension window: hit a tenant endpoint from a second session mid-import →
   tenant-suspended 403; after completion traffic restores without restart (cache invalidation).
4. Authorization conformance test passes (exactly one convention on the new group).

## Assumptions to verify at implementation time

- **Suspension side effects**: check `TenantLifecycleService` / lifecycle-email tracking (saas
  migration 2170) doesn't email on a transient `suspended`; if it does, use a distinct in-flight
  mechanism (e.g. status `importing` handled in `TenantMiddleware`).
- **csproj direction**: `Orkyo.Foundation.Core` → `Orkyo.Migration.Abstractions` (+ possibly
  `Orkyo.Migrator`) — confirm no cycle; fallback: run `MigrationRunner` from the Web layer.
- **ZipArchive Create-mode on non-seekable response stream** — supported on modern .NET; add
  temp-file spool fallback if the TFM misbehaves.
- **Tier feature-flag lookup path**: verified `data_export_enabled` rows in 2120; confirm
  inserting analogous rows is all `TierFeatureGate`/quota service needs for the new key.
- **nginx `client_max_body_size`** for SaaS ingress (orkyo-infra) — not yet read; must be raised
  for the import route.
- Exact transfer-table membership — derived authoritatively by the conformance test, not this
  plan.

## Branches

All four repos: develop on `claude/tenant-db-export-import-y8wytv`, push with
`git push -u origin <branch>`. Foundation first (A+B), then saas (C) and community (D) in either
order, infra docs (E) last.
