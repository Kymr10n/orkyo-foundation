# 03 — Acceptance Criteria

## Navigation

- The People page no longer exposes a standalone `Skills` tab as a primary management surface.
- The People list contains a `Manage skills` action for every person row.
- Settings still exposes the global `Criteria` page.

## Criteria Registry

- Settings Criteria lists all criteria.
- Criteria can be filtered by applicability: All, Spaces, People, Tools.
- Criteria display applicability badges.
- Creating a criterion from Settings requires or allows selecting applicability.
- Creating a criterion from the People skill dialog defaults applicability to `person`.

## Skill Assignment

- Clicking `Manage skills` opens a dialog/drawer for the selected person.
- The dialog shows the selected person's name.
- The dialog only lists criteria applicable to people.
- Existing person skill assignments are loaded and displayed correctly.
- Users can assign, update, and remove skill values.
- Values are edited using controls matching the criterion data type.

## Backend Rules

- The backend rejects assigning a non-person-applicable criterion to a person.
- The backend validates assignment values according to criterion type.
- The backend prevents deleting assigned criteria unless an explicit cleanup path exists.
- The backend prevents removing `person` applicability from criteria assigned to people unless an explicit cleanup path exists.

## UX Quality

- Empty state is clear when no person-applicable criteria exist.
- Quick-create keeps the user in the Manage Skills workflow.
- After quick-create, the new criterion appears in the dialog without a full page reload.
- Error messages are actionable and not raw exception dumps.
- The UI remains consistent with existing Orkyo styling and component patterns.

## Regression

- Existing Settings Criteria functionality still works.
- Existing People CRUD still works.
- Existing Groups, Absences, Job Titles, and Departments tabs still work.
- Existing request requirement/capability validation still works.
- Existing space criteria/capability behavior is not broken.

## Definition of Done

- Code compiles without warnings introduced by this change.
- Frontend tests pass.
- Backend tests pass.
- Linting passes.
- No duplicate skill definition model exists.
- No string-literal resource type scattering is introduced.
- Documentation is updated.
