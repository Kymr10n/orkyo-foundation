# 05 — Migration and Compatibility Plan

## Goal

Move resource-specific administration from Settings into People and Spaces without data loss, route breakage, or duplicated implementation.

## Implementation Phases

### Phase 1 — Inventory Existing Implementation

Before changing code, identify existing files and APIs for:

- Settings tabs,
- Departments UI,
- Job Titles UI,
- Groups UI,
- Criteria UI,
- People page tabs,
- Spaces page layout,
- route definitions,
- API clients/hooks,
- backend controllers/services,
- authorization checks.

Document the findings in implementation notes before making structural changes.

Do not guess. Follow existing conventions.

### Phase 2 — Add Resource Type Filtering Where Missing

Ensure groups and criteria can be filtered by resource type.

Required filters:

```text
people
space
```

If the database already supports this, reuse it.

If not, add minimal schema changes:

- groups: add `resource_type`, default existing groups based on current usage,
- criteria: ensure `applicable_resource_types` or equivalent mapping exists.

Use additive migrations first. Avoid destructive migrations in the same phase.

### Phase 3 — Create New Domain Tabs

Add tabs to People:

- Groups
- Departments
- Job Titles
- Skills / Criteria

Add tabs to Spaces:

- Groups
- Capabilities

Preserve current People, Absences, and Spaces/Floorplan behavior.

### Phase 4 — Move Existing UI Components

Move or reuse existing Settings components.

Preferred pattern:

```text
Before:
settings/components/DepartmentsTab.tsx
settings/components/JobTitlesTab.tsx

After:
people/components/DepartmentsTab.tsx
people/components/JobTitlesTab.tsx
```

If components are generic, place them under a shared domain area only if there is clear reuse:

```text
components/resource-groups/ResourceGroupsTab.tsx
components/criteria/CriteriaList.tsx
```

Avoid dumping domain-specific components into a generic shared folder.

### Phase 5 — Settings Cleanup

Once new tabs work:

- remove Departments from Settings tabs,
- remove Job Titles from Settings tabs,
- remove resource-specific Groups from Settings tabs,
- keep global Criteria in Settings.

### Phase 6 — Compatibility Redirects

Add redirects for old URLs:

```text
/settings/departments -> /people/departments
/settings/job-titles  -> /people/job-titles
```

For `/settings/groups`, implement one of:

1. Redirect to `/people/groups`, if old groups were people-specific.
2. Redirect to `/spaces/groups`, if old groups were space-specific.
3. Render a temporary disambiguation page with two cards:
   - People Groups
   - Space Groups

The third option is safest if current semantics are unclear.

### Phase 7 — Cleanup Legacy Code

After tests pass:

- remove unused Settings tab components,
- remove unused imports,
- remove stale route constants,
- remove duplicate API client methods if aliases are no longer needed,
- update navigation labels and tests.

Do not leave dead code.

## Database Migration Guidance

Use expand-and-contract where schema changes are needed.

### Additive Migration

Add fields/mapping tables required for resource type filtering.

Example:

```sql
ALTER TABLE resource_groups ADD COLUMN resource_type text;
```

Backfill based on existing semantics.

Then make field non-null only after backfill is complete and application code writes it consistently.

### Backfill Rules

- If all existing groups are space groups, backfill `space`.
- If all existing groups are people groups, backfill `people`.
- If ambiguous, use a safe default only after inspecting code usage and seed data.

Do not assume blindly.

## Route Compatibility

Old direct links should not break.

At minimum, old routes should redirect for one release cycle.

## API Compatibility

If current frontend or tests use old settings endpoints, keep backend compatibility temporarily.

Preferred approach:

- new canonical endpoints under people/spaces/resource-groups,
- old endpoints as thin aliases if required,
- mark old endpoints internally as deprecated,
- remove old endpoints in a later cleanup if safe.

## Rollback Strategy

This refactor should be low-risk because it mostly moves UI ownership.

Rollback should be possible by:

- restoring Settings tabs,
- keeping data model additions harmless,
- keeping old API endpoints active until new routes are stable.
