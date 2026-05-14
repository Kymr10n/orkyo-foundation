# Acceptance Criteria for Resource Model Introduction

## General Acceptance

The implementation is acceptable when:

- Existing Space planning still works.
- Resource model exists and is seeded.
- Spaces can be represented as Resources.
- ResourceAssignments can represent current Space assignments.
- Capabilities can be scoped by ResourceType.
- Allocation modes are enforced.
- Absences affect availability.
- Scheduler can be migrated to ResourceAssignments without UI regression.

## Phase 1 Acceptance — Resource Model

- `space`, `person`, and `tool` ResourceTypes are seeded.
- Resource CRUD works.
- AllocationMode exists with at least:
  - Exclusive
  - Fractional
  - ConcurrentCapacity
- BaseAvailabilityPercent is validated.
- Inactive Resources cannot be newly assigned by default.
- Existing application startup is unaffected.

## Phase 2 Acceptance — Space Mapping

- Every existing Space has a Resource of type `space`.
- New Space creation creates a Resource.
- Space-to-Resource mapping is unique.
- Mapping migration is idempotent.
- Space UI remains unchanged.
- Existing Space scheduler behavior remains unchanged.

## Phase 3 Acceptance — Capabilities

- Existing criteria are represented or mappable as Capabilities.
- Capabilities can be marked applicable to ResourceTypes.
- A Space-only capability cannot be assigned to Person or Tool.
- Capability matching is implemented in a central service.
- No hardcoded capability strings are used in scheduling logic.

## Phase 4 Acceptance — ResourceAssignment Dual-Write

- Assigning a Space to a Request creates or updates a ResourceAssignment.
- Removing a Space assignment cancels or removes the matching ResourceAssignment according to agreed lifecycle.
- Consistency checks identify mismatches.
- Existing Request/Space UI remains operational.
- No duplicate active ResourceAssignment exists for the same Request and mapped Space Resource.

## Phase 5 Acceptance — Scheduler Resource Read

- Scheduler conflict detection reads ResourceAssignments.
- Exclusive Resource conflicts are detected.
- Fractional capacity conflicts are detected.
- Absences block or reduce availability.
- Existing Space scheduling UX remains unchanged.
- Legacy Space assignment field is no longer the canonical scheduler source.

## Phase 6 Acceptance — Person and Tool Activation

- Person resources can be created and edited.
- Tool resources can be created and edited.
- Person resources can use Fractional allocation.
- Tool resources can use Exclusive allocation.
- Requests can hold assignments to multiple ResourceTypes.
- Person and Tool assignment does not require duplicated scheduler logic.

## Phase 7 Acceptance — Utilization

- Utilization can be calculated per Resource.
- Utilization can be calculated per ResourceGroup.
- Utilization can be filtered by ResourceType.
- Utilization uses assignments and effective availability.
- Utilization is not manually stored as source of truth.

## Regression Acceptance

The following must continue to work:

- Existing local dev startup
- Existing migrations
- Existing Space CRUD
- Existing request creation
- Existing scheduler display
- Existing conflict detection
- Existing tests, unless intentionally updated with clear migration notes

## Done Definition

The work is done only when:

- All tests pass.
- Migration applies cleanly to existing data.
- No existing Space planning workflow is broken.
- New Resource model is documented.
- Follow-up limitations are documented.
- Deprecated fields are marked but not prematurely removed.
