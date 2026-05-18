# 04 — Backend API and Permission Plan

## Goal

Support the UI refactoring with clean backend boundaries, resource-type filtering, and safe permission behavior.

This change should prefer reusing existing APIs where possible. Add new endpoints only when current APIs cannot express resource-type filtering safely.

## API Principles

- APIs must enforce tenant isolation.
- APIs must enforce resource-type filters server-side, not only in the frontend.
- Create operations from domain pages must default or require the correct resource type.
- Cross-resource data leakage must be prevented.

## Groups API

If a generic group API exists, extend it to support resource type filters.

Recommended endpoints:

```http
GET    /api/resource-groups?resourceType=space
POST   /api/resource-groups
GET    /api/resource-groups/{id}
PUT    /api/resource-groups/{id}
DELETE /api/resource-groups/{id}
```

Create request example:

```json
{
  "name": "Assembly Areas",
  "description": "Spaces used for assembly work",
  "resourceType": "space"
}
```

Server-side validation:

- `resourceType` is required.
- `resourceType` must be a known value.
- User must have permission to manage that resource type.
- Names should be unique within tenant + resource type unless existing behavior allows duplicates.

## Criteria / Capabilities API

If Criteria already exist globally, preserve that model.

Recommended endpoints:

```http
GET    /api/criteria
GET    /api/criteria?resourceType=space
GET    /api/criteria?resourceType=people
POST   /api/criteria
PUT    /api/criteria/{id}
DELETE /api/criteria/{id}
```

Filtering semantics:

```text
resourceType=space  -> return criteria where applicableResourceTypes includes SPACE
resourceType=people -> return criteria where applicableResourceTypes includes PEOPLE
```

Create request example from Spaces > Capabilities:

```json
{
  "name": "Has crane access",
  "description": "Space can support crane-based work",
  "applicableResourceTypes": ["space"]
}
```

Create request example from People > Skills:

```json
{
  "name": "Electrical certification",
  "description": "Person has required electrical certification",
  "applicableResourceTypes": ["people"]
}
```

Server-side validation:

- At least one applicable resource type is required.
- Resource types must be known values.
- Names should be unique per tenant, or per tenant + category, depending on current behavior.
- Criteria used by active requests/resources should not be hard-deleted unless the current system already supports safe deletion.

## Departments API

Departments remain people-domain data.

Recommended endpoints, if not already present:

```http
GET    /api/people/departments
POST   /api/people/departments
GET    /api/people/departments/{id}
PUT    /api/people/departments/{id}
DELETE /api/people/departments/{id}
```

If current endpoints are global settings endpoints, keep them temporarily but mark them deprecated internally.

Preferred migration:

```text
/settings/departments UI -> /people/departments UI
/api/settings/departments -> keep temporarily as alias
/api/people/departments -> canonical endpoint
```

## Job Titles API

Recommended endpoints, if not already present:

```http
GET    /api/people/job-titles
POST   /api/people/job-titles
GET    /api/people/job-titles/{id}
PUT    /api/people/job-titles/{id}
DELETE /api/people/job-titles/{id}
```

Same compatibility approach as departments.

## Permission Model

Do not create excessive permission granularity unless the existing authorization model already supports it.

Minimum permissions:

```text
ManageSettings
ManagePeople
ManageSpaces
```

Recommended behavior:

| Action | Permission |
|---|---|
| Manage global criteria | ManageSettings |
| Manage people departments | ManagePeople |
| Manage people job titles | ManagePeople |
| Manage people groups | ManagePeople |
| Manage people skills | ManagePeople or ManageSettings, depending on criterion lifecycle |
| Manage space groups | ManageSpaces |
| Manage space capabilities | ManageSpaces or ManageSettings, depending on criterion lifecycle |
```

Important decision:

If creating a capability/skill creates a global Criterion, decide whether domain managers are allowed to create global definitions. If not, domain pages should support assigning existing criteria only, while Settings remains the only place to create global criteria.

Recommended practical approach for now:

- Allow creating criteria from People/Spaces pages.
- Auto-scope applicability to the active resource type.
- Require the same permission as managing that resource domain.
- Keep Settings as the place where admins can see and edit all criteria across resource types.

## Auditability

If audit logging exists, log:

- creation/update/deletion of groups,
- creation/update/deletion of criteria,
- department changes,
- job title changes,
- resource type applicability changes.

Audit entries should include tenant, actor, object type, object id, and changed fields if available.
