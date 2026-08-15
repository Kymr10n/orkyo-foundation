# Lists — tenant-defined list definitions and instances, linkable from custom fields

Status: **phases A, B and C implemented 2026-08-15** on branch
`everythingisaresource`, unpushed and awaiting visual sign-off. Phase D (Space/People
dissolution, D1-D7) remains as backlog. Supersedes the discarded `catalogs-plan.md` draft.

### What shipped

Migration 1780 (validated against a real Postgres: full chain, constraint behaviours,
revert, re-apply), the definition/instance/row API with its governance split, per-resource
lists and shared lists with lookup fields, the "Resources" admin section, and export of
definitions with their shared rows. 3029 backend tests and 3822 frontend tests.

### Deviations from this plan, and why

1. **Resource delete does not cascade list data.** The plan said it did. `DELETE
   /api/resources` deactivates (`is_active = false`) rather than deleting, so the FK
   cascade never fires and a deactivated resource keeps its rows — the coherent outcome,
   since one restored without its history would have lost data nobody agreed to discard.
   The CASCADE still covers paths that truly remove a row, such as a tenant purge. Found by
   a test, not by review; an earlier check against Postgres had exercised a hard DELETE and
   so confirmed a path the API never takes.
2. **The per-resource resolver GET answers 200 with a null body**, not 404. An untouched
   list is the ordinary state, not an error, and this repo already answers that question
   this way in `getFloorplanMetadata`.
3. **German UI names dropped.** The frontend has no i18n layer at all, so the plan's
   `de:` names had nowhere to live. English only; the German terms stay a documentation
   glossary.
4. **Boolean list columns declare no filter.** `ColumnFilterMeta` has no boolean case;
   they still sort, which is the useful half.
5. **`keyFromLabel` extracted** to `src/lib/key-from-label.ts` — it was module-private in
   `CustomFieldEditDialog` and list columns need the same derivation, which must agree with
   the server's key CHECK.
6. **The sidebar item sits after Administration**, not between it and Settings:
   `SidebarNav.test` pins that adjacency deliberately.

**Reviewed against the codebase 2026-08-15 before implementation; corrections applied**
(citations, paths, and three design gaps — i18n, boolean column filters, the phase A/B
type gate). The design itself is unchanged.

## Context

PR #111 (`d730449`) introduced tenant-defined custom fields on resource types
(text/number/boolean/date/url; definitions in `resource_custom_fields`, values in
`resources.custom_fields JSONB`). The long-term goal is to replace the built-in Space and
People types with the generic resource type. For that, custom fields must be able to link
**lists**, not only primitives. Examples: a car with a maintenance log (date, mileage); a
machine with components (part number, name, description, price). Mental model: SharePoint
lists, but simpler.

Decisions (2026-08-12):

- **Both modes**, schema from day one: the *definition* of a list (columns + types) is
  reusable; each *instance* (the data) is unique — per-resource instances (a car's own
  log) and shared instances referenced by many resources (a price edit propagates).
- Column types v1: five primitives **+ select** (single-select, admin-defined options).
  Select stays list-only; plain custom fields keep their five types (the 1770 deferral
  stands).
- **Naming — fully explicit definition/instance, everywhere:** **List definition** (de:
  *Listendefinition*) = the reusable columns + types. **List instance** (de:
  *Listeninstanz*) = a data holder created from a definition. Shared instances are named
  ("Standard components"); per-resource instances are anonymous — the custom field's
  label serves. The UI uses these terms verbatim.
- New admin section in the left-hand sidebar: **"Resources"** (German glossary term
  *Ressourcen* is documentation-only — the frontend has no i18n layer, every string is a
  hardcoded English literal, so the UI ships English), a
  dedicated page with tabs of its own, housing **Resource Types** (moved from
  Administration) and **List definitions**. Route `/configuration` (label ≠ route has
  precedent: "Administration" lives at `/tenant-admin`; `/resources` is taken by the
  resource pages). **The Administration page is otherwise untouched — its "Configuration"
  tab (tenant settings) keeps its name and content.**

All changes live in orkyo-foundation. The saas and community shells consume `TenantApp`
wholesale and need no changes.

## Naming map

- Tables: `list_definitions`, `list_columns`, `list_instances` (kind
  `'shared'|'resource'`), `list_rows`.
- Custom-field data types: `list` (per-resource) and `list_lookup` (shared lookup).
- Binding columns on `resource_custom_fields`: `list_definition_id` (for `list`),
  `list_instance_id` (for `list_lookup`).
- Routes: `/api/list-definitions` (definitions/columns/shared instances),
  `/api/list-instances/{instanceId}` (+ `/rows` — all row CRUD),
  `/api/resources/{resourceId}/list-fields/{fieldId}/instance` (per-resource resolver).
- UI: "Resources" section (route `/configuration`) with tabs "Resource Types" and "List
  definitions" (`/configuration/list-definitions`).

## Core design

**One `list_instances` table for both modes**, discriminated by `kind`:

- Per-resource instances are keyed `UNIQUE(resource_id, field_id)` and created lazily by
  the POST resolver (never by GET); before that, the client renders an empty list.
- Shared instances are admin-created and named, `UNIQUE(list_definition_id, name)`. No
  `is_active` column — nothing consumes it.
- Rows live in `list_rows` (`list_instance_id`, `values JSONB` keyed by column key),
  ordered by `created_at`. No row `sort_order`: nothing renders a row order, and the data
  table sorts by columns. Column definitions keep `sort_order` — form order matters
  there.

**Two new custom-field data types** (not one type + mode knob — the value shapes differ,
and `data_type` immutability then forbids mode switching for free):

- `list` (per-resource): **no value** in `resources.custom_fields` — rows are addressed
  by `(resource_id, field_id)`. Whole-document replace on the resource can never clobber
  rows; row edits commit independently of the resource form. Cannot be `isRequired`.
- `list_lookup` (shared): value = JSON array of row-id strings (multi-select, ≤100,
  unique, all ids must exist in the bound instance — batched check). Required = non-empty
  array.

**Bindings on `resource_custom_fields`:** nullable `list_definition_id` and
`list_instance_id`, with a bidirectional CHECK — `(data_type = 'list') =
(list_definition_id IS NOT NULL) AND (data_type = 'list_lookup') = (list_instance_id IS
NOT NULL)` — so scalar fields cannot carry a stray binding. Both immutable after create
(absent from the update request, like key/dataType).

**Delete semantics:** the FK from an instance to its resource is ON DELETE CASCADE, which covers
paths that truly remove the row (a tenant purge). `DELETE /api/resources` is *not* such a path —
it deactivates (`is_active = false`), so a deactivated resource keeps its list data. Field delete →
per-resource instances cascade (`field_id` FK) + the existing key-strip. Definition /
shared-instance delete → RESTRICT (409) while anything references them. Shared **row**
delete → strip that row id from every referencing resource's `custom_fields` array in the
same transaction (mirrors the `ResourceCustomFieldRepository.DeleteAsync` strip
precedent), so stored ids stay valid. Select options are editable; removing an option
never rewrites stored rows (criteria `enum_values` semantics — validated on write only).

**Caps:** 500 rows per instance and 100 picked rows per lookup value (service layer —
both guard unbounded payloads nothing else bounds); option text ≤100 chars
(FluentValidation). No column-count or option-count caps — criteria enum values are
uncapped (precedent). Cell bounds reuse text≤2000, url≤2048.

**Authorization:** `/api/list-definitions` = `RequireMemberReadAdminWrite()` — reshaping
what editors must fill is governance, same as resource types. Row data (both row groups)
= `RequireMemberReadEditorWrite()` — content, like job titles; verb-gating makes the
resolver POST editor-only. Non-matchability invariant restated in the migration header
and model docs: the solver never reads list data; matchable attributes remain criteria.

**Read path:** resource payloads are unchanged (the lookup array rides in
`customFields`); rows are fetched on demand per instance — no N+1 in resource lists.

## Work items

### 1. Migration — `backend/migrations-foundation/sql/tenant/1780.foundation.lists.sql`

`-- @migration-class: expand`, plus a revert at
`backend/migrations-foundation/revert/1780.foundation.lists.revert.sql` (parallel
directory, referenced from the migration header the way 1770 does). Creates the four
tables with updated_at
triggers: `list_definitions` (no key column — `UNIQUE(name)` like `job_titles`;
`is_active` is the retire path under RESTRICT), `list_columns` (key-format CHECK
`^[a-z][a-z0-9_]{0,49}$`, `data_type IN (text,number,boolean,date,url,select)`,
`options JSONB` only-for-select CHECK, `sort_order` + `is_active` mirroring
`resource_custom_fields`), `list_instances` (kind-shape CHECK, the two UNIQUEs),
`list_rows` (plain `(list_instance_id)` index inside the transaction — never
CONCURRENTLY). Alters `resource_custom_fields`: widen the data_type CHECK by same-name
drop/re-add (same technique as 1610, which drops and re-adds `requests_status_check`)
with `list` + `list_lookup`, add the two FK columns
(RESTRICT) + the bidirectional binding CHECK. The header restates non-matchability and
notes this is where the deferred `select` lands — for list columns only.

### 2. Backend

Pattern sources: `ResourceCustomFieldRepository/Service/Endpoints`, `JobTitleRepository`,
`CriterionValueValidator.ValidateEnum`.

1. `core/Models/ListDefinition.cs` (new): `ListColumnDataTypes`, `ListInstanceKinds`,
   `ListDefinitionInfo`, `ListColumnInfo` (with `Options`), `ListInstanceInfo`,
   `ListRowInfo` (`Values: Dictionary<string, JsonElement>`), create/update requests
   (column update omits key/dataType; options stay updatable; row requests carry Values
   only).
2. `core/Models/ResourceCustomField.cs`: add `List`/`ListLookup` to
   `CustomFieldDataTypes.All`; `ListDefinitionId`/`ListInstanceId` on Info + Create (not
   Update).
3. `core/Services/CustomFieldValueRules.cs` (new, extraction): shared scalar validation
   (`IsEmpty` + the per-type switch) pulled out of `ResourceCustomFieldService`, plus the
   `select` membership case (Ordinal; no declared options = accept, mirroring
   `CriterionValueValidator.ValidateEnum`). The signature takes a subject prefix ("Custom
   field 'X'" / "Column 'Y'") so existing error messages stay byte-identical.
4. `core/Repositories/ListDefinitionRepository.cs` (new): definitions + columns CRUD;
   delete-definition FK 23503 → 409; column delete strips the key from all rows of the
   definition's instances in one transaction.
5. `core/Repositories/ListInstanceRepository.cs` (new): shared-instance CRUD;
   `GetOrCreateResourceInstanceAsync` (ON CONFLICT DO NOTHING); one row-CRUD path keyed
   by instanceId, ordered `created_at`; `RowIdsExistAsync` batched; shared-row delete
   strips ids from referencing resources in one transaction (the strips are load-bearing
   — the `ResourceCustomFieldRepository.DeleteAsync` rationale applies verbatim).
6. `core/Services/ListDefinitionService.cs` (new): governance — select-options
   invariants, immutability, 409 delete guards.
7. `core/Services/ListRowService.cs` (new): row ops keyed by instanceId — validates
   values against the definition's active columns via `CustomFieldValueRules`, 500-row
   cap — plus the per-resource resolver: GET returns the (resource, field) instance or
   null (never creates); POST get-or-creates after verifying the field belongs to the
   resource's type and has `data_type = 'list'`.
8. `core/Services/ResourceCustomFieldService.cs` (modify): create-time binding validation
   (`list` → active definition required, `isRequired` rejected; `list_lookup` → shared
   instance required); `ValidateValuesAsync` — a `list` key with a value → 400; lookup
   array rules; an empty array counts as empty.
9. `core/Validators/ListRequestValidators.cs` (new) + extend
   `ResourceCustomFieldRequestValidators.cs` (bindings required iff matching type).
10. `src/Endpoints/ListDefinitionEndpoints.cs` (new): `/api/list-definitions`,
    admin-write — definition/column/shared-instance CRUD.
    `src/Endpoints/ListInstanceEndpoints.cs` (new), `RequireMemberReadEditorWrite()` on
    both groups:
    - `/api/list-instances/{instanceId}`: `GET /` (instance metadata), `GET /rows`,
      `POST /rows`, `PUT|DELETE /rows/{rowId}` — the only row-CRUD surface, both kinds.
      No GET-single-row.
    - `/api/resources/{resourceId:guid}/list-fields/{fieldId:guid}/instance`: `GET`
      (404 until first ensure — never creates), `POST` (get-or-create).
11. DI in `src/Configuration/FoundationServiceExtensions.cs`; mapping in
    `FoundationEndpointExtensions.cs`.
12. Export (last): `ExportData.ListDefinitions` = definitions + columns + shared
    instances with rows. Per-resource rows deferred with an explicit comment + follow-up
    issue; lookup ids export as-is inside `CustomFields`.

### 3. Frontend

**New "Resources" section (user-visible):**

1. `constants/auth.ts`: `ROUTE_CONFIGURATION = '/configuration'`.
2. `pages/ConfigurationPage.tsx` (new, copy `TenantAdminPage.tsx`): page title
   "Resources", tabs "Resource Types" + "List definitions".
3. `components/auth/TenantApp.tsx`: new guarded route block (`RequireTenantAdmin`);
   remove `resource-types` from tenant-admin (its `configuration` child is untouched);
   redirects: `tenant-admin/resource-types` → `/configuration/resource-types`, retarget
   the existing `settings/resource-types` redirect; `APP_LAYOUT_PREFIXES` +=
   `/configuration`.
4. `pages/TenantAdminPage.tsx`: drop the resource-types tab, update `LEGACY_TAB_TO_PATH`.
5. `components/layout/SidebarNav.tsx`: admin-gated "Resources" item, next to the
   Administration item. No collision: SidebarNav is a flat list with no group labels, and
   no item is named "Resources" (verified 2026-08-15). Only a tenant type whose
   `displayNamePlural` is "Resources" could ever clash, and none ships.

**API layer:** `lib/core/api-paths.ts` (+ list paths); `lib/api/lists-api.ts` (new,
pattern `resource-custom-fields-api.ts`, incl. `LIST_COLUMN_DATA_TYPES`);
`resource-custom-fields-api.ts` (types `'list' | 'list_lookup'` + hints,
`CustomFieldValue` += `string[]`, `listDefinitionId`/`listInstanceId` fields);
`lib/api/query-keys.ts` (`qk.lists.*`); `hooks/useListDefinitions.ts`.

Widening `CustomFieldValue` to include `string[]` ripples through a fixed set — miss one
and the type lies: `CustomFieldValue` and `CustomFieldDataType`, `CUSTOM_FIELD_DATA_TYPES`,
`customFieldDataTypeLabel`, `hasCustomFieldValue` (in
`components/resources/CustomFieldInput.tsx`), and `hooks/useResourceCustomFieldForm.ts`.

**Frontend primitive kit (`components/lists/` + hooks)** — every surface below is an
assembly of these primitives; future surfaces (Space/People dissolution, new admin
grids) reuse them unchanged:

1. `components/fields/ScalarValueInput.tsx` (new — `components/fields/` does not exist yet
   and is created here as the reuse point; `CustomFieldInput.tsx` itself lives in
   `components/resources/`): one typed-value input driven by
   `{ dataType, options?, value, onChange, id }` — the frontend counterpart of the
   backend `CustomFieldValueRules` extraction; all six scalar types incl. `select`.
   `CustomFieldInput.tsx` delegates its scalar rendering to it (public prop API
   unchanged); list cells use it directly. One place to add the next data type.
2. `components/lists/format-list-cell.ts` (new): pure display formatter
   `formatListCell(column, value)` used by tables, cards, and the picker.
3. `components/lists/ListRowsTable.tsx` (new): schema-driven `OrkyoDataTable` wrapper —
   dynamic columns from `ListColumn[]`, phone `renderCard`, optional `RowActions`. Pure
   presentation. Four constraints come from the react-table v9 seam
   (`lib/table/features.ts`, migrated 2026-08-14):
   - type runtime-built columns as the `ColumnDef<TData>` alias from `lib/table/features`,
     never the raw `@tanstack/react-table` type, or the feature generic will not line up;
   - filter functions are registered centrally and named, not passed: only `oneOf`,
     `arrayOverlaps`, `dateBetween`, `includesString` and `inNumberRange` exist, and
     adding one edits a file shared by every table in the product;
   - filters are declared only through `meta.filter`, resolved by `filterFnFor` in
     `lib/table/column-meta.ts`;
   - `ColumnFilterMeta` is a closed union (`text | enum | date | number`) with **no
     boolean**. v1: `select` columns declare `enum`; `boolean` columns declare no filter
     and remain sortable. Extra meta keys need the `declare module` augmentation in
     `column-meta.ts`.
4. `hooks/useListRows.ts` (new): row queries/mutations keyed by **instanceId** — one
   data path, no adapter interface (`apiFor(instanceId)` over `createCrudApi` mirrors
   `resource-custom-fields-api.ts`). Plus `useResourceListInstance(resourceId, fieldId)`:
   GET-resolves the per-resource instance and exposes `ensure()` (POST) invoked before
   the first row write.
5. `components/lists/ListRowEditDialog.tsx` (new): `FormDialog` + `useEntityFormDialog`;
   body inlined — one `ScalarValueInput` per active column with required markers. No
   separate row-form component: one consumer.
6. `components/lists/ListRowsEditor.tsx` (new): the full assembly — `ListRowsTable` +
   `ListRowEditDialog` + `ConfirmDialog`. Props: `instanceId: string | null`, optional
   `ensureInstanceId?: () => Promise<string>`.
7. `components/lists/ListRowPicker.tsx` (new): multi-select over an instance's rows
   (display text via `formatListCell`, first active column primary).

**Assemblies (thin):**

- Admin: `components/settings/ListDefinitionSettings.tsx` (section table, pattern
  ResourceTypeSettings/JobTitleSettings) → `ListDefinitionEditDialog`
  (name/description/active) → `ListColumnsDialog` (pattern
  ResourceTypeCustomFieldsDialog) → `ListColumnEditDialog` (pattern
  CustomFieldEditDialog; `keyFromLabel` is currently module-private in
  `CustomFieldEditDialog.tsx` and must be exported/extracted first, then reused with
  `EnumValueEditor` for select options) →
  `ListInstancesDialog` (shared instances of one definition) → `ListInstanceDataDialog` =
  ScaffoldDialog wrapping `ListRowsEditor(instanceId)`.
- Resource form: `CustomFieldInput.tsx` branches — `list` →
  `useResourceListInstance(resourceId, fieldId)` then
  `ListRowsEditor(instanceId, ensureInstanceId)` (immediate commit + hint; the create
  dialog shows "rows can be added after the resource is created"); `list_lookup` →
  `ListRowPicker(field.listInstanceId)` writing `string[]` into the form value. Extend
  props with `resourceId?`; `hasCustomFieldValue` array handling; pass `resourceId` in
  `ResourceEditDialog.tsx` + `PersonEditDialog.tsx`.
- `CustomFieldEditDialog.tsx`: definition picker for `list` (required hidden), definition
  → shared-instance picker for `list_lookup`; both fixed on edit.

Invalidation: row mutations → `qk.lists.instanceRows(instanceId)`; column mutations →
`qk.lists.all`.

### 4. Tests

Backend: `tests/Endpoints/ListDefinitionEndpointsTests.cs` (CRUD, 409s, immutability,
options invariants, strip on column delete), `ListRowTests.cs` (per-type + select
validation, row cap, resolver GET-never-creates + POST-ensures + wrong field/type 404,
cascades, shared-row-delete strip, dissolution guarantee: a `list` field defined on the
system person type with rows attached to a person resource works end-to-end), extend
`ResourceCustomFieldEndpointsTests/ValueTests` (bindings incl. stray-binding rejection,
lookup array rules, field-delete cascade), authorization matrix entries for the three
new groups.

Frontend: primitive tests — `ScalarValueInput` (all six types), `ListRowsTable` (dynamic
columns, renderCard), `ListRowsEditor` (add/edit/delete against a mocked API;
ensureInstanceId invoked once before the first write), `ListRowPicker`,
`format-list-cell`; assembly tests — ConfigurationPage, ListDefinitionSettings,
ListColumnEditDialog (options only for select), ListInstanceDataDialog; update
`TenantApp.test.tsx` (routes + redirects), `SidebarNav.test.tsx`,
`TenantAdminPage.test.tsx`, `ResourceEditDialog.customFields`, `CustomFieldInput` (the
ScalarValueInput refactor must keep existing tests green).

### Phasing (PR boundaries only; migration 1780 complete from day one)

A: migration + definition/column backend + "Resources" section + primitive kit (items
1–6) + `list` type + per-resource rows editor. Migration 1780 admits both CHECK values on
day one, but `CustomFieldDataTypes.All` is the API gate: phase A adds **only** `List`, so
`list_lookup` cannot be created before its binding validation lands in B. B: shared instances + data grid (reusing
the kit) + `list_lookup` + `ListRowPicker` + row-delete strip. C: export + polish. D:
dissolution readiness (below).

## Backlog — a designated display column (requested 2026-08-15, from review)

**What is wrong today.** A row has no single field that identifies it, so every surface that
must show a row in one line invents an answer: `describeRow` in `ListRowPicker` takes the first
active column as the head and appends the rest as context, joined with `—` and `·`. Reviewing a
lookup field on the space form showed the result — `Name — 7'865` — which is a concatenation, not
a name. The author knows which column identifies a row; the code is guessing.

**What is wanted.** The admin marks one column of a definition as the one shown wherever a row
is represented as a single value.

**Shape.** A nullable `display_column_id` on `list_definitions`, FK to `list_columns` with
`ON DELETE SET NULL`, rather than an `is_display` flag on the column — one definition has exactly
one display column, and a single FK cannot drift into two columns both claiming it. `SET NULL`
matters: deleting the designated column must not take the definition with it, and the fallback
below then applies again.

**Rules.**
- The column must belong to that definition (validate on set, like every other cross-entity
  binding here).
- Unset falls back to the current behaviour — first active column by `sort_order`, then label.
  Existing definitions therefore keep working with no migration of data.
- A designated column that is later deactivated falls back the same way rather than showing a
  field the row form no longer asks for.
- Any column type may be designated. A `boolean` display column reads "Yes"/"No", which is
  useless but is the author's choice to make, and refusing it adds a rule to explain.

**Surfaces that consume it.** `ListRowPicker.describeRow` (the case that prompted this),
`ListRowsTable` for the phone card heading, and any future place a row is rendered as one value.
`formatListCell` already produces the text; this only decides which column to hand it.

**UI.** A "Shown in forms" picker in `ListDefinitionEditDialog`, listing the definition's active
columns; or a radio in the columns dialog. The dialog needs the columns loaded either way, which
`ListColumnsDialog` already does.

**Tests.** The fallback when unset, when the designated column is deactivated, and when it is
deleted; rejection of a column from another definition; and that the picker's line changes to
the designated column's value alone.

## Path to Space/People dissolution (Phase D)

The end goal is moving the built-in Space and People types onto the generic model
entirely. Lists is one prerequisite, not the mover. This section records the design
guarantees this feature holds, and the remaining backlog.

**Design guarantees (verified, not asserted):**

1. List fields bind by `resource_type_id`; space and person are ordinary
   `resource_types` rows, so tenants can attach lists to them (certifications on a
   person, equipment in a space) with zero special-casing. Per-resource instances key on
   `resources.id` — spaces and people ARE `resources` rows since migration 1700 (1710 is
   the contract half that dropped the side tables). A
   backend test pins this end-to-end on the system person type.
2. `list` fields cannot be required, so `EnsureRequirable`/foundation#110 (the space
   create form carries no custom-field document) is not aggravated — rows attach after
   creation regardless of which endpoint created the resource.
3. Zero `ResourceTypeKeys` references in any new code — no new key-driven special-casing
   (the §1 lesson of `docs/generic-resource-types-review-2026-08.md`: never reintroduce
   a dual key/flag system).
4. The primitive kit is dialog-agnostic: `PersonEditDialog`, `ResourceEditDialog`, and
   later `EditSpaceDialog` share one rows-editor implementation — the DRY vehicle for
   converging the three dialogs during dissolution.
5. Explicit non-goal: the person directory fields (email, notes, job_title_id,
   department_id, linked_user_id) are NOT remodeled as custom fields or lists — they
   carry FK integrity and drive identity and product behaviour. Their path is the
   generic `ResourceInfo` contract gated by `has_directory_profile`.

**Remaining backlog (follow-up work, in order):**

- D1: foundation#110 — `SpaceInfo`/`Create/UpdateSpaceRequest` gain `CustomFields`;
  `EditSpaceDialog` renders the custom-fields block (incl. list fields via the kit).
- D2: directory fields onto the generic contract (`ResourceInfo` + requests, gated by
  `has_directory_profile`) — **DONE**. Retiring `PersonProfileEndpoints` +
  `person-profiles-api.ts` is **refuted**: see below.

  **D2 revision (2026-08-15, evidence from the consumers).** The profile endpoints are not a
  thin shim over the same data — they are a read-optimised projection layer, and the generic
  per-resource contract cannot replace them:

  - `POST /api/person-profiles/job-titles` returns `{resourceId, jobTitleName}` only. It exists
    because `ResourceUtilizationGrid` previously fanned out one request per resource. It needs
    the *resolved* name, not the id.
  - `POST /api/person-profiles/batch` serves `PersonList`, which renders `jobTitleName` and
    `departmentPath` for every row.
  - `POST|DELETE /{id}/link` is its own operation with its own checks; `LinkedUserId` is
    deliberately absent from the generic requests for the same reason.

  Both resolved fields come from a `LEFT JOIN` to `job_titles` and a recursive CTE that walks
  the department parent chain. Putting them on `ResourceInfo` would add both to *every* resource
  read — including list reads and the utilization grid — to serve two screens.

  What can still be simplified: `GET /{id}` and `PUT /{id}` are now genuinely duplicated by
  `/api/resources/{id}`, so `PersonEditDialog` could move its read and write across, leaving the
  profile API as a pure projection layer (`/job-titles`, `/batch`, `/link`). That is a smaller,
  honest change than the retirement this plan assumed, and it is what should be scheduled.
- D3: retire the `SpaceEndpoints` thin shim in favour of `/api/resources` (needs D1; the
  placeable-cannot-travel rule is already enforced centrally in `ResourceService`).
- D4: frontend convergence — retire `TYPES_WITH_DEDICATED_PAGES`; `SpacesPage`/
  `PeoplePage` become configured compositions of generic surfaces.
- D5: floorplan/utilization surfaces generalized by `has_geometry`
  (`SpaceManagementPanel`, the `UtilizationPage` space scheduler grid).
- D6: scheduling stops defaulting a missing target type to `space`
  (`SchedulingProblemBuilder.cs:51`, `RequestRepository.cs:437`).
- D7: `absence_type`/`off_times.type` CHECK enums become tenant-editable reference data.
- Documented as permanent, NOT gaps (review §2.1/§2.2): presets and the space export
  stay key-driven (identity, not behaviour); people stay untargetable in the UI until a
  target can mean "one or more".

## Verification

- `dotnet test` in `backend` (includes the authorization contract/matrix guards). Migration
  numbering is not test-guarded: `scripts/ci/lint-migration-headers.sh` checks the class
  header and `migrator-runtime/MigrationOrderer.cs` rejects duplicate ids; `npm test`, `npm run lint`, `tsc` in `frontend`.
- Manual: (1) create a "Maintenance log" definition (date+number), bind it as a `list`
  field on a type, add/edit/delete rows on a resource, delete the resource → rows gone.
  (2) "Components" definition with a select column, shared instance, `list_lookup` field
  on machines, pick rows on two machines, edit a price → both see it; delete a picked
  row → both drop the reference. (3) `/tenant-admin/resource-types` and
  `/settings/resource-types` redirect to `/configuration/resource-types`;
  `/tenant-admin/configuration` works unchanged. (4) An editor edits rows but not
  definitions; a member reads everything, writes nothing.

## User-visible changes to announce

A new admin-only sidebar section **"Resources"** with tabs of its own:
Resource Types (moved out of Administration) and List definitions (new). Administration
keeps its "Configuration" tab unchanged — no tab is renamed. Old resource-types URLs
redirect.
