# Orkyo People Resources — Functional & Technical Specification

## 1. Objective

Introduce **People Resources** as the first non-space resource type after the resource-management migration.

The implementation must prove that the new resource model is generic and extensible while delivering a usable first feature set for managing people, teams, skills, absences, availability, and request assignments.

People must be schedulable resources. They may optionally be linked to application users, but they must not require a user account.

## 2. Product Scope

### In scope

- New navigation section: `People`, positioned between `Spaces` and `Requests`.
- People resource management page.
- People groups / teams.
- Person-level and group-level availability.
- Person-level and group-level absences.
- Weekend and holiday awareness using the existing scheduling/calendar model.
- Skills modeled through the existing criteria/capability/requirement model.
- Request-to-person assignments.
- People utilization grid.
- Collapsible people/resource grid on the existing Utilization page between Floorplan and Space Utilization.
- Drag-and-drop assignment of people to requests where technically feasible within the existing grid architecture.
- Request CRUD dialog extension to assign people explicitly.
- User page integration showing people that are linked or linkable to users, including admin-triggered invitation flow where supported by the current identity/invitation model.

### Out of scope for this increment

- Tool resources.
- Custom user-defined resource types.
- Payroll, HR master-data integration, approval workflows, timesheets, or capacity planning beyond scheduling availability.
- Full RBAC redesign.
- Replacing the existing utilization grid architecture.
- Advanced optimization/auto-scheduling of people.

## 3. Core Domain Concepts

### 3.1 Resource model principle

People must be implemented as a resource-type extension, not as an isolated people subsystem.

Prefer generic entities and services:

```text
Resource
ResourceType = Person
ResourceGroup
ResourceCapability
ResourceAvailability
ResourceAbsence
ResourceAssignment
```

Only person-specific metadata should live in a person-specific profile structure:

```text
PersonProfile
- resourceId
- displayName
- email
- jobTitle
- department
- linkedUserId nullable
- notes nullable
```

Avoid special-case entities such as:

```text
PersonAssignment
PersonAvailability
PersonAbsence
```

unless they are thin read models or frontend DTOs derived from the generic resource model.

## 4. Navigation and Information Architecture

Update the left navigation order:

```text
Utilization
Spaces
People
Requests
Conflicts
Settings
```

The existing duplicate `Requests` navigation entry visible in the screenshot must be treated as a defect and removed if still present.

## 5. People Page

### 5.1 Purpose

The People page is the administrative workspace for person resources.

It must support:

- list people
- create person
- edit person
- delete/deactivate person if supported by existing resource lifecycle
- assign to group/team
- maintain skills
- maintain availability percentage
- maintain absences
- link/unlink user account where supported
- invite linked/unlinked person as user where supported

### 5.2 Layout

Recommended structure:

```text
People
├── Toolbar
│   ├── Add Person
│   ├── Add Group
│   ├── Search
│   └── Filters: Group, Skill, Availability, User Link Status
├── Tabs
│   ├── People
│   ├── Groups
│   └── Absences
└── Main Content
    ├── Grid/list view
    └── Details drawer/dialog
```

### 5.3 People grid columns

Minimum columns:

- Name
- Group / Team
- Email
- Linked user status
- Skills
- Availability %
- Current status
- Next absence
- Actions

Current status values:

```text
Available
Partially Available
Absent
Overbooked
Inactive
```

### 5.4 Groups grid columns

Minimum columns:

- Group name
- Members count
- Default availability %
- Default skills/capabilities
- Upcoming absences / non-working periods
- Actions

### 5.5 Absences view

Absences should be manageable both from person details and from a central absences tab.

Minimum fields:

- Resource or group
- Absence type
- Start date/time
- End date/time
- Availability impact
- Notes

Absence types:

```text
Vacation
Sick Leave
Unavailable
Training
Public Holiday
Weekend / Non-working Day
Other
```

Weekend and public holiday entries should normally be derived from the existing calendar/scheduling configuration, not manually duplicated per person.

## 6. User Account Relationship

A person resource may optionally link to an application user.

Rules:

- A user can be linked to at most one person resource within the same tenant/organization.
- A person resource can exist without a linked user.
- A linked user can be invited from the Users page or People page if the current invitation model supports this.
- Linking must not automatically grant additional authorization unless explicitly implemented through existing role/permission flows.

Recommended status values:

```text
Not Linked
Linked
Invitation Pending
User Active
User Disabled
```

## 7. Skills and Criteria

Skills must reuse the existing criteria/capability/requirement mechanism.

Extend criteria with applicability flags if not already available:

```text
appliesToSpaces: boolean
appliesToPeople: boolean
appliesToTools: boolean
```

For this increment:

- People skills are criteria where `appliesToPeople = true`.
- Space capabilities continue to use `appliesToSpaces = true`.
- Request requirements continue to use the existing requirement assignment model.
- Person assignment checks must validate whether the person satisfies request requirements applicable to people.

Do not duplicate skill tables unless the current schema absolutely requires a transitional read model.

## 8. Availability and Absence Rules

### 8.1 Availability percentage

A person or group may define availability as a percentage.

Examples:

```text
100% = full-time available
80%  = four days per week or equivalent capacity
50%  = half capacity
0%   = unavailable
```

Availability may be inherited from the group and overridden on the person.

Resolution order:

```text
Person explicit availability
Group default availability
System default availability = 100%
```

### 8.2 Absence inheritance

A person may inherit group absences.

Resolution order:

```text
System calendar non-working time
Group absence
Person absence
Assignment allocation
```

Person-specific absences override availability for the absence period.

### 8.3 Weekends and public holidays

People availability must respect the same working-time model used by scheduling settings:

- weekends
- public holidays
- configured non-working hours
- tenant/site calendar if currently scoped that way

Do not create a parallel calendar implementation.

## 9. Request Assignment Model

Requests can have requirements already. People can be assigned to requests through:

- request CRUD dialog
- utilization page drag-and-drop
- people/resource grid interaction

Minimum assignment fields:

- requestId
- resourceId
- allocation percentage or units
- start date/time
- end date/time
- assignment role optional
- notes optional

### 9.1 Fractional vs exclusive allocation

People should support fractional allocation.

A person can be assigned to multiple requests at the same time only if total allocation does not exceed available capacity.

Example:

```text
Person availability: 100%
Request A allocation: 50%
Request B allocation: 50%
Status: fully allocated, no conflict
Request C allocation: 25%
Status: overbooked, conflict
```

### 9.2 Validation rules

When assigning a person to a request, validate:

- person exists and is active
- request exists and is schedulable
- assignment period is valid
- person is available during the assignment period
- person is not absent during the assignment period
- allocation does not exceed available capacity
- person satisfies applicable request requirements/skills

Validation outcomes should distinguish hard blockers from warnings.

Hard blockers:

- inactive/deleted resource
- invalid assignment date range
- missing required skill if requirement is mandatory
- zero availability during full assignment period if assignment is required

Warnings:

- partial availability
- soft skill mismatch if requirement is optional
- overbooking if product decision allows saving with conflict

Use existing conflict model where possible.

## 10. Utilization Page Extension

### 10.1 Layout

Extend the current utilization page vertically:

```text
Utilization
├── Floorplan
├── People Resources Grid        collapsible, default expanded when people exist
└── Space Utilization Grid
```

The people grid must be collapsible independently of the floorplan.

### 10.2 People grid behavior

Rows:

- grouped by people group/team
- person rows below each group

Columns:

- same time scale as the current utilization grid
- aligned with current date navigation and zoom mode

Cells/blocks:

- absences
- assignments
- public holidays / weekends / non-working time
- availability status
- overbooking status

### 10.3 Color coding

Use design-system tokens where available. Do not hardcode arbitrary colors directly in components.

Required semantic statuses:

```text
Available
Partially Available
Absent
Assigned
Overbooked
Skill Mismatch
Conflict
Non-working Time
```

The legend should make the distinction explicit.

### 10.4 Drag-and-drop

Target UX:

- user drags a person/resource onto a request allocation or request row/card
- system proposes assignment period from the request allocation period
- system validates availability and skills
- system creates assignment or opens confirmation dialog if warnings exist

If full drag-and-drop is too risky in the first implementation, implement the stable fallback first:

- click request allocation
- open assignment panel/dialog
- select person
- validate
- save

Then add drag-and-drop as a second step.

## 11. Request CRUD Dialog Extension

Add a People/Resources section to the request create/edit dialog.

Minimum behavior:

- show assigned people
- add person assignment
- remove person assignment
- show availability and skill validation result
- show conflict warnings before save

Assignment picker should allow filtering by:

- group
- skill
- availability
- search text

## 12. API Requirements

Follow existing API conventions.

Recommended endpoints if not already covered by generic resource endpoints:

```text
GET    /api/resources?type=person
POST   /api/resources/person
GET    /api/resources/person/{id}
PUT    /api/resources/person/{id}
DELETE /api/resources/person/{id}

GET    /api/resource-groups?type=person
POST   /api/resource-groups/person
PUT    /api/resource-groups/{id}
DELETE /api/resource-groups/{id}

GET    /api/resources/{id}/availability
PUT    /api/resources/{id}/availability

GET    /api/resources/{id}/absences
POST   /api/resources/{id}/absences
PUT    /api/resource-absences/{id}
DELETE /api/resource-absences/{id}

GET    /api/requests/{id}/resource-assignments
POST   /api/requests/{id}/resource-assignments
PUT    /api/resource-assignments/{id}
DELETE /api/resource-assignments/{id}

POST   /api/resource-assignments/validate
GET    /api/utilization/resources?type=person&from=...&to=...&scale=...
```

If generic endpoints already exist, prefer extending them rather than creating person-specific duplicates.

## 13. Frontend Requirements

Recommended components:

```text
PeoplePage
PeopleGrid
PersonDialog
PeopleGroupDialog
PersonAbsenceDialog
PeopleAbsenceGrid
PersonSkillSelector
PersonAvailabilityEditor
PeopleUtilizationGrid
ResourceAssignmentDialog
RequestPeopleAssignmentsSection
```

Recommended hooks/services:

```text
usePeopleResources
usePeopleGroups
usePersonAbsences
usePersonAssignments
usePeopleUtilization
useValidateResourceAssignment
```

All API calls must use the established API client and React Query patterns.

No ad-hoc fetch calls inside components.

## 14. Backend Requirements

Backend implementation must:

- use existing tenant scoping patterns
- reuse existing resource model and resource repositories/services where available
- centralize validation logic in service/domain validators
- avoid controller-heavy business logic
- use transactions for assignment writes and validation-sensitive operations
- expose stable DTOs for frontend use
- preserve existing space behavior

## 15. Database Requirements

Prefer extending the migrated generic resource schema.

Expected logical structures:

```text
resources
resource_types
resource_groups
resource_group_memberships
resource_capabilities
resource_availability
resource_absences
resource_assignments
person_profiles
criteria applicability flags
```

If any of these already exist under different names, use the existing names and document the mapping.

Migration must be additive and backward compatible where feasible.

## 16. Security and Authorization

Follow existing authorization patterns.

Minimum permissions to map to existing roles:

```text
View people/resources
Manage people/resources
Manage absences
Assign resources to requests
Invite linked users
```

Do not expose people data across tenant boundaries.

Do not allow user linking across tenants.

## 17. Acceptance Criteria

- Navigation contains People between Spaces and Requests.
- Admin can create/edit/delete or deactivate a person resource.
- Admin can create/edit/delete a people group.
- Admin can assign people to groups.
- Admin can assign people skills using criteria flagged for people.
- Admin can create person absences.
- People availability respects person/group availability, weekends, holidays, and absences.
- Request CRUD dialog supports assigning people to requests.
- Assignment validation detects skill mismatch, absence, and overbooking.
- Utilization page shows a collapsible people grid between Floorplan and Space Utilization.
- People grid shows assignments, absences, availability, and overbooking states.
- Existing space utilization behavior remains unchanged.
- Existing tests pass.
- New backend and frontend tests cover the main happy paths and validation failures.

---

## 18. Codebase Reality (post resource-model migration)

This section overrides anything earlier in the document that conflicts with it.
It was added 2026-05-15 after auditing the foundation codebase against this spec.

### 18.1 What is already implemented and must be reused

| Concept | Already exists as | Notes |
|---|---|---|
| `Resource`, `ResourceType` | `resources`, `resource_types` (migration 1300) | `person`, `space`, `tool` seeded as `is_system=true` |
| `ResourceGroup`, group membership | `resource_groups`, `resource_group_members` (1310, 1330) | Group's `resource_type_id` constrains membership |
| `ResourceCapability` (skills) | `resource_capabilities` + `criterion_resource_types` (1300, 1310, 1330) | Per-tenant join table replaces the boolean flag idea in §7 |
| `ResourceAssignment` | `resource_assignments` with `Exclusive` + `Fractional` allocation (1300) | `ConcurrentCapacity` is reserved but not implemented |
| Per-resource absences | `off_times` + `off_time_resources` join (1270, 1310) | Already exposed via `GET/POST/PUT/DELETE /api/resources/{id}/absences` (Phase 4) |
| Site-level non-working time | `scheduling_settings.weekends_enabled`, `public_holidays_enabled`, `working_hours_*` (1270) | `SchedulingEngine.IsWeekend` already enforces |
| Site-level holidays | `off_times` rows with `type='holiday'` (1270) | Holiday provider is a flag (`HolidayProviderEnabled`); concrete dates are entered as off_times |
| Generic resource API | `MapResourceEndpoints()` — `/api/resources`, `/api/resources/{id}/{capabilities,absences,assignments}` | Filterable by `?resourceTypeKey=person` |
| Generic utilization API | `UtilizationService` + `GET /api/utilization?resourceTypeKey=person` (Phase 5) | Time-weighted, fractional-aware, group averaging |
| Criterion-applicability API | `GET/PUT /api/criteria/{id}/applicability` | Manages the `criterion_resource_types` set per criterion |
| Typed-operator capability matching | `CapabilityMatcher.ResourceSatisfiesRequirementAsync` | Wired into write paths but **not yet** into `ResourceAssignmentService.CreateAsync` (presence-only there today) |

**Implication:** the spec's Phase 1–5 in [02_people_resources_implementation_plan.md](02_people_resources_implementation_plan.md) are mostly no-ops. The new plan in 02 reflects this.

### 18.2 Decisions on open questions

These supersede ambiguous text in §6, §7, §8.

1. **Skills applicability** — Use the existing `criterion_resource_types` join table. Do **not** add `applies_to_spaces`/`applies_to_people`/`applies_to_tools` boolean columns to `criteria`. The criterion editor will surface a multi-select of resource types backed by the existing applicability endpoint.
2. **PersonProfile storage** — Add a dedicated `person_profiles` table with `resource_id` (PK + FK + UNIQUE), `email`, `job_title`, `department`, `linked_user_id` (nullable, UNIQUE per tenant), `notes`. Do not stuff into `resources.metadata_json` because `linked_user_id` needs FK and uniqueness.
3. **User-account linking** — v1 supports only **link existing user** + **unlink** + **create person without user**. **Invitation flow is deferred** to a follow-up phase. Reason: no existing invitation mechanism in foundation; SaaS and Community would need different impls; out of scope for the resource-model rollout.
4. **Group default availability** — Add `default_availability_percent INT NOT NULL DEFAULT 100` to `resource_groups`. Inheritance is computed at read time (capacity service), not stored.
5. **Group default capabilities** — **Deferred** to a follow-up phase. Members get capabilities individually in v1.
6. **Group absences** — **Deferred**. `off_times` only references resources today; v1 ships per-person absences only. Site-level off-times already cover whole-team blocking via `applies_to_all_resources=true`.
7. **Calendar / holidays** — Reuse `scheduling_settings` + `off_times` (`type='holiday'`). No parallel calendar. The `HolidayProviderEnabled` flag remains a no-op; admins create holiday entries explicitly. A regional-holiday provider is out of scope for People v1.
8. **Assignment role + notes** — Add nullable `role VARCHAR(100)` and `notes TEXT` to `resource_assignments`. Surfaced in the request-dialog people section.
9. **Validation result shape** — Introduce a dedicated validator returning `{ severity, blockers[], warnings[] }` plus a dry-run endpoint `POST /api/resource-assignments/validate`. The existing single-`ResourceConflict` shape is kept for `CreateAsync` backwards compatibility.
10. **Off-time enforcement at assignment time** — Wire `IOffTimeResourceQuery` into `ResourceAssignmentService.CreateAsync` (closes the Phase 1 known limitation). Required before any People assignment can ship.
11. **Typed-operator matching at assignment time** — Switch `ResourceAssignmentService.CreateAsync` to use `ResourceSatisfiesRequirementAsync` (typed) instead of `ResourceSatisfiesRequirementsAsync` (presence). Skill mismatch warnings depend on this.
12. **UI placement** — All People UI lives in `orkyo-foundation/frontend`. Routes are added to `TenantApp.tsx`; nav entry in `SidebarNav.tsx` between `/spaces` and `/requests`. SaaS and Community inherit automatically. The "duplicate Requests nav entry" mentioned in §4 is not present in foundation `SidebarNav` and needs no fix here.
13. **Drag-and-drop** — Out of scope for People v1. Click-to-assign only. DnD is a separate, later phase.
14. **`ConcurrentCapacity` allocation** — Out of scope. Spec says fractional only.
15. **Status values surfaced in UI** — `Available | Partially Available | Absent | Overbooked | Inactive` are computed read-side; not stored. Use availability + active assignments + off_times in the date window.

### 18.3 Spec-text overrides

- §3.1 — No special-case `ResourceAvailability` table. Availability is `resources.base_availability_percent` (already exists) inherited from `resource_groups.default_availability_percent` (new).
- §6 — Linked-user statuses reduce to `Not Linked | Linked` for v1. `Invitation Pending`, `User Active`, `User Disabled` are deferred.
- §7 — Skills applicability uses `criterion_resource_types`, not boolean columns.
- §8.2 — "Group absence" row in the resolution order is deferred.
- §10.4 — Drag-and-drop is deferred.
- §12 — Use the existing generic endpoints (`/api/resources?resourceTypeKey=person`). Do **not** add type-specific endpoints like `/api/resources/person`. Add only:
  - `POST /api/resource-assignments/validate` (new dry-run validator)
  - `GET/POST/PUT/DELETE /api/person-profiles/{resourceId}` for person metadata
  - (the rest are already there)

### 18.4 What stays exactly as written

Everything else in §1–§17 stays in force, in particular:
- §4 navigation order (with People between Spaces and Requests).
- §9 fractional/exclusive allocation rules and capacity math.
- §10 utilization page layout (Floorplan → People grid → Space grid).
- §11 request-dialog assignment section.
- §13–§16 frontend/backend/DB/security guardrails.
- §17 acceptance criteria, with the v1-deferred items removed.

### 18.5 v1 acceptance criteria (revised)

Replace §17 for the v1 scope:

- Navigation contains **People** between Spaces and Requests in foundation `SidebarNav`.
- Admin can create / edit / deactivate a person resource (name, email, job title, department, group, availability %, notes, optional linked user).
- Admin can create / edit / delete a people group with `default_availability_percent` and members.
- Admin can attach criteria as skills to a person (using the existing capability endpoint, restricted to criteria applicable to type `person`).
- Admin can manage per-person absences (existing `off_times` endpoint).
- People availability respects: weekends (`scheduling_settings.weekends_enabled`), site holidays (`off_times` with `type='holiday'`), per-resource absences, group default availability, person base availability, existing assignments.
- Request CRUD dialog has a **People / Resources** section that:
  - lists current assignments
  - lets admin add / remove an assignment with allocation %, role, notes
  - calls `POST /api/resource-assignments/validate` and shows blockers/warnings inline
- `POST /api/resource-assignments` enforces off-time + typed capability matching.
- Utilization page shows a collapsible **People** grid between the floorplan and the space grid, sharing the existing date/zoom state, with statuses: Available, Partially Available, Absent, Assigned, Overbooked, Skill Mismatch, Non-working Time.
- Existing space behavior, scheduling, and tests are unchanged.
- New tests cover person CRUD, profile CRUD, user link/unlink, group default availability inheritance, off-time blocking at assignment time, typed capability matching at assignment time, validation dry-run, people utilization rollup.
