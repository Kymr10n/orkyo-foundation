# 03 — Frontend Implementation Plan

## Goal

Refactor the frontend so People and Spaces pages become resource-domain administration areas with their own tabs and filtered data views.

## Current Problem

Settings contains too many tabs and mixes unrelated concerns. This makes the page hard to scan and will become worse as more resource types are added.

## Target Routes

Use existing route conventions if they differ, but implement the following conceptual route structure.

### People

```text
/people
/people/list
/people/groups
/people/absences
/people/departments
/people/job-titles
/people/skills
```

The `/people` default route should redirect to `/people/list` or render the People tab directly.

### Spaces

```text
/spaces
/spaces/list
/spaces/groups
/spaces/capabilities
/spaces/floorplan
```

The `/spaces` default route should preserve the current primary Spaces experience if users expect the floorplan view there.

If the current Spaces page is strongly floorplan-centric, keep `/spaces` rendering the current layout and add tabs inside the page rather than forcing route changes immediately.

### Settings

```text
/settings/criteria
/settings/templates
/settings/presets
/settings/users
/settings/organization
/settings/scheduling
/settings/configuration
```

Remove or redirect:

```text
/settings/departments -> /people/departments
/settings/job-titles  -> /people/job-titles
/settings/groups      -> context-specific target if possible, otherwise deprecated
```

## Component Refactoring

### Extract Reusable Resource Domain Components

Create or reuse generic components where they reduce duplication without over-abstracting.

Suggested components:

```text
ResourceDomainTabs
ResourceGroupList
ResourceCapabilityList
ResourceGroupDialog
ResourceCapabilityAssignmentDialog
```

Avoid premature abstraction if current implementation is small. Prefer clear code over generic indirection.

## People Page Tabs

Implement tabs:

```text
People | Groups | Absences | Departments | Job Titles | Skills
```

Each tab must use the existing data model and service layer where possible.

### People > Departments

Move the current departments management UI from Settings into People.

Requirements:

- list departments,
- create department,
- edit department,
- delete/deactivate department,
- support parent department if already implemented,
- preserve validation rules,
- preserve existing API contracts if possible.

### People > Job Titles

Move the current job titles management UI from Settings into People.

Requirements:

- list job titles,
- create job title,
- edit job title,
- delete/deactivate job title,
- preserve validation rules.

### People > Groups

Show only groups for people resources.

Requirements:

- filter by `resourceType = PEOPLE`,
- create groups with `resourceType = PEOPLE`,
- prevent accidental editing of space/tool groups from this tab,
- use clear page copy: `Organize people into reusable resource groups.`

### People > Skills

Show Criteria applicable to people.

Requirements:

- filter criteria by `applicableResourceTypes contains PEOPLE`,
- create or assign criteria as people skills depending on existing model,
- avoid duplicating global criteria definitions,
- if creating from this tab, default applicability to PEOPLE.

## Spaces Page Tabs

Implement tabs:

```text
Spaces | Groups | Capabilities | Floorplan
```

The exact tab order may be adjusted if preserving current user behavior is more important. If the current Spaces page is mainly floorplan-oriented, use:

```text
Floorplan | Spaces | Groups | Capabilities
```

### Spaces > Groups

Show only groups for space resources.

Requirements:

- filter by `resourceType = SPACE`,
- create groups with `resourceType = SPACE`,
- prevent accidental editing of people/tool groups from this tab,
- use clear page copy: `Organize spaces into reusable resource groups.`

### Spaces > Capabilities

Show Criteria applicable to spaces.

Requirements:

- filter criteria by `applicableResourceTypes contains SPACE`,
- when creating a capability here, default applicability to SPACE,
- preserve compatibility with existing capability/requirement matching logic,
- avoid duplicating global criteria definitions.

### Spaces > Floorplan

Preserve the existing floorplan upload and drawing experience.

Requirements:

- do not regress existing upload flow,
- do not regress space drawing/editing,
- do not regress the spaces list sidebar,
- do not change floorplan data model as part of this UI refactor unless strictly necessary.

## Settings Page Cleanup

Remove tabs from Settings after their new homes work:

- Departments
- Job Titles
- resource-specific Groups

Keep:

- Criteria
- Templates
- Presets
- Users
- Organization
- Scheduling
- Configuration

## Backward-Compatible Redirects

Implement redirects for old settings URLs.

Required redirects:

```text
/settings/departments -> /people/departments
/settings/job-titles  -> /people/job-titles
```

For `/settings/groups`, choose based on current semantics:

- If old groups were people groups, redirect to `/people/groups`.
- If old groups were space groups, redirect to `/spaces/groups`.
- If ambiguous, keep a temporary compatibility page with links to People Groups and Space Groups.

## Error Handling

Preserve existing error handling patterns.

Each tab must handle:

- loading state,
- empty state,
- API failure state,
- validation errors,
- permission denied state.

## Testing Expectations

Add or update frontend tests for:

- Settings no longer showing moved tabs,
- People tabs render correctly,
- Spaces tabs render correctly,
- resource-type filters are applied,
- old routes redirect correctly,
- create dialogs default the correct resource type.
