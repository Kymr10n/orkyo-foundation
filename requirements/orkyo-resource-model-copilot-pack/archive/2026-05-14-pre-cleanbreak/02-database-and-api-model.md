# Resource Model Database and API Guidance

## Placement

The generic resource model belongs in `orkyo-foundation`.

Reason:

- Shared by SaaS and Community editions.
- Required by the scheduler core.
- Not inherently multi-tenant-specific.
- Avoids duplication between SaaS and Community.

SaaS-specific orchestration, tenant boundaries, and product workflows remain in `orkyo-saas`.

## Database Tables

Create migrations for the following core tables.

### resource_types

```text
id
key
display_name
description
is_system
is_active
created_at_utc
updated_at_utc
```

Constraints:

```text
unique(key)
not null(key, display_name, is_system, is_active)
```

Seed:

```text
space
person
tool
```

### resources

```text
id
resource_type_id
name
description
external_reference
allocation_mode
base_availability_percent
is_active
metadata_json
created_at_utc
updated_at_utc
```

Constraints:

```text
fk(resource_type_id) -> resource_types(id)
base_availability_percent >= 0
base_availability_percent <= 100
allocation_mode in supported enum values
```

### capabilities

```text
id
key
display_name
description
is_active
created_at_utc
updated_at_utc
```

Constraints:

```text
unique(key)
```

### capability_resource_types

```text
capability_id
resource_type_id
```

Constraints:

```text
pk(capability_id, resource_type_id)
fk(capability_id) -> capabilities(id)
fk(resource_type_id) -> resource_types(id)
```

### resource_capabilities

```text
resource_id
capability_id
value
created_at_utc
updated_at_utc
```

Constraints:

```text
pk(resource_id, capability_id)
fk(resource_id) -> resources(id)
fk(capability_id) -> capabilities(id)
```

Validation:

- capability must be applicable to the resource's type.

### resource_groups

```text
id
name
description
resource_type_id nullable
is_active
created_at_utc
updated_at_utc
```

### resource_group_members

```text
resource_group_id
resource_id
```

Constraints:

```text
pk(resource_group_id, resource_id)
```

### resource_group_capabilities

```text
resource_group_id
capability_id
value
created_at_utc
updated_at_utc
```

### resource_absences

```text
id
resource_id
start_utc
end_utc
absence_type
reason
created_at_utc
updated_at_utc
```

Validation:

```text
end_utc > start_utc
```

### resource_assignments

```text
id
request_id
resource_id
start_utc
end_utc
allocation_percent nullable
allocation_units nullable
assignment_status
created_at_utc
updated_at_utc
```

Validation:

```text
end_utc > start_utc
allocation_percent > 0 and <= 100 when present
```

## Link to Existing Spaces

During migration, add one of these mapping approaches.

Preferred:

```text
spaces.resource_id nullable unique
```

Alternative:

```text
space_resources
- space_id
- resource_id
```

Preferred approach is simpler if each Space maps to exactly one Resource.

Rules:

- Existing Space rows remain.
- A Space must eventually have exactly one Resource.
- The Resource must have ResourceType `space`.
- Deleting a Space must not silently delete historical ResourceAssignments.

## API Endpoints

Use REST-style endpoints unless the existing API style dictates otherwise.

### Resource Types

```text
GET    /api/resource-types
GET    /api/resource-types/{id}
```

Do not allow create/update/delete for custom types in first iteration.

### Resources

```text
GET    /api/resources
GET    /api/resources/{id}
POST   /api/resources
PUT    /api/resources/{id}
DELETE /api/resources/{id}
```

Delete should be soft-delete or deactivate.

Recommended query filters:

```text
resourceTypeKey
isActive
capability
groupId
search
```

### Capabilities

```text
GET    /api/capabilities
POST   /api/capabilities
PUT    /api/capabilities/{id}
```

Capability delete should be deactivate.

### Capability Applicability

```text
GET /api/capabilities/{id}/resource-types
PUT /api/capabilities/{id}/resource-types
```

### Resource Capabilities

```text
GET /api/resources/{id}/capabilities
PUT /api/resources/{id}/capabilities
```

### Resource Groups

```text
GET    /api/resource-groups
POST   /api/resource-groups
PUT    /api/resource-groups/{id}
DELETE /api/resource-groups/{id}
```

### Group Members

```text
GET /api/resource-groups/{id}/members
PUT /api/resource-groups/{id}/members
```

### Absences

```text
GET    /api/resources/{id}/absences
POST   /api/resources/{id}/absences
PUT    /api/resources/{id}/absences/{absenceId}
DELETE /api/resources/{id}/absences/{absenceId}
```

### Assignments

```text
GET    /api/requests/{id}/resource-assignments
PUT    /api/requests/{id}/resource-assignments
```

## DTO Rules

Do not expose database entities directly.

Create DTOs/contracts in foundation where appropriate.

Recommended DTOs:

```text
ResourceTypeDto
ResourceDto
CreateResourceRequest
UpdateResourceRequest
CapabilityDto
ResourceCapabilityDto
ResourceGroupDto
ResourceAbsenceDto
ResourceAssignmentDto
```

## Validation Rules

Implement validation centrally.

Rules:

- ResourceType must exist.
- AllocationMode must be valid.
- BaseAvailabilityPercent must be between 0 and 100.
- Capability can only be assigned to applicable ResourceTypes.
- Exclusive resources cannot be overbooked.
- Fractional resource allocations cannot exceed effective availability.
- Absences must have end after start.
- Assignments must have end after start.
- Inactive resources cannot receive new assignments unless explicitly allowed.

## Constants

Avoid string literals.

Create constants for:

```text
ResourceTypeKeys.Space
ResourceTypeKeys.Person
ResourceTypeKeys.Tool

AllocationModes.Exclusive
AllocationModes.Fractional
AllocationModes.ConcurrentCapacity

AssignmentStatuses.Planned
AssignmentStatuses.Confirmed
AssignmentStatuses.Tentative
AssignmentStatuses.Cancelled
```

## Security and Authorization

Initial guidance:

- Admin users can manage resources, capabilities, groups, and absences.
- Normal users may view resources required by scheduling UI.
- Assignment changes should follow existing request/scheduling authorization rules.
- SaaS must enforce tenant boundaries.
- Foundation must not contain SaaS-specific tenant assumptions beyond abstractions already used in the project.
