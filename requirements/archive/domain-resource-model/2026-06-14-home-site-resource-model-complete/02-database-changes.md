# Database Changes: Home Site / Current Site Resource Model

## 1. Current issue

Spaces currently carry a site association. People are tenant-wide. Requests may not carry a site. This creates ambiguity:

- site-scoped request feeds cannot place requests without site context,
- people/tools cannot be properly validated against site execution,
- cross-site lending or movement cannot be represented cleanly,
- spaces, people, and tools are not modelled consistently as resources.

## 2. Target data model

### 2.1 Spaces

Spaces remain physically bound to a site.

Current field likely exists:

```sql
spaces.site_id
```

Target options:

#### Option A — Minimal change, recommended first

Keep:

```sql
spaces.site_id NOT NULL
```

Treat it as:

```text
home_site_id = site_id
current_site_id = site_id
cross_site_allowed = false
```

Pros:

- lowest migration risk,
- minimal UI/API disruption,
- compatible with existing site-scoped floorplan and space logic.

Cons:

- spaces are not physically stored with the same column names as people/tools.

#### Option B — Full normalization, later optional

Rename/add:

```sql
spaces.home_site_id NOT NULL REFERENCES sites(id)
spaces.current_site_id NOT NULL REFERENCES sites(id)
spaces.cross_site_allowed BOOLEAN NOT NULL DEFAULT false
```

Constraints:

```sql
CHECK (home_site_id = current_site_id)
CHECK (cross_site_allowed = false)
```

Recommendation: start with Option A and expose a normalized domain model in code.

### 2.2 People

Add to people/resources table, depending on current schema.

If people are stored in `resources`:

```sql
ALTER TABLE resources
ADD COLUMN home_site_id UUID NULL REFERENCES sites(id),
ADD COLUMN current_site_id UUID NULL REFERENCES sites(id),
ADD COLUMN cross_site_allowed BOOLEAN NOT NULL DEFAULT true;
```

If people have their own table:

```sql
ALTER TABLE people
ADD COLUMN home_site_id UUID NULL REFERENCES sites(id),
ADD COLUMN current_site_id UUID NULL REFERENCES sites(id),
ADD COLUMN cross_site_allowed BOOLEAN NOT NULL DEFAULT true;
```

Migration rule:

- Initially nullable to avoid breaking existing tenants.
- Backfill from tenant default site if available.
- If no clear default site exists, require admin remediation in UI.
- After remediation, consider making `home_site_id` mandatory.

### 2.3 Tools / future resources

If tools are introduced later, define from the start:

```sql
tools.home_site_id UUID NOT NULL REFERENCES sites(id),
tools.current_site_id UUID NOT NULL REFERENCES sites(id),
tools.cross_site_allowed BOOLEAN NOT NULL DEFAULT true
```

If tools are generic resources, use the same columns on `resources`.

### 2.4 Requests

Add nullable site scope:

```sql
ALTER TABLE requests
ADD COLUMN site_id UUID NULL REFERENCES sites(id);
```

Meaning:

```text
NULL = site-neutral request
value = request scoped to this site
```

Index:

```sql
CREATE INDEX idx_requests_site_id ON requests(site_id);
```

For tenant-safe querying, use composite indexes as appropriate:

```sql
CREATE INDEX idx_requests_tenant_site ON requests(tenant_id, site_id);
CREATE INDEX idx_requests_tenant_schedule ON requests(tenant_id, start_at, end_at);
```

Exact columns should match current names.

## 3. Derived execution site

Do not store execution site initially unless necessary.

Derive execution site from:

1. assigned space site,
2. request site,
3. null/unknown.

Later optional:

```sql
schedules.execution_site_id UUID NULL REFERENCES sites(id)
```

Only introduce this if requests without spaces still need a firm execution site.

## 4. Constraints and validation

### 4.1 Database constraints

Database should enforce structural integrity only:

- foreign keys to `sites`,
- tenant consistency where supported,
- non-null fields after migration hardening,
- immovable-space invariants if normalized.

Business validation should remain in backend validation services because it depends on scheduling context.

### 4.2 Tenant consistency

Every resource and request must remain tenant-scoped.

Validation must ensure:

```text
resource.tenant_id == site.tenant_id
request.tenant_id == site.tenant_id
assigned_resource.tenant_id == request.tenant_id
```

If the schema does not support composite foreign keys, enforce this in application validation and tests.

## 5. Backfill strategy

### Phase 1 — Add nullable columns

Add fields without breaking existing data.

```text
people.home_site_id nullable
people.current_site_id nullable
requests.site_id nullable
```

### Phase 2 — Backfill

For each tenant:

1. If tenant has exactly one site:
   - set all people home/current site to that site.
2. If tenant has multiple sites:
   - leave null or use a configured default site,
   - expose admin remediation screen/report.
3. Existing requests:
   - keep `site_id = NULL`, unless a scheduled space clearly implies the site and the product decision is to lock historical requests to that site.

Recommendation: do not backfill request site unless the original intent is known. Let scheduled space imply execution site.

### Phase 3 — UI remediation

Admin sees incomplete resources:

```text
People without home site
Tools without home site
Requests without explicit site, if relevant
```

### Phase 4 — Harden

After migration window:

- make people/tools `home_site_id` required,
- make people/tools `current_site_id` default to home site,
- keep requests `site_id` nullable by design.

## 6. Example migration pseudo-SQL

Adapt table and column names to the actual codebase.

```sql
-- requests
ALTER TABLE requests
ADD COLUMN IF NOT EXISTS site_id UUID NULL REFERENCES sites(id);

CREATE INDEX IF NOT EXISTS idx_requests_tenant_site
ON requests(tenant_id, site_id);

-- people/resources
ALTER TABLE people
ADD COLUMN IF NOT EXISTS home_site_id UUID NULL REFERENCES sites(id),
ADD COLUMN IF NOT EXISTS current_site_id UUID NULL REFERENCES sites(id),
ADD COLUMN IF NOT EXISTS cross_site_allowed BOOLEAN NOT NULL DEFAULT true;

CREATE INDEX IF NOT EXISTS idx_people_tenant_home_site
ON people(tenant_id, home_site_id);

CREATE INDEX IF NOT EXISTS idx_people_tenant_current_site
ON people(tenant_id, current_site_id);
```

## 7. Acceptance criteria

- Existing tenants continue to load after migration.
- Existing spaces remain site-bound.
- Existing requests remain valid with `site_id = NULL`.
- People can be assigned a home site and current site.
- Validation can detect person/tool assignment outside current site.
- Tenant isolation is preserved in all queries and validations.
