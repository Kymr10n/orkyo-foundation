# 02 — Backend Implementation Instructions

## Objective

Extend the Criteria backend model, API contracts, validation, persistence, and query behavior to support criterion applicability scope.

## Required Behavior

Each Criterion must have one or more applicability values:

- `spaces`
- `people`
- `tools`

The backend must support:

- creating a criterion with applicability scope,
- editing applicability scope,
- retrieving all criteria,
- filtering criteria by applicability,
- preserving existing criteria through migration.

## API Contract Changes

### Criterion DTO

Add an applicability field.

```ts
appliesTo: CriterionApplicability[]
```

Equivalent backend DTO property:

```csharp
public IReadOnlyCollection<CriterionApplicabilityDto> AppliesTo { get; init; } = [];
```

Use whatever naming pattern already exists in the codebase, but keep the JSON contract as:

```json
{
  "appliesTo": ["spaces", "people"]
}
```

## Create Criterion Request

Add required `appliesTo`.

```json
{
  "name": "capacity_persons",
  "description": "Maximum number of persons supported by the space.",
  "dataType": "number",
  "appliesTo": ["spaces"]
}
```

Validation:

- `appliesTo` is required.
- Must contain at least one value.
- Values must be unique.
- Values must be from the allowed set.
- Reject unknown values.

## Update Criterion Request

Allow editing `appliesTo`.

Validation rules are identical to create.

## Query Filtering

Add optional query filter:

```http
GET /criteria?appliesTo=spaces
GET /criteria?appliesTo=people
GET /criteria?appliesTo=tools
```

Behavior:

- When no filter is supplied, return all criteria.
- When `appliesTo` is supplied, return only criteria that include that applicability value.
- Unknown filter values return `400 Bad Request`.

## Database Migration

Preferred schema:

```sql
CREATE TABLE criterion_applicability (
    criterion_id uuid NOT NULL REFERENCES criteria(id) ON DELETE CASCADE,
    resource_type text NOT NULL,
    PRIMARY KEY (criterion_id, resource_type),
    CONSTRAINT ck_criterion_applicability_resource_type
        CHECK (resource_type IN ('spaces', 'people', 'tools'))
);
```

Add indexes:

```sql
CREATE INDEX ix_criterion_applicability_resource_type
ON criterion_applicability(resource_type);
```

## Migration Defaults

Existing criteria must not disappear from current space and request workflows.

Therefore, existing criteria should default to:

```text
spaces
```

Rationale:

The current application already treats criteria mainly as space capabilities and request requirements. Defaulting existing criteria to spaces preserves existing behavior.

Optionally, if current request requirement logic is resource-agnostic, keep request-side matching compatible by selecting criteria based on the resource type being matched.

## Backend Domain Model

Avoid duplicating criterion concepts per resource type.

Do not create:

- `SpaceCriterion`
- `PeopleCriterion`
- `ToolCriterion`

unless those are assignment tables for resource instances.

The canonical definition remains `Criterion`.

## Validation Rules

Add validation close to request boundary.

Rules:

- Name remains unique according to existing uniqueness rules.
- Data type remains required.
- Applicability scope must contain at least one value.
- Applicability scope may contain multiple values.
- Duplicate scope values are normalized or rejected consistently.

Prefer rejecting invalid requests with clear messages.

## Security and Authorization

Use the same permission model currently used for managing Criteria.

No new privilege is required for the first implementation.

Later, resource-domain admins may be allowed to manage only criteria for their domain, but that is out of scope for this implementation.

## Testing

Add or update tests for:

- create criterion with one applicability value,
- create criterion with multiple applicability values,
- create criterion with empty applicability fails,
- create criterion with unknown applicability fails,
- update criterion applicability,
- list all criteria returns all entries,
- list criteria filtered by spaces,
- list criteria filtered by people,
- list criteria filtered by tools,
- migration backfills existing criteria with `spaces`.
