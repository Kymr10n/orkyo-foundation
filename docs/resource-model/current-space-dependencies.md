# Current Space Dependencies — Phase 0 Inventory

Produced 2026-05-14 as the Phase 0 deliverable. This document is
the ground truth for everything Phase 2 must touch during the
rename + cutover.

---

## 1. Tables with FK to `spaces(id)`

| Table                | Column     | ON DELETE      | Rename target              |
|----------------------|------------|----------------|----------------------------|
| `requests`           | `space_id` | SET NULL       | Column **dropped** (Phase 2) |
| `space_capabilities` | `space_id` | CASCADE        | → `resource_capabilities.resource_id` |
| `off_time_spaces`    | `space_id` | CASCADE        | → `off_time_resources.resource_id` |

---

## 2. Backend `SpaceId` / `space_id` references

**112 occurrences across 22 files.**

### Endpoints (3 files)

| File | Role |
|------|------|
| `backend/src/Endpoints/RequestEndpoints.cs` | Exposes `SpaceId` on request DTO |
| `backend/src/Endpoints/SpaceCapabilityEndpoints.cs` | Space capability CRUD |
| `backend/src/Endpoints/SpaceEndpoints.cs` | Space CRUD |

### Models (3 files)

| File | Fields |
|------|--------|
| `backend/src/Models/Request.cs` | `SpaceId` property on `RequestInfo` |
| `backend/src/Models/AutoSchedule.cs` | `SpaceId` on `ProposedAssignmentDto` |
| `backend/src/Models/Scheduling.cs` | `AppliesToAllSpaces`, `SpaceIds` on off-time models |

### Repositories (6 files)

| File | Role |
|------|------|
| `backend/src/Repositories/RequestRepository.cs` | All space_id read/write (see §4) |
| `backend/src/Repositories/RequestMapper.cs` | Maps `space_id` column → `SpaceId` |
| `backend/src/Repositories/SchedulingRepository.cs` | `off_time_spaces` queries, `applies_to_all_spaces` |
| `backend/src/Repositories/SchedulingMapper.cs` | Maps `applies_to_all_spaces` → `AppliesToAllSpaces` |
| `backend/src/Repositories/SpaceCapabilityRepository.cs` | `space_id` column in all queries |
| `backend/src/Repositories/ISpaceCapabilityRepository.cs` | Interface |
| `backend/src/Repositories/SpaceCapabilityMapper.cs` | Mapper |

### Services (6 files)

| File | Role |
|------|------|
| `backend/src/Services/SchedulingService.cs` | Reads `SpaceId` to load scheduling context (lines 110, 156) |
| `backend/src/Services/ExportService.cs` | Maps `AppliesToAllSpaces` into export payload |
| `backend/src/Services/AutoSchedule/AutoScheduleService.cs` | Reads `SpaceId` from assignment results |
| `backend/src/Services/AutoSchedule/SchedulingProblemBuilder.cs` | Builds space requirement from `SpaceId` |
| `backend/src/Services/AutoSchedule/SchedulingFeasibilityAnalyzer.cs` | Per-space capability matching; honors `AppliesToAllSpaces` |
| `backend/src/Services/AutoSchedule/GreedySchedulingSolver.cs` | Solver operand |
| `backend/src/Services/AutoSchedule/OrToolsSchedulingSolver.cs` | Solver operand |

### Validators (3 files)

| File | Role |
|------|------|
| `backend/src/Validators/CreateRequestRequestValidator.cs` | Validates `SpaceId` format |
| `backend/src/Validators/ScheduleRequestRequestValidator.cs` | `SpaceId` required on scheduling |
| `backend/src/Validators/SchedulingValidators.cs` | `SpaceIds is required when AppliesToAllSpaces is false` (line 116) |

### Other (1 file)

| File | Role |
|------|------|
| `backend/src/Models/Export/ExportPayload.cs` | `AppliesToAllSpaces` on export DTO |

---

## 3. Frontend `spaceId` references

**431 occurrences across 94 files.**

### Contracts (2 files)

- `frontend/contracts/autoSchedule.ts` — `ProposedAssignmentDto.spaceId`
- `frontend/contracts/request-contract.test.ts` — contract snapshot

### API layer (8 files)

- `frontend/src/lib/api/space-api.ts`
- `frontend/src/lib/api/space-capability-api.ts` + `.test.ts`
- `frontend/src/lib/api/request-api.test.ts`
- `frontend/src/lib/api/scheduling-api.ts` + `.test.ts`
- `frontend/src/lib/api/auto-schedule-api.ts`
- `frontend/src/lib/api/utilization-api.ts` + `.test.ts`
- `frontend/src/lib/core/api-paths.ts`

### Domain / scheduling logic (12 files)

All in `frontend/src/domain/scheduling/`:

- `types.ts` — `spaceId` on core scheduling types
- `schedule-model.ts` + `.test.ts`
- `schedule-index.ts` + `.test.ts`
- `schedule-selectors.ts` + `.test.ts`
- `schedule-validator.ts` + `.test.ts`
- `schedule-preview.ts` + `.test.ts`
- `capability-matcher.test.ts`
- `duration-calculator.ts` + `.test.ts`
- `recurrence.ts` + `.test.ts`
- `weekend-ranges.ts` + `.test.ts`
- `working-time.ts` + `.test.ts`

### Store (4 files)

- `frontend/src/store/app-store.ts` + `.test.ts`
- `frontend/src/store/scheduler-store.ts` + `.test.ts`

### Hooks (6 files)

- `frontend/src/hooks/useRequestForm.ts` + `.test.ts`
- `frontend/src/hooks/useSchedulingConflicts.ts`
- `frontend/src/hooks/useSpaces.ts` + `.test.tsx`
- `frontend/src/hooks/useUtilization.ts` + `.test.tsx`

### Types (1 file)

- `frontend/src/types/requests.ts`

### Components (38 files — grouped)

**Requests:**
`AddExistingRequestsDialog.test.tsx`, `CanvasInstructions.test.tsx`,
`CanvasInstructions.tsx`, `MoveToDialog.test.tsx`,
`RequestDetailPanel.test.tsx`, `RequestDetailsDialog.test.tsx`,
`RequestDetailsDialog.tsx`, `RequestFormDialog.tsx`,
`RequestListView.test.tsx`, `RequestRequirementsSection.test.tsx`,
`RequestScheduleSection.test.tsx`, `RequestScheduleSection.tsx`,
`RequestTreeView.test.tsx`, `SpaceDrawingCanvas.tsx`,
`SpaceShapeSvg.tsx`

**Settings:**
`GroupSpacesEditor.tsx`, `OffTimeDialog.test.tsx`, `OffTimeDialog.tsx`,
`SchedulingSettings.test.tsx`, `SchedulingSettings.tsx`

**Spaces:**
`EditSpaceDialog.test.tsx`, `EditSpaceDialog.tsx`,
`SpaceCapabilitiesEditor.test.tsx`, `SpaceCapabilitiesEditor.tsx`,
`SpaceList.test.tsx`, `SpaceList.tsx`, `SpaceManagementPanel.tsx`

**Utilization / Scheduler:**
`AutoSchedulePreviewDialog.test.tsx`, `CollapsibleFloorplan.test.tsx`,
`CollapsibleFloorplan.tsx`, `OffTimeOverlay.test.tsx`,
`OffTimeOverlay.tsx`, `RequestsPanel.test.tsx`, `RequestsPanel.tsx`,
`ScheduledRequestOverlay.test.tsx`, `ScheduledRequestOverlay.tsx`,
`SchedulerGrid.test.tsx`, `SpaceRow.test.tsx`, `SpaceRow.tsx`,
`TimeCell.test.tsx`, `TimeCell.tsx`

**Pages:**
`SpacesPage.tsx`, `UtilizationPage.test.tsx`, `UtilizationPage.tsx`

**Utils:**
`export-handlers.test.ts`, `export-handlers.ts`,
`gantt-pdf-export.test.ts`, `gantt-pdf-export.ts`, `utils.ts`,
`validation.test.ts`

---

## 4. `requests.space_id` write call sites

These are the four write paths that Phase 2 must replace with
`resource_assignments` writes.

| # | Location | Operation | Lines |
|---|----------|-----------|-------|
| 1 | `RequestRepository.CreateRequest` | INSERT `space_id` | ~143–162 |
| 2 | `RequestRepository.PatchRequest` | Patch field `space_id` | ~209 |
| 3 | `RequestRepository.ScheduleRequest` | UPDATE SET `space_id`, `start_ts`, `end_ts` | ~361–368 |
| 4 | `RequestRepository.UpdateSchedule` | UPDATE SET `space_id`, `start_ts`, `end_ts` | ~407–411 |

Both `ScheduleRequest` and `UpdateSchedule` update `space_id` atomically
with the time window. In Phase 2, these become inserts/updates of
`resource_assignments`.

---

## 5. `applies_to_all_spaces` / `AppliesToAllSpaces` refs

**Known inconsistency (to be fixed in Phase 2):**

| File | Behavior |
|------|----------|
| `SchedulingFeasibilityAnalyzer.cs:102` | **Honors it** — filters per space |
| `SchedulingEngine.cs` | **Ignores it** — treats all off-times as site-wide |
| `SchedulingService.cs` | Loads off-times but does not filter by space |

Phase 2 extracts a shared `OffTimeFilter.AppliesTo(resourceId, siteId)`
helper so both code paths behave identically.

All other references are mappings and validations that rename in lockstep.

---

## 6. Regression baseline test files

The behavioral assertions in these files must remain equivalent through
every phase. Only identifier/property names are renamed.

```
backend/tests/Services/AutoSchedule/AutoScheduleServiceTests.cs
backend/tests/Services/AutoSchedule/AutoScheduleTestHelpers.cs
backend/tests/Services/AutoSchedule/GreedySchedulingSolverTests.cs
backend/tests/Services/AutoSchedule/OrToolsSchedulingSolverTests.cs
backend/tests/Services/AutoSchedule/SchedulingFeasibilityAnalyzerTests.cs
backend/tests/Services/AutoSchedule/SchedulingScenarioTests.cs
backend/tests/Endpoints/RequestEndpointsTests.cs
backend/tests/Endpoints/SchedulingEndpointsTests.cs
backend/tests/Validators/RequestValidatorTests.cs
backend/tests/Validators/ScheduleRequestValidatorTests.cs
backend/tests/Validators/SchedulingValidatorTests.cs
```

Total backend test files containing space/scheduling/capability
assertions: **17 files**.

---

## 7. Additional rename targets not covered above

| Current name | Phase 2 target |
|---|---|
| `off_times.applies_to_all_spaces` | `applies_to_all_resources` |
| `off_time_spaces` table | `off_time_resources` |
| `space_capabilities` table | `resource_capabilities` |
| `space_groups` table | `resource_groups` |
| `group_capabilities` table | `resource_group_capabilities` |
| `SpaceCapabilityEndpoints.cs` | `ResourceCapabilityEndpoints.cs` |
| `/api/sites/{siteId}/spaces/{spaceId}/capabilities` | `/api/resources/{resourceId}/capabilities` |
| `/api/groups/{groupId}/capabilities` | `/api/resource-groups/{groupId}/capabilities` |
| `ISpaceCapabilityRepository` | `IResourceCapabilityRepository` |
| `SpaceCapabilityRepository.cs` | `ResourceCapabilityRepository.cs` |
| `SpaceCapabilityMapper.cs` | `ResourceCapabilityMapper.cs` |
| `OffTimeInfo.AppliesToAllSpaces` | `AppliesToAllResources` |
| `OffTimeInfo.SpaceIds` | `ResourceIds` |
| `CreateOffTimeRequest.SpaceIds` | `ResourceIds` |
| `UpdateOffTimeRequest.SpaceIds` | `ResourceIds` |
| `ProposedAssignmentDto.spaceId` | `resourceId` |
| `RequestInfo.SpaceId` | removed; replaced by `ResourceAssignments[]` |
