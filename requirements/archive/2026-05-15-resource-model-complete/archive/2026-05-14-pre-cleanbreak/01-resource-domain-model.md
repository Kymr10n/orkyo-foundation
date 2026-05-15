# Orkyo Resource Domain Model

## Architectural Intent

Orkyo must evolve from a Space-first scheduling model into a Resource-first scheduling model.

Current simplified model:

```text
Request -> Space
```

Target model:

```text
Request -> ResourceAssignment -> Resource
```

A Space is no longer the hardcoded scheduling target. Instead, a Space is one Resource of type `Space`.

The scheduler should reason about:

- Resources
- Resource types
- Capabilities
- Availability
- Absences
- Allocation modes
- Assignments
- Utilization

It should not directly reason about Spaces, Persons, or Tools except through typed metadata and rules.

## Initial System Resource Types

Create three built-in resource types:

```text
Space
Person
Tool
```

These must be seeded as system-controlled records.

Do not yet expose custom resource type creation to users.

However, design the schema and services so custom resource types can be added later.

## ResourceType

Represents the category of a resource.

Required properties:

```text
ResourceType
- Id
- Key
- DisplayName
- Description
- IsSystem
- IsActive
- CreatedAt
- UpdatedAt
```

Initial values:

| Key | Display Name | System |
|---|---|---|
| space | Space | yes |
| person | Person | yes |
| tool | Tool | yes |

Rules:

- `Key` must be stable and unique.
- System resource types must not be deleted.
- Later custom resource types should use the same entity.
- UI labels may differ from internal keys.

## Resource

Represents one schedulable entity.

Examples:

```text
Meeting Room Zurich 1
Forklift A
John Doe
Assembly Table 4
```

Required properties:

```text
Resource
- Id
- ResourceTypeId
- Name
- Description
- ExternalReference
- AllocationMode
- BaseAvailabilityPercent
- IsActive
- CreatedAt
- UpdatedAt
```

Recommended optional properties:

```text
- LocationNodeId
- OwningOrganizationUnitId
- MetadataJson
```

Rules:

- Every Space should eventually have exactly one Resource of type `space`.
- Every Resource must have one ResourceType.
- `BaseAvailabilityPercent` defaults to 100.
- A Resource can be inactive without deleting historical assignments.
- Type-specific details should not pollute the base Resource table unless truly shared.

## AllocationMode

Do not implement this as a boolean.

Use an enum:

```text
AllocationMode
- Exclusive
- Fractional
- ConcurrentCapacity
```

### Exclusive

The resource can only be assigned once in a given time window.

Examples:

- Meeting room
- Specific machine
- Specific vehicle
- Specific physical tool

### Fractional

The resource can be partially allocated by percentage.

Examples:

- Person
- Specialist role
- Shared support function

### ConcurrentCapacity

The resource has capacity units and may support multiple concurrent assignments.

Examples:

- Parking area with 20 slots
- Generic workstation pool
- Shared equipment pool

This mode can be implemented later, but the enum should already allow it.

## ResourceAssignment

Connects a Request to a Resource.

Required properties:

```text
ResourceAssignment
- Id
- RequestId
- ResourceId
- StartUtc
- EndUtc
- AllocationPercent
- AllocationUnits
- AssignmentStatus
- CreatedAt
- UpdatedAt
```

Rules:

- For `Exclusive` resources, allocation is effectively 100%.
- For `Fractional` resources, allocation may be between >0 and 100.
- For `ConcurrentCapacity` resources, allocation may use units.
- Assignments must be validated against resource availability, absences, and overlapping assignments.
- Assignment history should not be destroyed when resources are renamed or deactivated.

## AssignmentStatus

Recommended enum:

```text
AssignmentStatus
- Planned
- Confirmed
- Tentative
- Cancelled
```

Initial implementation may only use `Planned`, but the model should allow expansion.

## ResourceCapability

Capabilities describe what a resource can provide.

Examples:

```text
Projector
Forklift license
Cleanroom capable
Has compressed air
Can operate CNC machine
```

Required properties:

```text
Capability
- Id
- Key
- DisplayName
- Description
- IsActive
```

Applicability by resource type:

```text
CapabilityResourceType
- CapabilityId
- ResourceTypeId
```

Assignment to resources:

```text
ResourceCapability
- ResourceId
- CapabilityId
- Value
- CreatedAt
- UpdatedAt
```

Rules:

- A capability can be applicable to one or many resource types.
- A capability must not be assigned to a resource if it is not applicable to that resource's type.
- Existing criteria should be migrated or mapped into capabilities.
- Avoid hardcoded capability names in code.
- Capability matching should be service-based, not spread through UI components.

## ResourceGroup

Groups allow shared configuration and reporting.

Examples:

```text
Maintenance Team
Zurich Meeting Rooms
Forklifts
Production Line A Resources
```

Required properties:

```text
ResourceGroup
- Id
- Name
- Description
- ResourceTypeId nullable
- IsActive
- CreatedAt
- UpdatedAt
```

Membership:

```text
ResourceGroupMember
- ResourceGroupId
- ResourceId
```

Rules:

- A group may be type-specific or mixed.
- Group membership must not delete resources.
- Group defaults may be inherited by resources.
- Individual resource settings must override group defaults where explicitly set.

## Group Capability Inheritance

Support group-level capability defaults.

```text
ResourceGroupCapability
- ResourceGroupId
- CapabilityId
- Value
```

Resolution order:

```text
Resource-specific capability
then Group capability
then no capability
```

If multiple groups define conflicting values, the system must either:

1. reject the conflict, or
2. require an explicit resource-level override.

Do not silently pick arbitrary values.

## Availability

Separate base availability from calendar availability.

### Base Availability

```text
Resource.BaseAvailabilityPercent
```

Examples:

- 100% full-time
- 80% part-time
- 50% shared resource

### Absences

```text
ResourceAbsence
- Id
- ResourceId
- StartUtc
- EndUtc
- AbsenceType
- Reason
- CreatedAt
- UpdatedAt
```

Examples:

- Vacation
- Sick leave
- Maintenance
- Out of service
- Training
- Reserved externally

### Availability Calculation

Effective availability should be calculated as:

```text
Base availability
minus absences
minus existing assignments
```

Do not store utilization as primary source of truth. Calculate it from assignments and availability windows.

## Space as Resource

After migration:

```text
Space -> Resource(type = space)
```

Important:

- Keep Space as a UX/domain concept.
- Do not remove the word Space from the UI.
- Do not flatten all user-facing concepts into generic “Resource”.
- The abstraction is primarily for scheduling, capabilities, availability, and utilization.

## Person as Resource

A Person resource is not necessarily a full user account.

A person resource may represent:

- internal employee
- external contractor
- generic role placeholder
- shift role

Do not require every Person resource to have a login identity.

Optional future link:

```text
Resource.PersonUserId nullable
```

## Tool as Resource

A Tool resource may represent:

- a specific physical tool
- a vehicle
- a machine
- a shared equipment item

Initial rule:

- Most Tools default to `Exclusive`
- Allow override to `Fractional` or later `ConcurrentCapacity`

## Non-Goals for First Iteration

Do not implement yet:

- custom user-defined resource types
- advanced optimization solver
- HR management
- payroll
- asset depreciation
- procurement
- complex multi-level inheritance
- generic ERP features

The objective is scheduling abstraction, not enterprise resource planning.
