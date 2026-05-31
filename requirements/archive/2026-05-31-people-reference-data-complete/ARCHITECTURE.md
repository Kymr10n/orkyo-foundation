# Orkyo People Reference Data Architecture

## 1. Purpose

This document defines the architecture for managing hierarchical departments and job titles in Orkyo. The objective is to replace free-text person fields with tenant-managed reference data while keeping organizational structure clearly separated from people resource planning constructs.

Departments are an organizational hierarchy only. They are not resource groups, teams, planning pools, availability containers, or capability inheritance structures.

Job titles are a flat tenant-scoped reference list used to classify people by their organizational or professional role.

## 2. Domain Boundary

### Departments

Departments represent the organizational structure of a tenant.

They are used for:

- Organizational classification
- Filtering and reporting
- Displaying where a person belongs in the organization
- Potential future routing or approval logic

They must not be used for:

- Resource grouping
- Scheduling logic
- Availability inheritance
- Capability inheritance
- Skills assignment
- Allocation behavior
- Team/resource pool semantics

### Job Titles

Job titles represent reusable role/title labels for people.

They are used for:

- Person classification
- Display in people grids and dialogs
- Filtering and reporting

They must not be used for:

- Authorization
- Resource planning rules
- Scheduling constraints
- Capability assignment

### People Groups / Teams

People groups remain separate from departments.

People groups are planning/resource constructs and may later support:

- Availability defaults
- Capabilities or skills
- Allocation rules
- Scheduling behavior
- Resource pooling

A person can therefore have both:

```text
person.department_id   -> organizational placement
person.job_title_id    -> job title reference
person.group_ids       -> planning/resource memberships
```

## 3. Data Model

### Department

Recommended table: `departments`

```sql
CREATE TABLE departments (
    id UUID PRIMARY KEY,
    tenant_id UUID NOT NULL,
    parent_department_id UUID NULL,
    name TEXT NOT NULL,
    code TEXT NULL,
    description TEXT NULL,
    sort_order INTEGER NOT NULL DEFAULT 0,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),

    CONSTRAINT fk_departments_parent
        FOREIGN KEY (parent_department_id)
        REFERENCES departments(id)
        ON DELETE RESTRICT,

    CONSTRAINT uq_departments_tenant_parent_name
        UNIQUE (tenant_id, parent_department_id, name)
);
```

Notes:

- `tenant_id` is mandatory for tenant isolation.
- `parent_department_id` enables the hierarchy.
- `parent_department_id = NULL` identifies a root department.
- `is_active` supports soft-deactivation.
- Physical deletes should be restricted once referenced by people.
- Names should be unique among sibling departments, not necessarily globally unique within the tenant.

### Job Title

Recommended table: `job_titles`

```sql
CREATE TABLE job_titles (
    id UUID PRIMARY KEY,
    tenant_id UUID NOT NULL,
    name TEXT NOT NULL,
    description TEXT NULL,
    sort_order INTEGER NOT NULL DEFAULT 0,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),

    CONSTRAINT uq_job_titles_tenant_name
        UNIQUE (tenant_id, name)
);
```

Notes:

- Job titles are flat.
- Names are unique per tenant.
- `is_active` supports deactivation while preserving references.

### Person Changes

The person/resource table must reference departments and job titles by ID.

Recommended fields:

```sql
ALTER TABLE people
    ADD COLUMN department_id UUID NULL,
    ADD COLUMN job_title_id UUID NULL;

ALTER TABLE people
    ADD CONSTRAINT fk_people_department
        FOREIGN KEY (department_id)
        REFERENCES departments(id)
        ON DELETE SET NULL;

ALTER TABLE people
    ADD CONSTRAINT fk_people_job_title
        FOREIGN KEY (job_title_id)
        REFERENCES job_titles(id)
        ON DELETE SET NULL;
```

If existing free-text fields exist, keep them temporarily only for migration compatibility. They should be removed or deprecated after data migration and UI/API adoption.

## 4. Validation Rules

### Departments

Backend validation must enforce:

- Name is required.
- Name must be unique among siblings within the same tenant.
- Parent department must belong to the same tenant.
- A department cannot be its own parent.
- Circular hierarchy is forbidden.
- A department with children should not be hard-deleted.
- A department referenced by people should not be hard-deleted.
- Deactivation is allowed even when referenced, but inactive departments should not appear as default selectable options for new assignments unless already assigned.

Circular hierarchy detection must be performed server-side in the write path. Do not rely on the frontend only.

### Job Titles

Backend validation must enforce:

- Name is required.
- Name must be unique per tenant.
- Referenced job titles should not be hard-deleted.
- Deactivation is allowed even when referenced, but inactive job titles should not appear as default selectable options for new assignments unless already assigned.

### Person Dialog

Person create/update validation must enforce:

- `department_id`, when provided, must exist in the same tenant.
- `job_title_id`, when provided, must exist in the same tenant.
- Inactive referenced entries may remain on existing records but should require explicit confirmation or not be offered for new selection.

## 5. API Design

Use tenant-scoped APIs consistent with existing Orkyo patterns.

### Departments

Recommended endpoints:

```http
GET    /api/settings/departments
GET    /api/settings/departments/tree
POST   /api/settings/departments
PUT    /api/settings/departments/{id}
DELETE /api/settings/departments/{id}
POST   /api/settings/departments/{id}/deactivate
POST   /api/settings/departments/{id}/activate
```

`GET /tree` should return nested departments for tree rendering.

Flat list response is useful for dropdowns and search.

Recommended DTO:

```json
{
  "id": "uuid",
  "parentDepartmentId": "uuid|null",
  "name": "Engineering",
  "code": "ENG",
  "description": "Engineering department",
  "sortOrder": 10,
  "isActive": true
}
```

Tree DTO:

```json
{
  "id": "uuid",
  "name": "Operations",
  "children": [
    {
      "id": "uuid",
      "name": "Maintenance",
      "children": []
    }
  ]
}
```

### Job Titles

Recommended endpoints:

```http
GET    /api/settings/job-titles
POST   /api/settings/job-titles
PUT    /api/settings/job-titles/{id}
DELETE /api/settings/job-titles/{id}
POST   /api/settings/job-titles/{id}/deactivate
POST   /api/settings/job-titles/{id}/activate
```

Recommended DTO:

```json
{
  "id": "uuid",
  "name": "Maintenance Engineer",
  "description": "Responsible for maintenance engineering tasks",
  "sortOrder": 10,
  "isActive": true
}
```

## 6. Frontend Architecture

### Settings Page

Add two new tabs to the Settings page:

- `Departments`
- `Job Titles`

Departments should be managed with a tree/table layout:

- Expand/collapse hierarchy
- Create root department
- Create child department
- Edit department
- Activate/deactivate department
- Delete only when safe
- Display name, code, description, active state, and action buttons

Job titles can use a simple list/card/table pattern consistent with existing Criteria management:

- Add job title
- Edit job title
- Activate/deactivate job title
- Delete only when safe
- Display name, description, active state, and action buttons

### Person Dialog

Replace free-text fields with searchable combobox/select components:

- Job Title combobox
- Department combobox

Required behavior:

- User can search existing entries.
- User can create a new job title inline if no match exists.
- User can create a new department inline if no match exists.
- Inline department creation must support selecting a parent department or creating a root department.
- Newly created entries are selected automatically in the person dialog.
- The person payload sends IDs, not copied names.

### Caching and State

Use existing frontend query/state conventions.

Recommended query keys:

```ts
['settings', 'departments']
['settings', 'departments', 'tree']
['settings', 'jobTitles']
['people']
```

After creating or updating reference data, invalidate affected queries:

- Department mutation: invalidate departments, department tree, people where displayed department names are affected.
- Job title mutation: invalidate job titles and people where displayed job title names are affected.

## 7. UX Principles

- Do not force users to leave the person dialog just to add a missing department or job title.
- Avoid treating departments as teams.
- Keep department hierarchy management in Settings, not on the People page.
- Keep inline creation simple and safe.
- Show inactive entries when already referenced by a person, but visually mark them as inactive.
- Prefer deactivation over deletion once data is referenced.

## 8. Security and Tenancy

All queries and mutations must be tenant-scoped server-side.

The backend must never trust tenant IDs supplied by the frontend for writes. Resolve tenant context using the established tenant resolution mechanism.

Authorization should follow existing settings/admin permissions. Only users with settings administration rights should manage departments and job titles.

Person editors may select existing entries and create inline entries only if permitted by the product permission model. If no finer-grained permission exists yet, reuse the settings/admin permission initially.

## 9. Non-Goals

This implementation must not introduce:

- Department-based availability inheritance
- Department-based capabilities
- Department-based scheduling rules
- Department-based resource pooling
- Authorization roles based on job titles
- A generic organization/team/group unification
- HR master-data synchronization

These may be considered later, but they are intentionally out of scope for this feature.
