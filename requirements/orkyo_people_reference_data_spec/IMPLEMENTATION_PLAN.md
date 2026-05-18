# Orkyo Departments and Job Titles Implementation Plan

## 1. Objective

Implement tenant-managed hierarchical departments and flat job title reference data for people resources.

The existing person dialog currently uses free-text style inputs for Job Title and Department. These must become controlled reference-data selections with inline creation support.

Departments must remain pure organizational hierarchy. They must not be connected to people groups, scheduling teams, capabilities, availability, or allocation logic.

## 2. Implementation Sequence

Implement this in controlled phases. Do not mix this work with unrelated people-resource scheduling changes.

Recommended sequence:

1. Backend database migration
2. Backend domain/entities/repositories/services
3. Backend API endpoints and validation
4. Frontend API client and hooks
5. Settings page tabs
6. Person dialog combobox integration and inline creation
7. People grid display updates
8. Tests and cleanup

## 3. Phase 1 — Database Migration

Create a new migration for:

- `departments`
- `job_titles`
- `people.department_id`
- `people.job_title_id`

Required constraints:

- Departments are tenant-scoped.
- Departments have optional `parent_department_id`.
- Department sibling names are unique per tenant and parent.
- Job title names are unique per tenant.
- People reference departments and job titles by ID.

If existing free-text columns exist for department/job title, preserve them temporarily and migrate distinct values into reference tables where safe.

Suggested migration behavior:

1. Create new tables.
2. Add nullable FK columns to people.
3. For existing non-empty free-text job titles, create tenant-scoped job title rows.
4. For existing non-empty free-text department values, create tenant-scoped root departments.
5. Backfill people FK columns.
6. Mark old free-text fields as deprecated in code.
7. Remove old columns only after the application no longer depends on them.

Do not infer department hierarchy from existing flat free text unless an explicit delimiter and migration rule already exists.

## 4. Phase 2 — Backend Domain Model

Add backend domain models/entities consistent with current project conventions.

Required concepts:

```text
Department
JobTitle
```

Department fields:

```text
id
tenantId
parentDepartmentId
name
code
description
sortOrder
isActive
createdAt
updatedAt
```

JobTitle fields:

```text
id
tenantId
name
description
sortOrder
isActive
createdAt
updatedAt
```

Person model changes:

```text
departmentId
jobTitleId
```

Expose resolved names in person read DTOs where useful:

```text
departmentName
jobTitleName
```

Do not duplicate names into the person entity as persisted denormalized values unless there is already an established projection pattern requiring it.

## 5. Phase 3 — Backend Services and Validation

Create services for:

```text
DepartmentService
JobTitleService
```

or equivalent naming consistent with the existing backend.

### Department Service Responsibilities

- List flat departments.
- Return department tree.
- Create department.
- Update department.
- Activate/deactivate department.
- Delete department only when safe.
- Validate same-tenant parent.
- Prevent self-parenting.
- Prevent circular hierarchy.
- Enforce sibling uniqueness.

Circular hierarchy validation must walk ancestors server-side before saving.

Pseudo-logic:

```text
function validateNoCircularReference(departmentId, proposedParentId):
    current = proposedParentId
    while current is not null:
        if current == departmentId:
            reject
        current = getParent(current)
```

### Job Title Service Responsibilities

- List job titles.
- Create job title.
- Update job title.
- Activate/deactivate job title.
- Delete job title only when safe.
- Enforce tenant-scoped name uniqueness.

### Person Service Changes

Update person create/update logic:

- Accept `departmentId` and `jobTitleId`.
- Validate both IDs are tenant-scoped.
- Reject references to non-existing rows.
- Keep existing behavior for unrelated fields.

## 6. Phase 4 — Backend API

Add settings APIs.

Recommended route shape:

```text
/api/settings/departments
/api/settings/departments/tree
/api/settings/job-titles
```

Required department operations:

- List flat
- List tree
- Create
- Update
- Activate
- Deactivate
- Delete where safe

Required job title operations:

- List
- Create
- Update
- Activate
- Deactivate
- Delete where safe

Update people APIs so person DTOs use IDs:

```json
{
  "name": "Jane Doe",
  "email": "jane@example.com",
  "departmentId": "uuid|null",
  "jobTitleId": "uuid|null"
}
```

Read DTOs may include resolved display fields:

```json
{
  "departmentId": "uuid",
  "departmentName": "Operations / Maintenance",
  "jobTitleId": "uuid",
  "jobTitleName": "Maintenance Engineer"
}
```

For hierarchical department display, use either:

```text
Operations / Maintenance / Electrical
```

or keep flat name plus optional path field:

```text
departmentName
departmentPath
```

Prefer `departmentPath` for clarity in grids and dropdowns.

## 7. Phase 5 — Frontend API Client and Hooks

Add typed frontend API functions for:

```text
getDepartments()
getDepartmentTree()
createDepartment()
updateDepartment()
activateDepartment()
deactivateDepartment()
deleteDepartment()

getJobTitles()
createJobTitle()
updateJobTitle()
activateJobTitle()
deactivateJobTitle()
deleteJobTitle()
```

Add React Query hooks consistent with existing conventions:

```text
useDepartments
useDepartmentTree
useCreateDepartment
useUpdateDepartment
useJobTitles
useCreateJobTitle
useUpdateJobTitle
```

Use query invalidation after mutations.

## 8. Phase 6 — Settings Page Tabs

Add tabs to Settings:

- `Departments`
- `Job Titles`

### Departments Tab

Implement a tree-aware management UI.

Required functions:

- Add root department
- Add child department
- Edit department
- Activate/deactivate department
- Delete only where backend allows it
- Expand/collapse nodes

Recommended columns/fields:

```text
Name
Code
Description
Status
Actions
```

For the first implementation, drag-and-drop reordering is optional. `sort_order` can be maintained by backend default or simple move controls later.

### Job Titles Tab

Implement simple list management.

Required functions:

- Add job title
- Edit job title
- Activate/deactivate job title
- Delete only where backend allows it

Recommended fields:

```text
Name
Description
Status
Actions
```

Use the existing visual pattern from Criteria where practical, but do not overload the Criteria model.

## 9. Phase 7 — Person Dialog Integration

Replace free-text Job Title and Department fields in the Add/Edit Person dialog.

### Job Title Field

Use a searchable combobox.

Behavior:

- Load active job titles.
- Allow selection by name.
- Show inactive selected value if already assigned.
- If typed value does not exist, show action: `Create "<value>"`.
- After creation, select the newly created job title automatically.

### Department Field

Use a searchable combobox with hierarchical display.

Behavior:

- Load active departments.
- Show department path in dropdown, e.g. `Operations / Maintenance`.
- Allow inline creation.
- Inline creation must allow selecting a parent department.
- Newly created department is selected automatically.

For simple inline creation, use a nested mini-dialog rather than overloading the dropdown UI.

Suggested flow:

1. User types a department that does not exist.
2. Dropdown shows `Create department "X"`.
3. Mini-dialog opens.
4. User confirms name and optionally selects parent.
5. Department is created.
6. Department list refreshes.
7. New department is selected in the person dialog.

### Person Save Payload

Ensure person save uses:

```text
jobTitleId
departmentId
```

not free-text names.

## 10. Phase 8 — People Grid and Detail Display

Update people list/grid/detail views to display resolved names:

```text
Job Title: jobTitleName
Department: departmentPath or departmentName
```

Filtering should use IDs where possible, not names.

Add filters later if not already present:

- Department
- Job Title

Filtering by department should eventually support subtree semantics, but that can be a follow-up feature. Initial implementation may filter exact department ID only.

## 11. Phase 9 — Tests

Add tests at the appropriate backend and frontend layers.

### Backend Tests

Required coverage:

- Create root department.
- Create child department.
- Reject duplicate sibling department name.
- Allow same department name under different parent.
- Reject cross-tenant parent.
- Reject circular parent update.
- Deactivate referenced department.
- Prevent unsafe hard-delete.
- Create job title.
- Reject duplicate job title name within same tenant.
- Allow same job title name in different tenant.
- Assign department and job title to person.
- Reject cross-tenant department/job title assignment.

### Frontend Tests

Required coverage where test infrastructure allows:

- Settings tabs render.
- Department tree renders hierarchy.
- Job title list renders.
- Person dialog loads department and job title options.
- Inline job title creation selects the new entry.
- Inline department creation selects the new entry.
- Person save sends IDs.

## 12. Phase 10 — Cleanup

After the new model is fully wired:

- Remove legacy free-text job title and department UI fields.
- Remove deprecated DTO fields if no longer needed.
- Remove old database columns only in a follow-up migration after validating data migration.
- Update seed data if applicable.
- Update API docs or developer documentation.

## 13. Acceptance Criteria

The implementation is complete when:

- Departments can be managed as a tenant-scoped hierarchy in Settings.
- Job titles can be managed as a tenant-scoped flat list in Settings.
- Person dialog uses selectable reference data for department and job title.
- Users can create missing departments and job titles from the person dialog.
- Newly created inline entries are immediately selected.
- Person records persist `department_id` and `job_title_id`.
- People grids display resolved department path/name and job title name.
- Departments are not connected to people groups or teams.
- Departments do not influence availability, allocation, scheduling, capabilities, or skills.
- Backend validation prevents circular department references.
- Backend validation prevents cross-tenant references.
- Tests cover the critical backend validation and frontend dialog behavior.

## 14. Explicit Guardrails for Copilot

Do not:

- Reuse the existing Groups model for departments.
- Add availability to departments.
- Add capabilities to departments.
- Add scheduling logic to departments.
- Add team/resource semantics to departments.
- Store department/job title names as authoritative person fields.
- Trust frontend-provided tenant IDs.
- Implement hierarchy validation only in the frontend.
- Introduce a broad generic organization model unless explicitly requested.

Do:

- Keep this feature narrow and reference-data focused.
- Follow existing Orkyo backend/frontend conventions.
- Keep tenant isolation server-side.
- Prefer soft-deactivation over destructive delete.
- Keep APIs typed and DTOs explicit.
- Keep the person dialog user-friendly with inline creation.
