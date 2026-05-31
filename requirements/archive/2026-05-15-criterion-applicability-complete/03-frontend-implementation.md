# 03 — Frontend Implementation Instructions

## Objective

Update the Criteria user interface and resource-domain screens so criteria can be tagged by applicability and filtered consistently.

## Settings → Criteria

The Settings Criteria page remains the global registry.

Add filter chips or tabs:

- All
- Spaces
- People
- Tools

Behavior:

- `All` shows every criterion.
- `Spaces` shows only criteria where `appliesTo` contains `spaces`.
- `People` shows only criteria where `appliesTo` contains `people`.
- `Tools` shows only criteria where `appliesTo` contains `tools`.

## Criteria List Display

Each criterion card/list item should show applicability badges.

Example:

```text
Capacity (persons)   Number   Spaces
AV Equipment         Boolean  Spaces · Tools
Forklift Certified   Boolean  People
```

Use existing design language:

- compact badges,
- dark-mode compatible,
- no excessive visual weight,
- align with current Boolean/Number badges.

## Create Criterion Dialog

Add a required section:

```text
Applies to *
[ ] Spaces
[ ] People
[ ] Tools
```

Validation:

- At least one checkbox must be selected.
- Show inline validation if none selected.
- Disable submit or show validation on submit, following existing form behavior.

Recommended default:

- Preselect `Spaces` when opened from Settings, to preserve current mental model.
- If opened from a resource-domain page later, preselect that resource domain.

## Edit Criterion Dialog

Allow editing applicability values.

Ensure that editing a criterion does not accidentally remove existing applicability values unless the user explicitly changes the checkboxes.

## API Client Changes

Update frontend types:

```ts
export type CriterionApplicability = 'spaces' | 'people' | 'tools';

export interface Criterion {
  id: string;
  name: string;
  description?: string;
  dataType: CriterionDataType;
  appliesTo: CriterionApplicability[];
  createdAt: string;
  updatedAt?: string;
}
```

Update create/update payloads accordingly.

## Resource-Domain Filtering

### Spaces Page

When assigning or selecting capabilities for spaces, only show criteria where:

```ts
criterion.appliesTo.includes('spaces')
```

The Spaces page may later get a dedicated `Capabilities` tab. That tab must consume only space-applicable criteria.

### People Page

When assigning skills/capabilities to people, only show criteria where:

```ts
criterion.appliesTo.includes('people')
```

The People page may expose this as `Skills` or `Capabilities`, depending on existing terminology.

### Tools Page

When tools are implemented, show only criteria where:

```ts
criterion.appliesTo.includes('tools')
```

## Empty States

When filtered view has no entries:

```text
No criteria defined for Spaces yet.
Create a criterion and mark it as applicable to Spaces.
```

Use equivalent wording for People and Tools.

## UX Rules

- Do not remove Settings → Criteria.
- Do not create separate criteria registries per resource type.
- Do not show People-only criteria in Spaces assignment dialogs.
- Do not show Space-only criteria in People skill dialogs.
- Multi-applicability criteria must appear in all relevant filtered views.

## Testing

Add frontend tests for:

- create dialog requires at least one applicability value,
- create payload includes `appliesTo`,
- edit dialog loads existing applicability values,
- filter chips/tabs filter correctly,
- criteria with multiple applicability values appear in multiple filtered views,
- resource-specific selectors only show applicable criteria.
