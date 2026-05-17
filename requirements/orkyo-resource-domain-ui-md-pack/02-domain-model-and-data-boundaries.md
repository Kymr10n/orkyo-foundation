# 02 — Domain Model and Data Boundaries

## Architectural Goal

Establish clear ownership between global configuration and resource-domain administration.

This refactoring is primarily UI and routing work, but it must respect the emerging resource model:

- A resource type owns its operational metadata.
- Global settings own only reusable tenant-wide definitions and platform behavior.

## Resource Type Ownership

### People Domain Owns

- People resources
- People groups
- Absences
- Departments
- Job titles
- People-specific criteria usage
- People-specific availability metadata

### Spaces Domain Owns

- Space resources
- Space groups
- Floorplans
- Space-specific criteria usage/capabilities
- Space-specific availability or maintenance metadata, where applicable

### Settings Owns

- Global criteria registry
- Templates
- Presets
- Tenant organization settings
- Tenant scheduling defaults
- Technical/system configuration
- User administration, unless a separate Administration section is introduced later

## Criteria / Capabilities Model

Do not duplicate Criteria entities per resource type.

Criteria should remain a global reusable definition with applicability metadata.

Expected conceptual model:

```text
Criterion
- id
- tenantId
- name
- description
- category
- applicableResourceTypes[]
- active
- createdAt
- updatedAt
```

Resource-type-specific pages should filter criteria by applicability:

```text
Spaces -> Criterion where applicableResourceTypes contains SPACE
People -> Criterion where applicableResourceTypes contains PEOPLE
Tools  -> Criterion where applicableResourceTypes contains TOOL, future
```

## Groups Model

Groups should be associated with a resource type.

Expected conceptual model:

```text
ResourceGroup
- id
- tenantId
- resourceType
- name
- description
- parentGroupId, optional if hierarchy is supported
- active
- createdAt
- updatedAt
```

The People page must only show groups where `resourceType = PEOPLE`.

The Spaces page must only show groups where `resourceType = SPACE`.

If the existing schema does not yet contain `resourceType` on groups, add it through a migration and backfill existing records safely.

## Departments and Job Titles

Departments and job titles are people-domain master data.

They must not be treated as tenant-wide platform settings in the UI.

Departments may be hierarchical.

Expected conceptual model:

```text
Department
- id
- tenantId
- name
- description
- parentDepartmentId, optional
- active
- sortOrder, optional
- createdAt
- updatedAt
```

Expected conceptual model:

```text
JobTitle
- id
- tenantId
- name
- description
- active
- sortOrder, optional
- createdAt
- updatedAt
```

## No Data Duplication

This refactoring must not create duplicate data stores for the same concepts.

Allowed:

- Move views and routes.
- Add resource-type filters.
- Add compatibility redirects.
- Add resource type metadata if missing.

Not allowed:

- Fork Criteria into separate PeopleCriteria and SpaceCriteria tables.
- Fork Groups into separate PeopleGroups and SpaceGroups tables unless the current architecture already requires it.
- Keep two independent UIs for the same master data after migration.

## Bounded Context Rule

When adding future resource types, follow the same structure:

```text
Resource type page
- Resources
- Groups
- Capabilities / Criteria
- Availability
- Type-specific master data
```

Settings must not become the default location for resource-type-specific lists.
