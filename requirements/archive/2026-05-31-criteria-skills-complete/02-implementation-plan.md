# 02 — Implementation Plan

## Implementation Principles

- Keep `Settings → Criteria` as the single source of truth for criterion definitions.
- Do not introduce a separate `SkillDefinition` entity.
- Treat skills as criterion assignments to person resources.
- Prefer generic resource/criteria assignment infrastructure over people-specific duplication.
- Enforce applicability rules in the backend.
- Keep the UI simple and operational: define globally, assign locally.

## Phase 1 — Inspect Current Implementation

Before changing code, inspect the current implementation and document the existing flow.

Find:

- criteria model/entity
- criteria API endpoints
- people page components
- people tabs implementation
- current skills tab implementation
- person skill assignment storage
- existing criterion value assignment APIs
- existing resource model abstractions

Expected locations may include, but are not limited to:

```text
frontend/src/pages/People*
frontend/src/components/people/*
frontend/src/components/settings/*
frontend/src/api/*
backend/**/Criteria*
backend/**/People*
backend/**/Resources*
backend/**/CriterionValues*
```

Do not start by adding new abstractions until the current model is understood.

## Phase 2 — Criteria Applicability Validation

Ensure every criterion has explicit applicability metadata.

The model should support at least:

```ts
applicableResourceTypes: ResourceType[]
```

or the backend equivalent.

Resource type values should include:

```text
person
space
tool
```

Use existing enum/constants if already present. Do not scatter string literals.

Backend must validate:

- criterion applicability when assigning to a resource
- value type against criterion type
- deletion or applicability changes when assignments exist

## Phase 3 — Settings Criteria Page

Update or confirm the Settings Criteria page behavior:

- list all criteria
- filter by All / Spaces / People / Tools
- show applicability badges
- create criteria with applicability selection
- edit criteria applicability safely

When editing applicability:

- if removing `person` from a criterion assigned to people, block the change or display a confirmation flow with cleanup
- prefer blocking initially to keep implementation safe and simple

## Phase 4 — Remove People Skills Tab as Management Surface

Remove the standalone `Skills` tab from People navigation.

Current tab list should become:

```text
People | Groups | Absences | Job Titles | Departments
```

Do not leave a second criteria assignment surface under `People → Skills`.

If removing the route is risky because of existing routing assumptions:

- keep route temporarily
- render an informational empty state
- provide a button back to `People`
- mark route as deprecated in code comments

Preferred outcome: remove the tab completely.

## Phase 5 — Add Manage Skills Action to People List

Add a dedicated action to each row in the People table.

Action label:

```text
Manage skills
```

Icon may be a sliders/settings/badge icon, but must have an accessible label.

The action opens:

```text
ManagePersonSkillsDialog
```

or equivalent component name following existing conventions.

## Phase 6 — Implement Manage Person Skills Dialog

Create or refactor a component for assigning person-applicable criteria to one person.

Responsibilities:

1. Load selected person details.
2. Load criteria filtered by `person` applicability.
3. Load existing criterion values for the person.
4. Render type-specific controls.
5. Save changed values.
6. Remove/clear assigned values.
7. Support quick-create of missing skill criteria.

Recommended component structure:

```text
ManagePersonSkillsDialog
  PersonSkillCriteriaList
  PersonSkillValueEditor
  AddSkillCriterionDialog
```

Keep it simple if existing component patterns prefer fewer files.

## Phase 7 — Type-Specific Value Editing

Implement a reusable criterion value editor if not already available.

Suggested abstraction:

```ts
CriterionValueEditor
```

Inputs:

```ts
criterion
value
onChange
readonly?
```

It should render based on criterion data type.

Supported initial types:

- Boolean
- Number
- Text
- Enum, if already supported

Do not invent unsupported types unless already present in the domain model.

## Phase 8 — Quick Create Skill Criterion

Inside the Manage Skills dialog, add:

```text
Add Skill Criterion
```

This opens the normal criterion creation flow with defaults:

```ts
applicableResourceTypes: ['person']
```

After successful creation:

- refresh person-applicable criteria
- keep the Manage Skills dialog open
- optionally focus the newly created criterion row

Avoid duplicating the full Settings criteria form if a reusable form component exists.

If no reusable form exists:

- extract one from Settings Criteria implementation
- reuse it in both Settings and People context

## Phase 9 — Backend Enforcement

Add or verify backend tests for:

1. Assigning a person-applicable criterion to a person succeeds.
2. Assigning a space-only criterion to a person fails.
3. Assigning invalid value type fails.
4. Removing `person` applicability from an assigned criterion fails.
5. Deleting assigned criterion fails or requires explicit cleanup.

Return clear API errors suitable for frontend display.

## Phase 10 — Frontend Tests

Add or update tests for:

1. People list renders Manage Skills action.
2. Clicking Manage Skills opens dialog for selected person.
3. Dialog only lists person-applicable criteria.
4. Boolean skill can be assigned/removed.
5. Number/text skill values can be saved where supported.
6. Add Skill Criterion defaults applicability to person.
7. Removed People Skills tab no longer appears.

## Phase 11 — Cleanup

Remove or deprecate:

- old People Skills tab component
- old duplicated criteria management code under People
- unused hooks/API clients
- stale tests
- unused route constants

Ensure no broken imports remain.

## Phase 12 — Documentation Update

Update project documentation to describe:

- criteria are global definitions
- skills are person criterion assignments
- applicability controls where criteria can be assigned
- people skills are managed from the People list per person

## Suggested Commit Breakdown

1. `refactor(criteria): enforce applicability model for resource assignments`
2. `feat(people): add manage skills action to people list`
3. `feat(people): implement manage person skills dialog`
4. `feat(criteria): support quick-create person skill criteria`
5. `refactor(people): remove standalone skills tab`
6. `test(criteria): add applicability and assignment coverage`
7. `docs(people): document skill assignment model`
