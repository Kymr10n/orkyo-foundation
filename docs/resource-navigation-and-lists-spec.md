# Resource navigation and shared lists — specification

Status: **draft for approval**, written 2026-08-17 on branch `everythingisaresource`.
This document revises an externally written specification. It keeps the intent of that
document and replaces its assumptions with the schema and the routes that exist today.

The objective is a resource information architecture that scales to an unlimited number of
tenant-defined resource types, without growth of the primary navigation.

## 1. Corrections to the source specification

The source document was written without access to this codebase. Five of its premises are
not correct here.

| Source premise | Actual state |
|---|---|
| "Spaces → Stations" is a pending migration | The `space` type is already retired. Migration `1800.foundation.demote_space_type.sql` made it an ordinary tenant type. Placement is keyed on `resource_types.has_geometry`. |
| Cars are an existing resource type | No car or vehicle type exists. The seeded types are `person` and `tool`. The demo adds `mill`, `drill` and `assembly_station`. |
| Teams are an existing concept | No `teams` table exists. Person groups carry the label "Team" in the route `/people/teams`. The storage is `resource_groups`. |
| Departments and job titles are lists | They are first-class tables with foreign keys from `resources` and a parent tree on departments. Section 8.4 covers their migration. |
| `person_profiles` holds people data | That table is dropped. Migrations 1700 and 1710 folded its columns onto `resources`. |

The source document also asks for preservation of floorplan behaviour. That behaviour is
already generic: the floorplan holds every placeable type, not one type.

## 2. Domain model

A **Resource** is anything that participates in planning, scheduling and requests. Every
resource has a **resource type**. Resources fall into two classes:

- A **Station** has a fixed location. A station can carry geometry on a floorplan.
- An **Asset** is mobile. Availability is the primary planning dimension of an asset.

The class is not a new column. `resource_types.has_geometry` is the discriminator, because
that flag already carries the station semantics: code, geometry, capacity and owning site.
The API will expose a derived field `resourceClass` with the values `station` and `asset`,
so that the frontend does not read the raw flag.

```
RESOURCE
├── STATION  (has_geometry = true)   types: mill, drill, assembly_station, …
│     ├── Groups
│     └── Lists  (definitions owned by the type)
└── ASSET    (has_geometry = false)  types: person, tool, …
      ├── Groups
      └── Lists  (definitions owned by the type)
```

A type answers "what kind of resource is this". A list supplies controlled values for one
attribute of that type. Brand, model and certification are list definitions, not types.

### 2.1 Class changes

System types keep their current protection. `ResourceTypeService` refuses a flag change on
a system type, and that guard stays.

A tenant type can change class only while no resource of that type holds geometry. The
service must verify this precondition, because a station that becomes an asset otherwise
loses its placement silently.

## 3. Primary navigation

The sidebar must not contain one entry per resource type. The target sidebar is:

```
Utilization
Stations
Assets
Requests
Insights
Organization
Settings
Administration
Configuration
```

Today the sidebar builds one entry per active type
(`frontend/src/components/layout/SidebarNav.tsx:67-74`), plus dedicated People and Floorplan
entries. This specification deletes those entries. The constants `TYPES_WITH_DEDICATED_PAGES` and
`DEDICATED_TYPE_ROUTES` in `frontend/src/constants/resource-type-key.ts` become unnecessary
and can be deleted, together with the last hardcoded type exception in the frontend.

A new resource type must never add a sidebar entry, a route, or a tab.

## 4. Class pages

Stations and Assets use one page component with a `resourceClass` property. The existing
`frontend/src/pages/ResourcesPage.tsx` generalizes into it, and keeps its nested-outlet
pattern.

Routes are path segments, not query parameters, because the route tree nests this
way and deep links stay simple:

```
/stations/:typeKey/instances
/stations/:typeKey/groups
/stations/:typeKey/lists
/stations/floorplan

/assets/:typeKey/instances
/assets/:typeKey/groups
/assets/:typeKey/lists
```

`/stations` and `/assets` without a type redirect to the first active type of the class.

The type selector is a single-select dropdown in the page header. It navigates between
`:typeKey` routes. The tab set is stable: a different type never changes the tabs, with the
single exception of the Lists tab in section 8.5.

The tab bodies exist: `ResourceListTab` and `ResourceGroupsTab` in
`frontend/src/components/resources/ResourceTypeTabs.tsx`. `ResourceList` already carries
capabilities, absences, CSV transfer and custom-field editing for any type.

Groups keep their per-type scope. The label stays configurable in the frontend through a map
`GROUP_ENTITY_LABELS = { person: 'Team' }`, which preserves the current wording without a
schema change.

### 4.1 Redirects

All previous locations redirect permanently:

| From | To |
|---|---|
| `/resources/:typeKey/list` | `/stations` or `/assets` + `/:typeKey/instances`, resolved from the type |
| `/resources/:typeKey/groups` | the same class route with `/groups` |
| `/people/list` | `/assets/person/instances` |
| `/people/teams`, `/people/groups` | `/assets/person/groups` |
| `/people/departments` | `/organization/departments` |
| `/people/job-titles` | `/organization/job-titles` |
| `/floorplan`, `/floorplan/floorplan` | `/stations/floorplan` |
| `/floorplan/stations` | `/stations` |

The `/spaces/*` redirects that exist today continue to work, through the `/floorplan`
targets they point at.

`frontend/src/components/layout/CommandPalette.tsx` builds resource deep links from the same
resource-types query, and follows the new routes.

## 5. Floorplan

The floorplan canvas moves into the Stations page as the tab `/stations/floorplan`. It is a site surface that holds every placeable type at once. The type selector has no
meaning on that tab, and stays hidden there.

The canvas is the whole body of that tab. `ResourceClassPage` renders the header and the tab
strip around it, and `frontend/src/components/spaces/FloorplanView.tsx` renders the canvas. No
page sits between them, and the tab has no sub-tabs of its own.

An earlier draft of this document kept a `FloorplanPage` with Plan and Stations sub-tabs. Both
are removed. The Plan sub-tab was one click between the Floorplan tab and the floorplan. The
Stations sub-tab listed stations that the per-type Instances tabs already list.

## 6. Utilization

The Utilization page carries exactly three tabs: **Calendar**, **Stations** and **Assets**.
The current page adds one tab per non-placeable type
(`frontend/src/pages/UtilizationPage.tsx:698-702`). This specification deletes those tabs.

Each of the Stations and Assets tabs carries a multiselect dropdown of the active types of
its class:

- The default selection is every type of the class.
- Each tab has its own URL parameter, `?stationTypes=` and `?assetTypes=`, so a link is
  shareable and one tab cannot reject and reset the other's selection.
- `localStorage` mirrors the last selection per tab, and supplies the default when the URL
  carries no parameter. A selection of every type is recorded as absence in both places, never
  as the expanded list. A stored list excludes a type the tenant defines later.

The Stations tab keeps its single grid and its collapsible floorplan, and filters the rows
to the selected types. The Assets tab stacks one `ResourceUtilizationGrid` per selected
type, which reuses the grid unchanged.

Auto-schedule needs exactly one type. When the selection holds one type, the button is active. In every
other case it is inactive, with a tooltip that gives the reason. This generalizes the
present rule, which enables the button only when the tenant has one placeable type
(`UtilizationPage.tsx:210-214`).

Export follows the selection of the active tab.

## 7. People

The person type becomes an ordinary resource type. This merge is a prerequisite for the
class pages, because `person` is the last type with a dedicated page.

The behaviour flag `has_directory_profile` drives the extra behaviour, in place of the
hardcoded `person` key:

- `ResourceList` shows the directory columns for a type with a directory profile. The data
  is already on `resources` after migrations 1700 and 1710.
- `ResourceEditDialog` shows the directory fields under the same condition.
- Skills unify onto `ResourceCapabilitiesEditor`, which is already type-aware
  (`frontend/src/components/resources/ResourceCapabilitiesEditor.tsx:24-25`).
  `PersonSkillsEditor` is a parallel implementation of the same concept, and is deleted.
- Absences need no work. `resource_absences` is generic to any resource.

`frontend/src/components/people/` and `frontend/src/pages/PeoplePage.tsx` are deleted after
the generic pages reach parity. Their tests move to the generic components.

The person type keeps `is_system = true`. The merge removes its dedicated page, not its
protection.

## 8. Lists

### 8.1 Definitions and values

The distinction exists in the schema, and stays:

- A **list definition** describes an attribute. It owns typed **columns**
  (`list_definitions`, `list_columns`).
- A **list instance** holds the values as **rows** (`list_instances`, `list_rows`).

`list_instances.kind` separates a `shared` instance, which carries a name, from a `resource`
instance, which belongs to one resource and one custom field. That mechanism is unchanged.

Custom fields bind to lists through two data types, both unchanged: `list` binds a
definition and gives every resource its own rows. `list_lookup` binds one shared instance
and stores the selected row identifiers.

### 8.2 Scopes

The specification asks for three scopes. `list_definitions` today has no scope and no owner:
its name is unique across the tenant. Migration `19xx` adds ownership:

```sql
ALTER TABLE list_definitions
  ADD COLUMN scope TEXT NOT NULL DEFAULT 'common'
    CHECK (scope IN ('resource', 'organization', 'common')),
  ADD COLUMN resource_type_id UUID NULL REFERENCES resource_types(id),
  ADD CONSTRAINT list_definitions_scope_owner_check
    CHECK ((scope = 'resource') = (resource_type_id IS NOT NULL));
```

The unique constraint on `name` relaxes to `(scope, resource_type_id, name)`, so that two
types can each own a definition called "Certification".

An explicit scope column is necessary. A derived scope cannot separate `organization` from
`common`, because both leave `resource_type_id` empty.

The backfill sets every existing definition to `common`, and the demo seed keeps its Tooling
Catalog and Maintenance Log there. An earlier draft of this document said those two become
`resource`-scoped. That was wrong: all three machine types share them, and one
`resource_type_id` cannot express an owner that is three types. A definition serving several
types stays `common`. The `resource` scope is for a definition one type owns alone.

### 8.3 Where each scope appears

| Scope | Owner | Values edited at |
|---|---|---|
| `resource` | one resource type | the Lists tab of that type |
| `organization` | the tenant organization | `/organization` |
| `common` | the tenant | `/configuration/list-definitions` |

Definition CRUD stays in `/configuration/list-definitions` for every scope, and that page
gains a scope selector. The Organization page edits values, not definitions.

Common lists have no sidebar entry. Reference data such as countries and units is
administrative, and the Configuration area holds it. This is a deliberate deviation
from the source document, recorded in section 10.

### 8.4 Departments and job titles

Departments and job titles become `organization`-scoped list definitions. Their tables,
their endpoints and their settings components are deleted.

This is a deliberate decision, taken with knowledge of the following costs:

1. **The department tree flattens.** `departments.parent_department_id` has no equivalent in
   `list_rows`, whose values are flat JSON. A parent column can hold a text value. The database no longer
   enforces the tree, the self-reference constraint, or the sibling-name uniqueness.

   *Superseded (2026-08-20).* Migrations 1890 and 1900 gave the tree back, as the `row_ref`
   column type: a cell holds the id of another row of the same list, and `ListRowService` checks
   on write that the target exists in that list, is not the row itself, and closes no cycle. The
   database still does not enforce it — every row lives in one table, so a foreign key could not
   tell a sibling from a row of another instance — but the application does, and the department
   parent is a reference again rather than a name.
2. **Referential integrity ends.** `resources.department_id` and `resources.job_title_id`
   are foreign keys. They become `list_lookup` custom fields on the person type, whose value
   is a JSON array of row identifiers with no foreign key behind it.
3. **The row cap applies.** `ListRowService.MaxRowsPerInstance` is 500. A tenant with more
   than 500 departments cannot model them as a list.
4. **The work is a contract migration.** The classification rules in
   `orkyo-infra/docs/migrations/classification.md` apply, and the deploy gate enforces them.

The blast radius is contained. The dependent code is
`backend/core/Repositories/DepartmentRepository.cs`, `PersonProfileRepository.cs`,
`ResourceRepository.cs`, `backend/core/Services/ResourceService.cs`,
`backend/seeding/Factories/PeopleFactories.cs`, and the frontend components
`DepartmentSettings.tsx`, `JobTitleSettings.tsx`, `DepartmentEditDialog.tsx` and
`PersonEditDialog.tsx`. Insights, export, orkyo-saas and orkyo-community hold no reference
to either column.

The migration runs in the expand-and-contract shape of this repository. The expand step
seeds the two definitions and copies the rows. The application then moves to the list
fields. A later contract step deletes the tables.

### 8.5 The Lists tab

The Lists tab of a class page shows the definitions the selected type's `list_lookup` custom
fields bind, whatever their scope. For each it shows the rows of the bound shared instance,
through the existing row editor components in `frontend/src/components/lists/`.

Ownership is not the test here, and an earlier draft of this document said it was. A shared
catalogue such as the demo's Tooling Catalog is `common`. An ownership test therefore hides from
the mill page exactly the list a reader opened it for. A reader wants "the lists this type uses".
Scope decides where a list is *administered*, which is a different question.

Per-resource instances stay where they are today, in the custom-field editor of the resource
row. The tab covers the shared catalogs that the type owns.

When the type owns no definition, the tab is hidden. It is not disabled and not empty. The
definitions query runs with the other data of the page, so the tab set does not flicker.

## 9. Organization

`/organization` holds the organization-scoped list definitions and their values. Departments
and job titles appear there as two seeded organization lists, beside any list that the
tenant adds, such as cost centres.

The page reads members and writes editors, which matches the governance of list rows today:
`ListInstanceEndpoints` requires authentication for row work, while definition work is
admin-only.

## 10. Deviations from the source document

Two decisions differ from the source specification. Both are deliberate.

1. **No "Common" navigation entry.** The source document asks for Organization and Common as
   sidebar entries. Common lists stay in the Configuration area, because reference data is
   administrative and the sidebar stays shorter.
2. **Departments and job titles migrate into lists.** The source document models them as
   organization lists, and this specification follows it. The alternative was preservation of
   the two tables with presentation inside the Organization area, which keeps foreign keys
   and the tree. Section 8.4 records the costs of the chosen option.

## 11. Phases

Each phase is one pull request, in dependency order.

1. **Person merge.** `has_directory_profile` drives the directory columns and fields. Skills
   unify, and `components/people/` is deleted. Routes stay as they are.
2. **List scopes.** Migration `19xx`, the API and DTO changes, the scope selector, and the
   seed update. Independent of phase 1.
3. **Department and job-title migration.** Expand, application cutover, contract.
4. **Class pages.** `resourceClass` in the API, the class page and its primitives, the
   `/stations` and `/assets` routes, every redirect, the sidebar and the command palette.
   Depends on phases 1 and 2.
5. **Organization page.** The organization scope surface.
6. **Floorplan fold-in.** `/stations/floorplan`, and retirement of `/floorplan`.
7. **Utilization.** Three tabs, the multiselect, the stacked asset grids, and the
   auto-schedule and export rules.

## 12. Acceptance criteria

1. The sidebar holds no entry that names a resource type.
2. A new resource type appears in the type selector of its class, and changes no route, tab
   or navigation entry.
3. `/stations/:typeKey` and `/assets/:typeKey` show the type's own plural, Groups and, under the
   rule of section 8.5, Lists.
4. The tab set does not change when the type selector changes, except for the Lists tab.
5. Every redirect of section 4.1 answers with the new location.
6. `frontend/src/constants/resource-type-key.ts` holds no dedicated-page exception.
7. The person type renders through the generic pages, and keeps its directory columns, its
   skills, its absences and its group label "Team".
8. `list_definitions` holds a `scope` column with the three values, and the paired CHECK
   rejects an inconsistent row.
9. Two resource types can each own a definition with the same name.
10. The Lists tab is absent for a type that owns no definition.
11. `/organization` shows departments and job titles as organization lists.
12. The Utilization page holds three tabs, and each grid tab filters through a multiselect.
13. The auto-schedule button is active only when the multiselect holds one type.
14. `?stationTypes=` reproduces a grid selection in a new browser session, and a type added
    afterwards appears rather than staying hidden.
15. Existing resources, groups, assignments and list rows survive every phase.
16. Authorization and tenant isolation are unchanged for every list scope.

## 13. Verification

Each phase runs the full backend suite, the frontend suite with `tsc` and `eslint`, the
seeding suites, and `./scripts/test-downstream.sh`. Phases 2 and 3 also verify the migration
against a real Postgres, in the shape of migration 1780. That shape is the full chain, the
constraint behaviours, the revert, and the re-apply.

Phase 3 needs a data check. A demo reset must show the same department names and the same
person-to-department relations before and after the migration.

The visual surfaces of phases 4 to 7 need sign-off before a push.
