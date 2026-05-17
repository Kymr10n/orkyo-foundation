# 06 — Acceptance Criteria

## Settings Page

- Settings no longer shows Departments as a tab.
- Settings no longer shows Job Titles as a tab.
- Settings does not show resource-type-specific Groups as a generic tab.
- Settings still shows global Criteria.
- Settings still shows Templates, Presets, Users, Organization, Scheduling, and Configuration if they existed before.
- Existing Settings functionality not touched by this refactor still works.

## People Page

- People page has tabs for People, Groups, Absences, Departments, Job Titles, and Skills/Criteria.
- Departments can be listed, created, edited, and deleted/deactivated from People.
- Job Titles can be listed, created, edited, and deleted/deactivated from People.
- People Groups shows only groups with resource type People.
- Creating a group from People creates a group with resource type People.
- People Skills/Criteria shows only criteria applicable to People.
- Creating a skill/criterion from People defaults applicability to People.
- Existing People and Absence functionality still works.

## Spaces Page

- Spaces page has tabs or equivalent navigation for Spaces/Floorplan, Groups, and Capabilities/Criteria.
- Existing floorplan upload still works.
- Existing floorplan empty state still works.
- Existing space drawing/editing still works.
- Space Groups shows only groups with resource type Space.
- Creating a group from Spaces creates a group with resource type Space.
- Space Capabilities/Criteria shows only criteria applicable to Space.
- Creating a capability/criterion from Spaces defaults applicability to Space.

## Routing

- `/settings/departments` redirects to `/people/departments`.
- `/settings/job-titles` redirects to `/people/job-titles`.
- `/settings/groups` either redirects to the correct resource-domain group page or shows a temporary disambiguation page.
- Direct navigation to new People and Spaces tabs works.
- Browser refresh on new routes works.

## Backend and Data

- Groups are filtered server-side by resource type.
- Criteria are filtered server-side by applicable resource type.
- Tenant isolation remains enforced.
- Existing data is preserved.
- No duplicate criteria records are created solely because the UI moved.
- No duplicate group records are created solely because the UI moved.

## Permissions

- Users without People administration rights cannot manage departments, job titles, people groups, or people skills.
- Users without Spaces administration rights cannot manage space groups or space capabilities.
- Users without Settings administration rights cannot manage global Settings unless existing rules permit it.
- Hidden buttons are not considered sufficient security; backend authorization must enforce the rules.

## Tests

- Frontend route tests updated or added.
- Frontend tab-rendering tests updated or added.
- API client tests updated where applicable.
- Backend tests cover resource-type filtering for groups.
- Backend tests cover resource-type filtering for criteria.
- Migration tests or integration checks verify existing records are preserved and correctly classified.

## Non-Regression

- Existing utilization logic still resolves space capabilities correctly.
- Existing request requirement matching still works.
- Existing people absence behavior still works.
- Existing settings unrelated to this refactor still work.
