# People Resources — Implementation Status

**Single source of truth for progress on this initiative.**
The implementing agent updates this file at the end of every phase and at any
meaningful checkpoint (blocker found, scope change, partial PR merged).

> Rules for the implementing agent:
> 1. Read this file before starting any work in a session.
> 2. After finishing a phase, flip its checkbox to ✅, fill in the date, the
>    commit / PR link, the test names added, and any deviations from
>    [02_people_resources_implementation_plan.md](02_people_resources_implementation_plan.md).
> 3. If a phase is paused or blocked, mark it 🟡 and write the blocker under
>    "Open issues / blockers".
> 4. If a decision in [01_people_resources_specification.md §18](01_people_resources_specification.md)
>    has to change, record it under "Decision changes" with rationale **before**
>    coding around it.
> 5. Never delete history from this file — append, don't rewrite.

---

## Summary

| Phase | Title | Status | Date | Commit / PR |
|------:|-------|:------:|------|-------------|
| 0 | Kickoff & baseline | ✅ | 2026-01-15 | 9405645 |
| 1 | Schema deltas | ✅ | 2026-01-15 | Migration 1400 + tests |
| 2 | Off-time + typed capability checks | ✅ | 2026-05-15 | Behaviour delivered via the validator created in Phase 3; CreateAsync delegates. See revised Phase 2 log. |
| 3 | Validation dry-run endpoint | ✅ | 2026-05-15 | Validator split into per-check methods; weekend + holiday checks added; ValidationIssue→ResourceConflict converter rewritten as dictionary (was brittle string-switch). See revised Phase 3 log. |
| 4 | Person profile API | ✅ | 2026-05-15 | Endpoints wired in foundation test factory, SaaS, and Community Program.cs. SQL syntax in `UnlinkUserAsync`/`LinkUserAsync`/`UpsertAsync` fixed (3× malformed `@$` literals); CITEXT email cast to text for Npgsql read; link auto-upserts profile; FluentValidator + input length limits added. 7 integration tests passing. |
| 5 | Frontend: People page | ✅ | 2026-05-15 | All three tabs complete: People tab (PersonEditDialog saves resource + person-profile); Groups tab (ResourceGroupEndpoints, PeopleGroupList/PeopleGroupEditDialog); Absences tab (PersonAbsenceList with site picker + useQueries, PersonAbsenceEditDialog). 30 vitest tests across 5 test files. |
| 6 | Request dialog: People assignment | ✅ | 2026-05-16 | `RequestPeopleSection` component with debounced validation, blocker/warning feedback, add/delete assignments; wired into `RequestFormDialog`; save button gated on blockers. 12 vitest tests. |
| 7 | Utilization page: People grid | ✅ | 2026-05-16 | See Phase 7 log. |
| 8 | Polish, docs, release notes | ⬜ | | |

Legend: ⬜ not started · 🟡 in progress / blocked · ✅ done

---

## Phase logs

### Phase 0 — Kickoff & baseline
- Status: ✅
- Baseline commit: `94056457c931121c007eadbaab39b82924e250c6`
- Notes: Completed 2026-01-15. Baseline commit includes the Phase 6 Read-Model Unification implementation. All source and test files verified clean of `primaryResourceId`/`PrimaryResourceId` references.

### Phase 1 — Schema deltas
- Status: ✅
- Migration file: `1400.foundation.people.sql`
- Columns / tables added: `person_profiles` table, `resource_groups.default_availability_percent`, `resource_assignments.role`, `resource_assignments.notes`
- Tests added: `TestTenant_ShouldContain_PersonProfilesTable`, `TestTenant_ResourceGroups_ShouldHave_DefaultAvailabilityPercentColumn`, `TestTenant_ResourceAssignments_ShouldHave_RoleColumn`, `TestTenant_ResourceAssignments_ShouldHave_NotesColumn` in `MigrationHarnessSmokeTests.cs`
- Notes: Delivered 2026-05-15. Migration created with `-- @migration-class: schema-additive` header. Revert script created at `1400.foundation.people.revert.sql`. Tests validate schema exists after migration.

### Phase 2 — Off-time + typed capability checks
- Status: ✅ (revised 2026-05-15)
- Initial implementation (2026-01-15) injected `IOffTimeResourceQuery`, `IRequestRepository`, `ISchedulingRepository` directly into `ResourceAssignmentService`. **Phase 3 superseded this**: the same behaviours moved into `ResourceAssignmentValidator`, and `ResourceAssignmentService.CreateAsync` became a thin delegation layer. The Phase 2 "off-time check" and "typed capability check" tests in `ResourceAssignmentServiceTests.cs` were dropped on 2026-05-15 because they tested the obsolete inline shape; the validator tests in `ResourceAssignmentValidatorTests.cs` cover the same scenarios.
- Files final state:
  - `backend/src/Services/ResourceAssignmentService.cs`: 2-arg ctor (assignment repo + validator); delegates to validator and translates the first blocker into the legacy `ResourceConflict` shape via a `ValidationReasonCode→ResourceConflictType` dictionary.
- Behaviour delivered: off-time overlap raises warnings (holidays surface as a distinct `NonWorkingHoliday` code, others as `OffTimeOverlap`); typed capability checks via `ResourceSatisfiesRequirementAsync`.

### Phase 3 — Validation dry-run endpoint
- Status: ✅ (revised 2026-05-15)
- Endpoint: `POST /api/resource-assignments/validate`
- Reason codes implemented (all 10): `resource.not-found`, `resource.inactive`, `resource.type-mismatch`, `capability.missing`, `offtime.overlap`, `assignment.overbooked`, `nonworking.weekend`, `nonworking.holiday`, `allocation-mode.invalid`, `allocation-percent.invalid`
- Cleanup 2026-05-15:
  - 185-line `ValidateAsync` method split into per-check private methods (`CheckCapabilitiesAsync`, `CheckSiteScopedWindowAsync` → `CheckOffTimesAndHolidaysAsync` + `CheckWeekendsAsync`, `CheckAllocationAsync`). Orchestrator is one screen.
  - **Weekend and holiday checks implemented** (were stub `Assert.Equal(Ok)` placeholders). Weekends use `SchedulingEngine.IsWeekend` (made public) and respect `scheduling_settings.weekends_enabled`. Holidays are distinguished from generic off-times by `OffTimeType.Holiday` and surface as the dedicated `NonWorkingHoliday` reason code.
  - `ValidationReasonCode → ResourceConflictType` mapping in `ResourceAssignmentService` rewritten from brittle string-switch to a `Dictionary` with explicit `InvalidOperationException` on unmapped codes.
  - Mock-default returns added to `ResourceAssignmentValidatorTests` constructor (Moq returned `null` Tasks → NRE on iteration).
- Tests in `ResourceAssignmentValidatorTests.cs` (14 passing): all 10 reason codes covered. `NonWorkingWeekend_WeekendsEnabled_DoesNotWarn` added to prove the `weekends_enabled=true` negative path.

### Phase 4 — Person profile API
- Status: ✅ (revised 2026-05-15 — original 2026-01-15 impl was build-broken)
- Endpoints: `GET /api/person-profiles/{resourceId}`, `PUT /api/person-profiles/{resourceId}`, `POST /api/person-profiles/{resourceId}/link`, `DELETE /api/person-profiles/{resourceId}/link`
- Wiring: `MapPersonProfileEndpoints()` now called from `FoundationWebApplicationFactory` (tests), `orkyo-saas/backend/api/Program.cs`, and `orkyo-community/backend/api/Program.cs`. SaaS additionally dropped 3 dead references to the removed `SpaceCapability*` symbols (a Phase 2 resource-model carryover).
- Bugs fixed during cleanup:
  - **Build was broken**: 3× malformed `@$` SQL string literals in `PersonProfileRepository` (54 cascading compile errors). Fixed.
  - **CITEXT read**: Npgsql 8+ has no `CITEXT` handler; `SelectColumns` now casts `email::text AS email`.
  - **Link UX**: `LinkUserAsync` was a conditional UPDATE that failed when no profile row existed yet; rewritten as `INSERT … ON CONFLICT DO UPDATE` so admins can link without first manually saving an empty profile.
  - **Upsert SQL**: collapsed branched INSERT/UPDATE into a single `INSERT … ON CONFLICT DO UPDATE` round-trip (DRY); extracted `NullableParam` helper.
  - **Endpoint chain**: `.RequireAdminAccess()` was on the wrong builder type; moved to each write endpoint.
  - **Magic strings**: extracted `NotAPersonMessage` const + extracted the repeated "load resource + assert person" preamble into `ResolvePersonResourceAsync`.
  - **Input validation**: added `UpsertPersonProfileRequestValidator` (FluentValidation): email format, 254-char limit, 200-char limits on jobTitle/department.
- Tests in `PersonProfileEndpointsTests.cs` (7 passing, no longer `[Skip]`): coverage for not-found / not-a-person / upsert round-trip / link / 409 duplicate-link / unlink / unlink-not-linked. Tests use the existing `CreateTestUserAsync` helper so FK constraints are honoured.

### Phase 5 — Frontend: People page
- Status: ✅ (completed 2026-05-15; Groups + Absences tabs completed in follow-up)
- Route + nav wiring: Added `/people` route in TenantApp.tsx between `/spaces` and `/requests`; added `{ to: "/people", label: "People", icon: Users }` to navItems in SidebarNav.tsx
- Components added: `PeoplePage.tsx`, `components/people/` (PersonList, PersonEditDialog, PeopleGroupList, PeopleGroupEditDialog, PersonAbsenceList, PersonAbsenceEditDialog)
- Tests added: 30 vitest tests across 4 files:
  - `PersonEditDialog.test.tsx` — 4 tests: field rendering, create/edit API calls, Save button guard
  - `PeopleGroupList.test.tsx` — 8 tests: loading state, empty state, group rows, CRUD actions
  - `PeopleGroupEditDialog.test.tsx` — 9 tests: create/edit titles, field population, validation, API calls
  - `PersonAbsenceList.test.tsx` — 7 tests: site selector, empty state, absence rows, delete
  - `PersonAbsenceEditDialog.test.tsx` — 6 tests: field rendering, person fetch, Save guard, Cancel
- Backend additions (Groups + Absences follow-up):
  - `backend/src/Models/ResourceGroup.cs` — `ResourceGroupInfo`, `CreateResourceGroupRequest`, `UpdateResourceGroupRequest`
  - `backend/src/Repositories/ResourceGroupRepository.cs` — CRUD repository with `IResourceGroupRepository` interface; joins `resource_types` by `key`
  - `backend/src/Endpoints/ResourceGroupEndpoints.cs` — `GET /api/resource-groups?resourceTypeKey=`, `POST`, `PUT /{id}`, `DELETE /{id}`
  - `backend/src/Validators/ResourceGroupRequestValidator.cs` — FluentValidation for create + update
  - Community `Program.cs`: registered `IResourceGroupRepository` + `MapResourceGroupEndpoints()`
- Frontend API clients added:
  - `frontend/src/lib/api/resource-groups-api.ts` — `getResourceGroups`, `createResourceGroup`, `updateResourceGroup`, `deleteResourceGroup`
  - `frontend/src/lib/api/resource-absences-api.ts` — `getResourceAbsences`, `createResourceAbsence`, `deleteResourceAbsence`
  - `frontend/src/lib/core/api-paths.ts` — added `RESOURCE_GROUPS`, `resourceGroup`, `resourceAbsences`, `resourceAbsence` paths
- API client bugs fixed (2026-05-15 cleanup):
  - `resources-api.ts`: `API_PATHS.resources` → `API_PATHS.RESOURCES` (2 occurrences; was causing 404 at runtime)
  - `person-profiles-api.ts`: `apiDelete<void>(...)` → `apiDelete(...)` (invalid generic removed)
  - `RequestAssignmentsSection.tsx`: **deleted** — premature Phase 6 work that imported a non-existent `resource-assignment-api` module
- Notes: Groups tab uses real `/api/resource-groups` CRUD with `resourceTypeKey=person` filter. Absences tab has a site picker; loads all people, then uses `useQueries` for per-person absences, merged into a flat sorted table. `PersonAbsenceEditDialog` requires `siteId` prop.

### Phase 6 — Request dialog: People assignment
- Status: ✅ 2026-05-16
- Files changed:
  - `backend/src/Endpoints/ResourceAssignmentEndpoints.cs` — added `GET /api/resource-assignments?requestId=` endpoint
  - `frontend/src/lib/core/api-paths.ts` — added `RESOURCE_ASSIGNMENTS`, `RESOURCE_ASSIGNMENT_VALIDATE`, `resourceAssignment(id)` paths
  - `frontend/src/lib/api/resource-assignments-api.ts` (new) — `getAssignmentsByRequest`, `validateAssignment`, `createAssignment`, `cancelAssignment`
  - `frontend/src/components/requests/RequestPeopleSection.tsx` (new) — collapsible people assignment section
  - `frontend/src/components/requests/RequestFormDialog.tsx` — wired `RequestPeopleSection`; save button gated on `hasPeopleBlockers`
- Tests added: 12 in `RequestPeopleSection.test.tsx`
- Notes: Section shows existing assignments loaded on open (when `requestId` present), add-row with person picker + allocation %, debounced validation (500 ms), inline blocker (red) / warning (amber) badges, delete with confirm. `onBlockersChange` propagates blocker state to parent so the dialog Save button is disabled while hard conflicts exist.

### Phase 7 — Utilization page: People grid
- Status: ✅ 2026-05-16
- Files changed: `frontend/src/lib/core/api-paths.ts`, `frontend/src/lib/api/resources-api.ts`, `frontend/src/components/utilization/PeopleUtilizationGrid.tsx` (new), `frontend/src/pages/UtilizationPage.tsx`
- Tests added: 11 in `PeopleUtilizationGrid.test.tsx`
- Notes: Collapsible people utilization grid inserted between Floorplan and Space grid (spec §10). Uses `useQueries` for parallel per-person `GET /api/resources/{id}/utilization` calls. `getViewWindow(anchorTs, scale)` aligns query window to the current view. Bucket status → colour: available=emerald, partial=amber, assigned=blue, overbooked=red, non-working=muted. Returns `null` when people list is loading or empty, so existing UtilizationPage tests need no new mocks.

### Phase 8 — Polish, docs, release notes
- Status: ⬜
- Docs touched:
- Release notes line:

---

## Decision changes

Append entries here whenever a decision in 01 §18 has to be revised. Format:

```
- YYYY-MM-DD — <decision id / topic>
  Old: <what 01 §18 said>
  New: <what we are doing instead>
  Why: <rationale>
  Phase impact: <which phases need to be redone or amended>
```

(none yet)

---

## Open issues / blockers

Append entries here when a phase is paused. Remove the entry (and add a closing
note in the phase log) once it's resolved.

### 2026-05-15 — SaaS Program.cs missing resource-model endpoints

Separate from People, the SaaS Program.cs is missing all `MapResource*` /
`MapCriterion*` / `MapUtilizationEndpoints` calls that Community already has.
The People feature CANNOT ship to SaaS until that wiring is added. Out of
scope for this initiative; flagged here so it surfaces during a SaaS-rollout
phase.

---

## Deferred items (tracked, not implemented in v1)

Mirrors 01 §18 / 02 "Deferred" — listed here so future sessions can find them:

- User invitation flow
- Group default capabilities
- Group-scoped absences
- Regional holiday provider
- Drag-and-drop assignments
- `ConcurrentCapacity` allocation mode
