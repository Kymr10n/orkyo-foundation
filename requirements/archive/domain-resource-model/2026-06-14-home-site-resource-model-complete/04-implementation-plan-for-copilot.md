# Implementation Plan for Copilot

## 0. Instruction to Copilot

Implement the Home Site / Current Site resource model incrementally. Preserve tenant isolation and avoid breaking existing data. Do not overbuild travel, lending workflow, or multi-site request scope in the first iteration.

Before changing code, inspect the current schema, migrations, API DTOs, validation services, and UI forms for spaces, people/resources, requests, scheduling, and conflicts.

## 1. Phase 1 — Codebase assessment

### Tasks

1. Locate current database tables/entities for:
   - sites,
   - spaces,
   - people/resources,
   - requests,
   - assignments,
   - schedules,
   - conflicts/validation.
2. Determine whether people and future tools use a generic `resources` table or separate tables.
3. Identify all places where `site_id` is assumed to exist only on spaces.
4. Identify request query APIs and whether they are currently tenant-wide, site-scoped, or time-window-scoped.
5. Identify frontend forms and grids that need updates.

### Output

Create a short assessment note before implementation:

```text
- Tables affected
- DTOs affected
- API endpoints affected
- UI screens affected
- Validation functions affected
- Migration risk
```

## 2. Phase 2 — Database migration

### Tasks

1. Add nullable `site_id` to requests.
2. Add `home_site_id`, `current_site_id`, and `cross_site_allowed` to people/resources.
3. Keep spaces using existing `site_id` unless the codebase already has a generic resource model that makes normalization trivial.
4. Add indexes for tenant/site queries.
5. Add safe backfill for single-site tenants:
   - people/resources home/current site = only tenant site.
6. Do not force non-null constraints until UI remediation exists.

### Acceptance criteria

- Migration runs on empty database.
- Migration runs on seeded demo tenant.
- Migration runs with multi-site tenant data without destructive assumptions.
- Existing requests remain valid with `site_id = NULL`.
- Existing spaces remain unchanged.

## 3. Phase 3 — Backend domain model and DTOs

### Tasks

1. Update request entity/model with nullable `site_id`.
2. Update people/resource entity/model with:
   - `home_site_id`,
   - `current_site_id`,
   - `cross_site_allowed`.
3. Add domain helper:

```text
resolve_execution_site(request, assigned_space) -> Option<SiteId>
```

Resolution order:

1. assigned space site,
2. request site,
3. none.

4. Add helper for resources:

```text
resource_effective_current_site(resource)
```

For spaces, this should return their site.

5. Preserve backwards-compatible API fields where needed.

### Acceptance criteria

- Backend compiles.
- API responses include new fields for people/resources.
- Request create/update accepts nullable site.
- Existing frontend does not break before UI phase is complete.

## 4. Phase 4 — Validation logic

### Tasks

1. Extend validation to include site/location checks.
2. Add conflict types:
   - `SITE_MISMATCH_SPACE`
   - `SITE_MISMATCH_PERSON`
   - `SITE_MISMATCH_TOOL`
   - `CROSS_SITE_NOT_ALLOWED`
   - `EXECUTION_SITE_UNKNOWN`
3. Enforce:
   - request site vs assigned space site must match,
   - space cannot be cross-site,
   - people/tools assigned outside current site require `cross_site_allowed = true`,
   - if not allowed, create blocking conflict.
4. Keep validation tenant-safe.

### Acceptance criteria

- Site-scoped request cannot be scheduled into another site's space.
- Site-neutral request can be scheduled into any matching space.
- Person/tool outside execution site creates warning if cross-site allowed.
- Person/tool outside execution site creates error if cross-site not allowed.
- Single-site behavior remains unchanged except for new fields.

## 5. Phase 5 — API query updates

### Tasks

1. Update request listing APIs to optionally filter by:
   - `site_id`,
   - `include_site_neutral=true/false`,
   - time window, if already planned.
2. Ensure site-scoped views can include:
   - requests explicitly scoped to the site,
   - requests scheduled into spaces at the site,
   - optionally site-neutral unscheduled requests.
3. Avoid loading all tenant requests where a site/time window is available.

### Acceptance criteria

- Site-specific utilization view can fetch relevant requests.
- Unscheduled site-neutral requests remain available for scheduling.
- Conflict page can still evaluate all relevant scheduled conflicts tenant-wide or via dedicated conflict feed.

## 6. Phase 6 — Frontend UI updates

### Tasks

#### Spaces

- Keep UI label as `Site`.
- Do not expose cross-site fields.

#### People/resources

Add fields:

- `Home Site`
- `Current Site`
- `Available for other sites`

Add filters:

- by home site,
- by current site.

#### Requests

Add field:

```text
Site: Any site | specific site
```

Use `null` for Any site.

#### Scheduling dialogs

- Show local candidates first.
- Add badges: `Local`, `Cross-site`, `Not available cross-site`.
- Show concise validation messages.

#### Conflict UI

- Render new conflict types with clear text.

### Acceptance criteria

- User can create site-neutral request.
- User can create site-scoped request.
- User can assign home/current site to people/resources.
- Scheduling UI explains site mismatch without technical wording.

## 7. Phase 7 — Tests

### Backend tests

Add tests for:

1. request with site + matching space = valid,
2. request with site + non-matching space = conflict/error,
3. request without site + selected space = valid and execution site derived,
4. person at same current site = valid,
5. person at other current site + cross-site allowed = warning/valid,
6. person at other current site + cross-site not allowed = conflict/error,
7. tenant isolation: cannot use site/resource from another tenant,
8. single-site tenant migration backfill.

### Frontend tests

Add tests for:

1. request form site dropdown supports Any site,
2. people form shows home/current site,
3. spaces form does not show cross-site fields,
4. scheduler displays site mismatch messages,
5. candidate ordering local before cross-site.

## 8. Phase 8 — Demo seed updates

Update demo seed data to include:

- at least three sites,
- spaces bound to each site,
- people with different home/current sites,
- at least one cross-site allowed person,
- at least one non-cross-site person,
- future tools if tools exist,
- requests that are site-neutral,
- requests that are site-scoped,
- examples that intentionally create site/location conflicts.

## 9. Rollout strategy

1. Ship database migration with nullable fields.
2. Deploy backend compatibility layer.
3. Deploy UI remediation for home/current site.
4. Run demo seed update.
5. After stable period, consider enforcing non-null home/current site for people/tools.

## 10. Avoid these mistakes

- Do not make all resources hard site-bound like spaces.
- Do not make people globally unscoped forever.
- Do not force every request to have a site.
- Do not store execution site too early if it can be derived from the scheduled space.
- Do not expose unnecessary mobility fields for spaces.
- Do not break tenant isolation while adding site joins.

## 11. Final acceptance checklist

- [ ] All resources have a home site concept.
- [ ] Spaces remain immovable and simple in UI.
- [ ] People/tools support current site and cross-site usage.
- [ ] Requests support `Any site` and specific site.
- [ ] Scheduling derives execution site correctly.
- [ ] Validation detects site mismatch and cross-site violations.
- [ ] Conflict page shows meaningful messages.
- [ ] Existing tenants and demo seed continue to work.
- [ ] Query model is ready for site/time-window scoped utilization views.
