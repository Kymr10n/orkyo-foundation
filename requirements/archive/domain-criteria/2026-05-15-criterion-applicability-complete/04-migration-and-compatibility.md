# 04 — Migration and Compatibility Plan

## Objective

Introduce criterion applicability without breaking existing behavior, existing data, or existing utilization logic.

## Migration Strategy

### Step 1 — Add Schema

Add normalized applicability table:

```sql
criterion_applicability
  criterion_id
  resource_type
```

Allowed resource types:

- `spaces`
- `people`
- `tools`

### Step 2 — Backfill Existing Criteria

All existing criteria should be assigned:

```text
spaces
```

This preserves the current behavior where criteria are already used as space capabilities and request requirements.

### Step 3 — Update API Read Model

Every returned criterion must include:

```json
"appliesTo": ["spaces"]
```

for migrated records.

### Step 4 — Update Create/Update Flows

After the migration, new criteria must require at least one applicability value.

### Step 5 — Update Consumers

Update every criteria consumer to use either:

- all criteria, when managing the global registry, or
- filtered criteria, when used inside a resource domain.

## Backward Compatibility

No existing criteria should disappear from:

- Settings → Criteria
- Spaces workflows
- Request requirement matching
- Utilization validation

If any legacy code expects criteria without `appliesTo`, normalize loaded data defensively in the frontend only during transition:

```ts
const appliesTo = criterion.appliesTo?.length ? criterion.appliesTo : ['spaces'];
```

Remove this fallback once backend/API guarantees the field.

## Request Matching Compatibility

Existing requests may already use criteria as requirements.

Until request requirements become explicitly scoped by resource type, the matching logic should evaluate criteria against the candidate resource type:

- when matching Spaces, use criteria applicable to Spaces,
- when matching People, use criteria applicable to People,
- when matching Tools, use criteria applicable to Tools.

Do not globally reject a request requirement simply because the criterion is not applicable to one resource type. Applicability must be evaluated in the context of the resource assignment being attempted.

## Rollback Considerations

Rollback is low-risk if:

- schema migration is additive,
- old code ignores the new table/field,
- backend does not remove old columns or old behavior.

Avoid destructive migration steps.

## Data Quality Considerations

After implementation, administrators may need to review existing criteria and add `people` or `tools` applicability where appropriate.

Do not attempt to infer applicability automatically from names except for explicitly controlled seed data.

## Seed Data

If seed/demo data exists, update it to include applicability.

Examples:

```text
capacity_persons      -> spaces
whiteboard            -> spaces
av_equipment          -> spaces, tools
power_400v            -> spaces, tools
forklift_certified    -> people
first_aid_certified   -> people
```
