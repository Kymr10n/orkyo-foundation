# Data Model & Schema Review — June 2026

A review of the orkyo-foundation data model and schema, weighing **performance**,
**integrity**, and adherence to **best practices, DRY, and KISS**.

Scope reviewed:

- All 60 migrations across two schemas: `backend/migrations-foundation/sql/controlplane/`
  and `backend/migrations-foundation/sql/tenant/`.
- The C# domain model (`backend/core/Models/`) and repository layer
  (`backend/core/Repositories/`) — raw Npgsql/ADO.NET, no EF Core.
- The multi-tenancy model: **database-per-tenant** (one control-plane DB plus one DB per
  tenant), routed via `OrgContext` / `TenantConnectionStringHelper`, with scoping enforced
  by repository constructor dependency (`RepositoryScopingTests`) rather than Postgres RLS.

> **On remediation:** applied migrations are immutable in this repo
> (`CLAUDE.md` → Migration rules, enforced by `scripts/ci/lint-migration-headers.sh`).
> Every DDL suggestion below is therefore written as a **new follow-up migration**, never
> as an edit to an existing file. No schema changes are made by this document.

---

## A. What the schema does well

Worth stating up front, because these are the right defaults:

- **`timestamptz` everywhere** — no naive `timestamp`. Correct for a scheduling domain that
  spans time zones (`scheduling_settings.time_zone`).
- **Rich domain CHECK constraints** on `requests`: date ordering
  (`requests_constraint_dates_order_check`), schedule-within-constraints, duration
  completeness (`requests_actual_duration_complete_check`), and `valid_time_range`. Integrity
  lives in the database, not only in app code.
- **Partial indexes** used well: `WHERE enabled = true`, `WHERE assignment_status != 'Cancelled'`,
  `WHERE keycloak_id IS NOT NULL`. Smaller, hotter indexes.
- **Disciplined expand/contract migrations.** The space→resource cutover (1300/1310) staged
  into `*_phase1` tables and dropped them after the atomic cutover — no leftover scaffolding.
- **`ux_ra_active_request_resource`** (partial unique on `(request_id, resource_id)` where not
  cancelled) prevents duplicate active assignments at the database level.
- **DB-per-tenant isolation** eliminates whole classes of cross-tenant data-leak bugs that
  RLS or shared-schema designs are prone to.

---

## B. Performance

### B1 — Redundant indexes (recurring pattern) — *high value, low risk*

A `UNIQUE` / `PRIMARY KEY` constraint already creates a backing btree. A second plain index
on the same leading column(s) is dead weight: it adds write amplification on every
insert/update and consumes space, while the planner uses the constraint's index anyway.

Confirmed instances:

| Redundant index | Already covered by |
| --- | --- |
| `idx_users_email` (1010) | `users_email_key UNIQUE (email)` |
| `idx_tenants_slug` (1080) | `tenants_slug_key UNIQUE (slug)` |
| `idx_invitations_token_hash` (1095) | `invitations_token_hash_key UNIQUE (token_hash)` |
| `idx_user_preferences_user_id` (1120) | `user_preferences_pkey (user_id)` |
| `idx_tos_acceptances_user_id` (1040) | unique `(user_id, tos_version)` — left-prefix |
| `idx_memberships_user_id` (1220) | unique `(user_id, site_id)` — left-prefix |
| `idx_requests_start_ts` (1200) | `idx_requests_time_range (start_ts, end_ts)` — left-prefix |
| `idx_criteria_name` (1170) | `criteria_name_key UNIQUE (name)` (see B2) |

Suggested follow-up migration (new file, e.g. `1570.foundation.drop_redundant_indexes.sql`):

```sql
-- @migration-class: contract
-- Drop indexes fully covered by an existing UNIQUE/PK backing index.
DROP INDEX IF EXISTS public.idx_criteria_name;            -- tenant DB
DROP INDEX IF EXISTS public.idx_requests_start_ts;        -- tenant DB
DROP INDEX IF EXISTS public.idx_memberships_user_id;      -- tenant DB
DROP INDEX IF EXISTS public.idx_user_preferences_user_id; -- tenant DB
```

```sql
-- Control-plane DB (separate migration module / target):
DROP INDEX IF EXISTS public.idx_users_email;
DROP INDEX IF EXISTS public.idx_tenants_slug;
DROP INDEX IF EXISTS public.idx_invitations_token_hash;
DROP INDEX IF EXISTS public.idx_tos_acceptances_user_id;
```

> Note the control-plane and tenant indexes live in different databases, so they belong in
> the migration module that targets each. Use `DROP INDEX CONCURRENTLY` if these run against
> a live production DB outside a transaction.

### B2 — `criteria.name` is indexed three times

```sql
CONSTRAINT criteria_name_key UNIQUE (name)              -- backing btree on (name)
CREATE INDEX idx_criteria_name ON criteria (name);      -- duplicate btree on (name)
CREATE UNIQUE INDEX idx_criteria_name_lower
    ON criteria (LOWER(name));                          -- case-insensitive unique
```

The case-insensitive unique (`LOWER(name)`) is the strictest rule — two names differing only
by case are already disallowed. So:

- `idx_criteria_name` is **fully redundant** (drop it, per B1).
- `criteria_name_key UNIQUE (name)` is **largely redundant** with the `LOWER(name)` unique.
  Keeping the case-insensitive unique as the single canonical rule is simpler (KISS) and still
  serves equality lookups on `name`.

### B3 — Synchronous search triggers are expensive on bulk paths

`sync_search_*` triggers fire `AFTER INSERT OR UPDATE OR DELETE` on `requests`, `resources`,
`spaces`, `criteria`, `sites`, `templates`, and `person_profiles`. Each does joins and a
`string_agg` over a resource's capabilities to rebuild a `search_documents` row **per affected
row**.

This is fine for interactive single-row edits. It becomes costly on bulk paths — most notably
**preset application** (`PresetApplier`), which inserts many criteria/spaces/templates and pays
the per-row re-index cost on each. Recommendation: for bulk/import paths, suppress the triggers
for the batch and rebuild the affected `search_documents` in a single pass (or move search
sync to an async/queued projection). At minimum, document the cost so bulk callers are aware.

### B4 — `v_requests_with_assignments` correlated subquery

The view builds each request's `assignments` array with a correlated `jsonb_agg` subquery, so a
list endpoint returning N requests runs the subquery N times. It is mitigated by `idx_ra_request`
and is acceptable today. For large tenants, an equivalent `LATERAL` join or a pre-aggregating CTE
keeps the same shape with one pass. Flag, don't fix now.

---

## C. Integrity

### C1 — No optimistic concurrency control anywhere

There is no `version` / `xmin` / `rowversion` usage in the schema or repositories. All
mutations are raw-SQL `UPDATE`s with last-write-wins semantics. With the dialog-based UI, two
users editing the same `request` / `resource` / `criterion` / settings row will silently clobber
each other — the second save wins with no detection.

Recommendation (larger initiative, no ready DDL): add optimistic concurrency on hot mutable
entities. Two options:

- **`xmin` system column** — no schema change; read `xmin` on load, add `WHERE xmin = @expected`
  to the `UPDATE`, treat 0 rows affected as a conflict. Cheapest to adopt.
- **Explicit `version integer` column** — bumped on each update; portable and visible in the
  model. More invasive but clearer.

Surface the token as an ETag / `If-Match` at the API boundary so the frontend can show a
"changed since you opened it" message instead of silently overwriting.

### C2 — Polymorphic associations with no database-level FK

Several tables reference a target by `(type, id)` with no real foreign key, so orphans are
possible if the referenced row is deleted:

- `assets (owner_type, owner_id)`
- `availability_event_scopes (target_type, target_id)`
- `preset_mappings (entity_type, entity_id)`
- `audit_events (target_type, target_id)` — **acceptable**: an audit log is immutable history
  and intentionally outlives its targets.

For the first three, where the target-type set is small and fixed, prefer one of:

- **Typed nullable FK columns + an exactly-one CHECK** (e.g. `owner_site_id uuid REFERENCES sites`,
  with a CHECK that exactly one owner column is set). Restores real referential integrity.
- A **scheduled orphan-integrity check** if the polymorphic shape must stay.

At minimum, document which component owns cleanup when a target is deleted.

### C3 — `enforce_space_single_group()` guards INSERT only

```sql
CREATE TRIGGER trg_space_single_group
    BEFORE INSERT ON resource_group_members
    FOR EACH ROW EXECUTE FUNCTION enforce_space_single_group();
```

The trigger enforcing "a space belongs to at most one group" fires only on `INSERT`. An
`UPDATE` of `resource_group_members.resource_group_id` would bypass it. Likelihood is low (the
PK includes `resource_group_id`, so group moves are delete+insert), but the function's own
comment frames it as defense-in-depth — so it should cover `UPDATE` too.

Suggested follow-up migration:

```sql
-- @migration-class: contract
DROP TRIGGER IF EXISTS trg_space_single_group ON resource_group_members;
CREATE TRIGGER trg_space_single_group
    BEFORE INSERT OR UPDATE ON resource_group_members
    FOR EACH ROW EXECUTE FUNCTION enforce_space_single_group();
```

### C4 — Inconsistent `ON DELETE` semantics for site references

- `spaces.site_id` → `ON DELETE CASCADE`.
- `requests.site_id` (1550) → no action declared → defaults to `NO ACTION` (restrict).
- `resources.home_site_id` (1550) → no action declared → `NO ACTION`.

Both `requests.site_id` and `resources.home_site_id` are nullable *scope* columns, so deleting a
site is currently blocked by them — likely not the intent. Decide the semantics explicitly
(probably `ON DELETE SET NULL` for both nullable scopes) and apply consistently via a follow-up
migration that drops and re-adds the FKs with the chosen action.

### C5 — `assets.tenant_id` is a denormalized constant in a per-tenant DB

In the database-per-tenant model, a tenant DB contains exactly one tenant, so `assets.tenant_id`
holds a single constant value across all rows. It provides no isolation (the database boundary
already does) yet leads every asset index — `idx_assets_tenant_owner`,
`idx_assets_tenant_asset_type`, and `ux_assets_owner_asset_type_floorplan`. It is also the
**only** tenant-DB table carrying a tenant column, which makes it inconsistent with the rest of
the schema and meaningless in Community single-tenant.

Recommendation: either drop `tenant_id` and re-create the three indexes without it, or document
the concrete reason it's retained (e.g. a planned cross-DB / shared-storage asset path). Pick
one; the current state reads as an accidental carry-over.

---

## D. DRY / KISS

### D1 — Two `updated_at` trigger functions that do the same thing

```sql
update_updated_at_column()   -- NEW.updated_at = NOW();
update_requests_updated_at() -- NEW.updated_at = CURRENT_TIMESTAMP;
```

`NOW()` and `CURRENT_TIMESTAMP` are the same function in Postgres, so these are identical. Only
`requests` uses the bespoke variant, for no reason. Consolidate onto the shared function and drop
the duplicate:

```sql
-- @migration-class: contract
DROP TRIGGER IF EXISTS trigger_requests_updated_at ON public.requests;
CREATE TRIGGER trigger_requests_updated_at
    BEFORE UPDATE ON public.requests
    FOR EACH ROW EXECUTE FUNCTION public.update_updated_at_column();
DROP FUNCTION IF EXISTS public.update_requests_updated_at();
```

### D2 — Inconsistent index / constraint naming

Three prefixes coexist: `idx_` (original), `ix_` (introduced ~1410), `ux_` (newer unique
indexes). This hurts greppability and makes the schema look stitched-together. Pick one scheme
(e.g. `ix_` for non-unique, `ux_` for unique), document it in `CLAUDE.md` or a migrations README,
and align new migrations going forward. Existing names need not churn (renaming is low value and
immutable-migration-unfriendly), but new objects should follow one rule.

### D3 — Enum value-sets duplicated across scattered CHECKs

The same value sets are repeated in CHECK constraints on multiple tables — duration units on both
`requests` and `request_templates`; role lists across `tenant_memberships`, `invitations`,
`memberships`, `invites`; off-time/absence types expanded table-by-table (1480). Adding one value
means hunting every CHECK and writing a new migration per table (as 1480 already had to).

The decision to model enums as `varchar + CHECK` (rather than PG `enum` types, which are painful
to alter) is reasonable. The fix is to keep a single **documented canonical list** per concept —
or a small reference table the values FK into — so the sets can't silently drift.

### D4 — Acceptable duplication (documented, not a defect)

- **Control-plane vs tenant `audit_events`** are byte-identical by design; they serve different
  scopes and live in different databases.
- **Tenant-local `users` mirror** duplicates a subset of control-plane `users` so tenant-DB rows
  can FK to a local user. Sync is app-enforced via `synced_at`, with no DB-level guarantee of
  consistency. This is a reasonable trade in a DB-per-tenant design — but the sync contract
  should be documented so the eventual-consistency window is explicit.

---

## E. Prioritized recommendations

1. **Low-risk, high-value — one new migration (per DB target):**
   drop redundant indexes (B1, B2), consolidate the duplicate trigger function (D1), widen the
   single-group guard to `UPDATE` (C3).
2. **Decide, then fix:** site-reference `ON DELETE` semantics (C4); keep-or-drop `assets.tenant_id` (C5).
3. **Larger initiatives:** optimistic concurrency on hot entities (C1); a polymorphic-FK integrity
   strategy (C2).
4. **Hygiene:** naming convention (D2), canonical enum lists (D3), bulk-path search-trigger cost (B3).

All schema changes above must ship as **new** migration files — existing migrations are immutable.
