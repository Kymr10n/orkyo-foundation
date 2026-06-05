# Scheduler Integration for Resource-first Planning

## Intent

The scheduler must evolve from Space-specific conflict detection to generic Resource-based allocation validation.

Current mental model:

```text
Can this Request use this Space during this time?
```

Target mental model:

```text
Can this Request consume these Resources during this time, given capabilities, availability, absences, and allocation modes?
```

## Scheduler Core Rule

The scheduler must not hardcode resource-specific concepts such as Space, Person, or Tool.

It should operate on:

```text
Resource
ResourceType
Capability
AvailabilityWindow
Absence
ResourceAssignment
AllocationMode
```

## Assignment Validation

When assigning a resource to a request, validate:

1. Resource exists.
2. Resource is active.
3. Resource has applicable type.
4. Resource satisfies required capabilities.
5. Resource is available during requested time.
6. Resource is not absent during requested time.
7. Assignment does not exceed allocation capacity.
8. Assignment respects allocation mode.

## Exclusive Resource Validation

For `Exclusive` resources:

```text
No overlapping active assignment may exist for the same resource.
```

Relevant statuses:

```text
Planned
Confirmed
Tentative
```

Ignore:

```text
Cancelled
```

Conflict example:

```text
Room A assigned 10:00-12:00
New request wants Room A 11:00-13:00
Result: conflict
```

## Fractional Resource Validation

For `Fractional` resources:

```text
Sum of overlapping allocation percentages must not exceed effective availability.
```

Example:

```text
Person A base availability: 80%
Existing assignment: 30%
New assignment: 60%
Total: 90%
Result: conflict because 90% > 80%
```

If absences reduce availability, the available capacity must be reduced accordingly.

## Concurrent Capacity

Do not fully implement in first iteration unless already easy.

Reserve the model for later.

Future rule:

```text
Sum of allocation units must not exceed available units.
```

## Absence Handling

An absence reduces availability.

For `Exclusive` resources:

```text
Any overlapping absence blocks assignment.
```

For `Fractional` resources:

```text
Absence reduces available capacity for the affected period.
```

Initial implementation may treat any overlapping absence as full unavailability if partial-day logic is too complex.

Document the limitation clearly.

## Capability Matching

A request may require capabilities.

Example:

```text
Request requires:
- Space with projector
- Person with forklift license
- Tool with lifting capacity
```

Capability matching must be handled by a central service.

Recommended service:

```text
IResourceCapabilityMatcher
```

Responsibilities:

- Check resource-specific capabilities.
- Check inherited group capabilities.
- Validate applicability by ResourceType.
- Return clear match/mismatch reasons.

## Conflict Explanation

Do not only return `false`.

Return structured reasons:

```text
ResourceConflict
- ResourceId
- ConflictType
- Message
- ConflictingAssignmentId nullable
- ConflictingAbsenceId nullable
```

Conflict types:

```text
ResourceInactive
CapabilityMissing
CapabilityNotApplicable
ResourceAbsent
ExclusiveOverlap
FractionalCapacityExceeded
InvalidAllocationMode
InvalidAllocationPercent
```

This is important for user-facing scheduling diagnostics.

## Scheduler Read Model

Eventually, scheduler should load:

```text
Requests
ResourceAssignments
Resources
ResourceTypes
Capabilities
Absences
Groups
```

It should not require direct Space assignment fields.

## Space Compatibility

During migration, Space UI may still use Space IDs.

Introduce adapter logic:

```text
SpaceId -> ResourceId
ResourceId -> SpaceId
```

Do not spread this mapping across components.

Recommended service:

```text
ISpaceResourceMapper
```

## Auto-Scheduling Impact

Auto-scheduling becomes multi-resource matching.

First iteration:

- Continue auto-scheduling Spaces as ResourceType `space`.
- Do not require Person/Tool optimization initially.
- Ensure the solver abstraction can later accept multiple resource requirements.

Future target:

```text
Request requires:
- one matching Space
- zero or more matching Persons
- zero or more matching Tools
```

## Performance Considerations

Resource scheduling can become expensive.

Add indexes for:

```text
resource_assignments(resource_id, start_utc, end_utc)
resource_absences(resource_id, start_utc, end_utc)
resources(resource_type_id)
resource_capabilities(resource_id, capability_id)
resource_group_members(resource_id)
```

Avoid N+1 queries in scheduler loops.

Prefer bulk-loading the required scheduling window.

## Testing Requirements

Add tests for:

- Exclusive resource overlap
- Fractional allocation within limit
- Fractional allocation over limit
- Absence blocking assignment
- Capability missing
- Capability inherited from group
- Invalid capability/resource type assignment
- Space assignment compatibility
- Request with multiple resources
- Cancelled assignment ignored in conflict detection
