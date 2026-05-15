# Migration Path: Space-first to Resource-first Scheduling

## Migration Objective

Move Orkyo from:

```text
Request -> Space
```

to:

```text
Request -> ResourceAssignment -> Resource(type = Space)
```

without breaking the current Space planning user experience.

The UI may continue to show "Spaces" while the backend scheduling model becomes resource-based.

## Recommended Migration Sequence

## Phase 0 — Preparation

Before changing code:

1. Review existing Space, Request, Criteria, Scheduling, and Allocation models.
2. Identify all direct dependencies on `spaceId`.
3. Identify all places where criteria are assumed to be Space-only.
4. Identify all scheduler logic that checks Space occupancy.
5. Add regression tests around current Space assignment behavior.

Deliverable:

```text
docs/resource-model/current-space-dependencies.md
```

This document should list affected files and behaviors.

## Phase 1 — Add Resource Model in Parallel

Implement new tables/entities/services:

- ResourceType
- Resource
- Capability
- CapabilityResourceType
- ResourceCapability
- ResourceGroup
- ResourceGroupMember
- ResourceAbsence
- ResourceAssignment

Seed initial ResourceTypes:

```text
space
person
tool
```

Do not yet remove or change Space assignment behavior.

Acceptance:

- Application starts.
- Migrations apply cleanly.
- Existing Space planning still works.
- API can list seeded ResourceTypes.
- Resources can be created independently.

## Phase 2 — Map Existing Spaces to Resources

For every existing Space:

1. Create a Resource with ResourceType `space`.
2. Copy name/description where appropriate.
3. Set `allocation_mode = Exclusive` by default.
4. Set `base_availability_percent = 100`.
5. Store mapping from Space to Resource.

Recommended mapping:

```text
spaces.resource_id
```

Rules:

- Migration must be idempotent.
- Running the migration twice must not create duplicate resources.
- Each Space must have exactly one Resource after migration.
- Each mapped Resource must have type `space`.

Acceptance:

- All existing Spaces have Resource mappings.
- New Space creation creates corresponding Resource.
- Updating Space name updates Resource name, or a clear synchronization rule exists.
- Existing scheduler still uses old Space logic at this phase.

## Phase 3 — Criteria to Capability Mapping

Existing criteria should become Resource Capabilities.

Approach:

1. Introduce Capability table.
2. Map existing criteria to Capability rows.
3. Mark existing Space-related criteria as applicable to ResourceType `space`.
4. Do not yet assume criteria apply to Person or Tool.
5. Add capability applicability rules.

Acceptance:

- Existing criteria behavior still works for Spaces.
- Capability applicability can be queried.
- Capability cannot be assigned to invalid resource types.
- No hardcoded criterion strings are introduced.

## Phase 4 — Introduce ResourceAssignment Alongside Space Assignment

When a Request is assigned to a Space, also write a ResourceAssignment.

Example:

```text
Request.SpaceId = existing value
ResourceAssignment = mapped Space resource
```

Rules:

- ResourceAssignment mirrors current Space assignment.
- Existing UI still reads Space assignment.
- Scheduler can begin reading ResourceAssignment in read-only mode for verification.
- Add consistency checks between `Request.SpaceId` and ResourceAssignment.

Acceptance:

- Existing Space scheduling remains functional.
- ResourceAssignment is created for each Space assignment.
- Consistency checks pass for current data.
- No duplicate active assignments for the same Request/Space pair.

## Phase 5 — Switch Scheduler Read Model

Change scheduler internals to read Space assignment through ResourceAssignment.

UI still displays Spaces.

Internal target:

```text
Request -> ResourceAssignment -> Resource(type = space) -> Space
```

Avoid directly depending on `Request.SpaceId` in scheduler logic.

Acceptance:

- Scheduler works using ResourceAssignment.
- Space occupancy checks are based on Resource assignments.
- Conflicts are detected through Resource allocation rules.
- Existing visual UI behavior remains unchanged.

## Phase 6 — Deprecate Direct Space Assignment

Once ResourceAssignment is stable:

1. Stop writing new canonical values to `Request.SpaceId`.
2. Keep it as compatibility/read model if required.
3. Add migration note marking it deprecated.
4. Eventually remove it only after all code paths are migrated.

Do not remove early.

Acceptance:

- New assignments are canonical in ResourceAssignment.
- No scheduler service depends on `Request.SpaceId`.
- Old field is either synchronized or explicitly read-only.

## Phase 7 — Activate Person and Tool Resources

Only after Space migration is stable:

1. Enable creation/editing of Person resources.
2. Enable creation/editing of Tool resources.
3. Add capability applicability for Person and Tool.
4. Add assignment UI only where needed.
5. Extend scheduling rules to multiple required resource types.

Acceptance:

- Requests may require more than one resource.
- Space assignment remains visually familiar.
- Person and Tool assignment works without special-case scheduling code.
- Fractional allocation works for Person resources.
- Exclusive allocation works for Tool resources.

## Phase 8 — Utilization Reporting

Build utilization from:

```text
effective availability
resource assignments
absences
allocation mode
```

Do not store utilization as a manually maintained field.

Acceptance:

- Show utilization per Resource.
- Show utilization per ResourceGroup.
- Show utilization by ResourceType.
- Exclusive resources show occupancy.
- Fractional resources show percentage load.

## Rollback Strategy

Each phase must be independently deployable.

Rollback principles:

- Phase 1 only adds tables.
- Phase 2 only adds mappings.
- Phase 4 dual-writes but keeps old behavior.
- Phase 5 read switch should be feature-flagged if feasible.
- Never delete Space assignment fields until stable for several releases.

## Feature Flags

Recommended flags:

```text
ResourceModelEnabled
SpaceResourceMappingEnabled
ResourceAssignmentDualWriteEnabled
SchedulerReadsResourceAssignments
PersonToolResourcesEnabled
```

## Important Constraints

- Do not break existing local dev.
- Do not break current tests.
- Do not delete Space UI.
- Do not expose custom ResourceTypes yet.
- Do not introduce solver complexity before the ResourceAssignment model is stable.
