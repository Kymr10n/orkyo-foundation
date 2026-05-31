# 04 — Copilot Execution Prompt

You are working on Orkyo.

Implement the UX and architecture change described in this package.

## Goal

Refactor skills handling so that:

- `Settings → Criteria` remains the global criteria definition registry.
- `People → People List` is the place where skills are assigned to individual people.
- `People → Skills` is removed as a standalone management surface.
- Skills are represented as criterion values assigned to people, not as separate skill definitions.

## Required Behavior

1. Add a `Manage skills` action to each row in the People list.
2. Open a Manage Skills dialog/drawer for the selected person.
3. Load all criteria applicable to `person`.
4. Load the selected person's existing criterion values.
5. Allow assigning/updating/removing values using type-appropriate controls.
6. Add an `Add Skill Criterion` action inside the dialog.
7. Quick-created criteria must default to `person` applicability.
8. Remove the standalone People Skills tab from primary navigation.
9. Ensure backend validation prevents assigning non-person criteria to people.
10. Add or update tests and documentation.

## Architectural Constraints

- Do not create a separate `SkillDefinition` entity.
- Do not duplicate the criteria registry under People.
- Reuse existing criteria forms/components where possible.
- Reuse generic resource criterion assignment infrastructure where available.
- Keep resource type constants/enums centralized.
- Do not scatter literals like `person`, `space`, or `tool` across the codebase.
- Backend validation is mandatory. Frontend filtering alone is not acceptable.
- Prefer blocking unsafe delete/applicability changes over implementing cascading cleanup in this iteration.

## Investigation First

Before implementing, inspect the current codebase and identify:

- existing criteria entity/model
- existing applicability representation
- existing People page and tabs
- existing Skills tab implementation
- existing criterion value assignment APIs
- existing tests

Then implement the smallest clean refactor that achieves the target behavior.

## Expected Result

The final UX should be:

```text
Settings → Criteria
  Global list of all criteria
  Filterable by All / Spaces / People / Tools

People → People
  Table of people
  Each row has Manage skills action

Manage Skills Dialog
  Shows selected person
  Lists only person-applicable criteria
  Allows value assignment
  Allows quick-create of person-applicable criterion
```

## Validation

Run the relevant build, lint, and test commands for the repository.

If tests fail, determine the root cause before applying fixes. Do not mask failing tests by weakening assertions unless the assertion is outdated because of the intended UX change.
