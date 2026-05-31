# People Resources — Implementation Plan (v1)

This plan supersedes the original phased plan. It assumes the resource-model
refactor (migrations 1300–1350) is merged and reflects §18 of
[01_people_resources_specification.md](01_people_resources_specification.md).

**Read first:**
- [01_people_resources_specification.md §18](01_people_resources_specification.md) — codebase reality and decisions
- [STATUS.md](STATUS.md) — current progress (the implementing agent updates this after every phase)

**Hard rules for the implementer:**
1. Do **not** recreate generic resource infrastructure. Reuse `resources`, `resource_groups`, `resource_capabilities`, `resource_assignments`, `off_times`, `scheduling_settings`.
2. Every behavioral change ships with tests in the same PR.
3. Update [STATUS.md](STATUS.md) at the end of every phase (and at any meaningful checkpoint within a phase). Treat that file as the single source of truth for progress.
4. Migrations carry the `-- @migration-class:` header (see `orkyo-infra/docs/migrations/classification.md`).
5. Do not break the existing `/api/resources*`, `/api/utilization`, `/api/criteria*` contracts.

---

## Phase 0 — Kickoff & baseline

**Goal:** record starting state in STATUS.md and verify the working tree is clean.

- Read 01 §18 in full.
- Verify `dotnet build` (foundation slnx) and `pnpm -C frontend test` are green on `main`.
- In STATUS.md, set Phase 0 = ✅ with the commit SHA used as baseline.

---

## Phase 1 — Schema deltas

**Goal:** add the small set of columns/tables the spec requires on top of the resource model.

Files:
- New migration `backend/migrations-foundation/sql/tenant/1400.foundation.people.sql`
  (use the next free number; verify against the directory).
- Header: `-- @migration-class: schema-additive`

Schema:
```sql
-- person_profiles: per-resource extras for type='person'
CREATE TABLE person_profiles (
    resource_id   UUID PRIMARY KEY REFERENCES resources(id) ON DELETE CASCADE,
    email         CITEXT NULL,
    job_title     VARCHAR(200) NULL,
    department    VARCHAR(200) NULL,
    linked_user_id UUID NULL UNIQUE REFERENCES users(id) ON DELETE SET NULL,
    notes         TEXT NULL,
    created_at    TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at    TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX ix_person_profiles_linked_user ON person_profiles(linked_user_id) WHERE linked_user_id IS NOT NULL;

-- group default availability
ALTER TABLE resource_groups
    ADD COLUMN default_availability_percent INT NOT NULL DEFAULT 100
        CHECK (default_availability_percent BETWEEN 0 AND 100);

-- assignment role/notes
ALTER TABLE resource_assignments
    ADD COLUMN role  VARCHAR(100) NULL,
    ADD COLUMN notes TEXT NULL;
```

Tests:
- Add a migrator integration test that asserts the new columns/tables exist after running migrations against a clean DB.

Update [STATUS.md](STATUS.md) → Phase 1 ✅ with migration filename + columns added.

---

## Phase 2 — Wire off-time + typed capability checks at assignment time

**Goal:** close the two known gaps in `ResourceAssignmentService.CreateAsync` so any People assignment is safe.

Files:
- `backend/src/Services/ResourceAssignmentService.cs`
- `backend/src/Services/CapabilityMatcher.cs` (no change expected — already typed)
- Inject `IOffTimeResourceQuery` (or equivalent) into the assignment service.

Behavior:
- Before creating an assignment, query overlapping `off_times` for the target resource (per-resource and global). Block if any overlap and the assignment is `Exclusive`; warn otherwise.
- Switch the capability check from `ResourceSatisfiesRequirementsAsync` (presence) to `ResourceSatisfiesRequirementAsync` (typed) and aggregate the result per criterion.
- Keep the public method signature and `ResourceConflict` shape backwards-compatible.

Tests (xUnit, foundation `backend/tests`):
- `Creates_assignment_when_no_offtime_overlap`
- `Blocks_exclusive_assignment_overlapping_offtime`
- `Allows_fractional_assignment_overlapping_offtime_with_warning`
- `Blocks_assignment_when_required_typed_capability_missing`
- `Allows_assignment_when_typed_capability_satisfied`

Run the existing space allocation tests to confirm no regression.

Update [STATUS.md](STATUS.md) → Phase 2 ✅ with test names added.

---

## Phase 3 — Validation dry-run endpoint

**Goal:** let the request dialog surface blockers/warnings without committing.

Files:
- New `backend/src/Services/ResourceAssignmentValidator.cs` with `IResourceAssignmentValidator`.
- New endpoint `POST /api/resource-assignments/validate` in `ResourceEndpoints.cs` (or a new `ResourceAssignmentEndpoints.cs`).

Contract:
```
POST /api/resource-assignments/validate
{ requestId, resourceId, allocationPercent, periodStart, periodEnd, allocationMode }
→ 200 { severity: "ok"|"warning"|"blocker", blockers: [...], warnings: [...] }
```

Reasons surfaced (string codes, stable):
- `resource.inactive`
- `resource.type-mismatch`
- `capability.missing` (with criterionKey + expected/actual)
- `offtime.overlap` (with offTimeId, type)
- `assignment.overbooked` (with overlapping window + total %)
- `nonworking.weekend`
- `nonworking.holiday`

Implementation: factor the existing checks in `CreateAsync` into the validator, then make `CreateAsync` call the validator with `severity != "blocker"` as the gate.

Tests:
- One test per reason code (happy + sad).
- A test that proves `CreateAsync` rejects when validator says `blocker`.

Update [STATUS.md](STATUS.md) → Phase 3 ✅.

---

## Phase 4 — Person profile API

**Goal:** expose the `person_profiles` row + user-link/unlink.

Files:
- New `backend/src/Repositories/PersonProfileRepository.cs` + `IPersonProfileRepository`.
- New `backend/src/Endpoints/PersonProfileEndpoints.cs` mounted from the same place as `MapResourceEndpoints()`.

Endpoints:
```
GET    /api/person-profiles/{resourceId}
PUT    /api/person-profiles/{resourceId}        # upsert (email, job_title, department, notes)
POST   /api/person-profiles/{resourceId}/link   { userId }
DELETE /api/person-profiles/{resourceId}/link
```

Rules:
- The target resource must exist and have `resource_type.key='person'`. Otherwise 400.
- `userId` must belong to the same tenant. `linked_user_id` is unique per tenant; conflict → 409.
- Authorization: same admin role required by existing `/api/resources` write endpoints.

Tests:
- Profile upsert + read.
- Link/unlink happy path.
- 400 when resource is not a person.
- 409 on duplicate link.
- Cross-tenant link is rejected.

Update [STATUS.md](STATUS.md) → Phase 4 ✅.

---

## Phase 5 — Frontend: People page

**Goal:** ship the People management surface in foundation.

Files:
- New `frontend/src/pages/PeoplePage.tsx` with three tabs: **People**, **Groups**, **Absences**.
- New components under `frontend/src/components/people/`:
  - `PersonList.tsx`, `PersonEditDialog.tsx`
  - `PeopleGroupList.tsx`, `PeopleGroupEditDialog.tsx`
  - `PersonAbsenceList.tsx`, `PersonAbsenceEditDialog.tsx`
- Route: add `<Route path="people" element={<PeoplePage />} />` in `frontend/src/components/auth/TenantApp.tsx` between `/spaces` and `/requests`. Add `"/people"` to `APP_LAYOUT_PREFIXES`.
- Nav: add `{ to: "/people", label: "People", icon: Users }` to `navItems` in `frontend/src/components/layout/SidebarNav.tsx` between Spaces and Requests. Import `Users` from `lucide-react`.

Data:
- Reuse the existing `resourcesApi` with `resourceTypeKey=person` filter.
- New `personProfilesApi.ts` for the Phase 4 endpoints.
- Reuse existing capability + absence endpoints; in the skill picker, restrict the criterion list to those whose applicability includes `person` (use `/api/criteria/{id}/applicability`).

Tests (Vitest):
- `PeoplePage.test.tsx`: renders three tabs, switches between them.
- `PersonEditDialog.test.tsx`: validates required fields, calls profile + resource APIs.
- `PeopleGroupEditDialog.test.tsx`: edits `default_availability_percent`.

Update [STATUS.md](STATUS.md) → Phase 5 ✅.

---

## Phase 6 — Request dialog: People assignment section

**Goal:** allow assigning People to Requests with live validation.

Files:
- Existing request-edit dialog (locate via grep for `RequestEditDialog` or equivalent in `frontend/src/components/requests/`). Add a **People / Resources** section.

Behavior:
- List current assignments where `resource.type='person'`.
- "Add" opens a row with: person picker, allocation %, role, notes.
- On change, call `POST /api/resource-assignments/validate` (debounced) and render blockers (red) / warnings (amber) inline.
- Save button is disabled while there is any blocker.

Tests:
- Validation feedback rendered for each reason code.
- Save disabled when blocker present.

Update [STATUS.md](STATUS.md) → Phase 6 ✅.

---

## Phase 7 — Utilization page: People grid

**Goal:** add a collapsible People grid to the existing utilization page.

Files:
- Existing `frontend/src/pages/UtilizationPage.tsx` (or equivalent). Insert between Floorplan and Space grid.
- Reuse the existing date range + zoom store; do not introduce parallel state.

Data:
- `GET /api/utilization?resourceTypeKey=person&from=…&to=…`. The endpoint exists.

Statuses to render per cell: `available | partial | absent | assigned | overbooked | skill-mismatch | nonworking`.

Tests:
- Component test: collapsed by default, expands, renders statuses for fixture data.

Update [STATUS.md](STATUS.md) → Phase 7 ✅.

---

## Phase 8 — Polish, docs, release notes

- Update `README.md` (foundation) with a one-paragraph note on People resources.
- Add a short section to `frontend/ARCHITECTURE.md` describing the People page split.
- Verify SaaS + Community pick up the changes via the foundation NuGet/npm bumps (no per-product code expected).
- In STATUS.md, set Phase 8 ✅ and add a "v1 done" line with the merge commit.

---

## Deferred (explicitly out of scope for v1)

Captured in 01 §18 — do not implement in this initiative:
- User invitation flow.
- Group default capabilities.
- Group-scoped absences.
- Regional holiday provider.
- Drag-and-drop assignments.
- `ConcurrentCapacity` allocation mode.

When any of these is picked up, open a new spec pack under `requirements/`.
