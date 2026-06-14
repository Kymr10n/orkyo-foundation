# UI and Process Changes

## 1. UX goal

Expose the new model without forcing users to think like data modellers.

The UI should use simple business language:

- Spaces: `Site`
- People: `Home Site`, `Current Site`, `Available for other sites`
- Tools: `Home Site`, `Current Site`, `Available for other sites`
- Requests: `Site` with an explicit `Any site` option

## 2. Master data UI

### 2.1 Spaces

Do not show technical mobility fields.

Show:

```text
Site
```

Rules:

- Site is required.
- Space cannot be marked as available cross-site.
- Space cannot be moved operationally.
- If site is changed, treat as master-data correction and warn if existing schedules are affected.

### 2.2 People

Add fields:

```text
Home Site
Current Site
Available for other sites
```

Recommended defaults:

- Home Site: required after migration hardening
- Current Site: defaults to Home Site
- Available for other sites: default true or tenant-configurable

UI behavior:

- If current site differs from home site, show a small badge: `Temporarily at Basel`.
- In people lists, allow filtering by home site and current site.
- In request assignment dialogs, show local people first, then cross-site candidates.

### 2.3 Tools

Same UI pattern as people:

```text
Home Site
Current Site
Available for other sites
```

Additional optional later fields:

```text
Requires transport
Transport lead time
Return expected by
```

Do not implement these initially unless already needed.

## 3. Request UI

Add or adjust request site field:

```text
Site: Any site | Zurich | Basel | Geneva
```

Recommended label:

```text
Where can this request be fulfilled?
```

Options:

- `Any site` → `site_id = NULL`
- specific site → `site_id = selected site`

For advanced future configuration, this can become:

```text
Site scope:
- Any site
- Specific site
- Preferred site
- Selected sites
```

Do not implement advanced options initially.

## 4. Scheduling UI

### 4.1 Space scheduling

When scheduling a request onto a space:

- If request has no site, allow any matching space.
- If request has a site, filter or rank spaces from that site first.
- If user attempts to schedule site-scoped request onto another site's space, block with clear error.

Message:

```text
This request is scoped to Zurich and cannot be scheduled into a Basel space.
```

### 4.2 People assignment

When assigning people to a scheduled request:

Sort candidates:

1. current site equals execution site,
2. home site equals execution site,
3. cross-site allowed candidates,
4. non-matching candidates with warning/conflict.

Show concise badges:

```text
Local
Home site
Cross-site
Not available cross-site
```

### 4.3 Tools assignment

Same pattern as people.

For tools, the current site is more operationally important than home site.

Sort candidates:

1. current site equals execution site,
2. cross-site allowed,
3. non-matching with conflict.

## 5. Conflict UI

Add conflict messages for site/location mismatches.

Examples:

```text
Space is at Basel, but the request is scoped to Zurich.
```

```text
Person is currently assigned to Basel and is not available for cross-site work in Zurich.
```

```text
Tool is currently located at Geneva. Cross-site use is allowed, but movement may be required.
```

Severity:

- Blocking error for space/request site mismatch.
- Error for cross-site not allowed.
- Warning for cross-site allowed but current site differs.
- Info when execution site is unknown.

## 6. Settings and tenant policy

Add tenant-level configuration if not already available:

```text
Default cross-site availability for people: true/false
Default cross-site availability for tools: true/false
Cross-site mismatch enforcement: warn/block
```

Recommended initial defaults:

```text
People cross-site default: true
Tools cross-site default: true
Enforcement: warn for allowed cross-site movement, block for not allowed
```

## 7. Process changes

### 7.1 Resource onboarding

When creating a person/tool:

1. Select Home Site.
2. Current Site defaults to Home Site.
3. Decide whether the resource can be used cross-site.

### 7.2 Temporary movement / lending

Initial simple process:

- Edit resource current site manually.
- Scheduling immediately uses the new current site.

Later optional process:

- Add movement events with start/end date.
- Add approval workflow.
- Add transport lead time.

### 7.3 Request creation

User decides whether the request is site-neutral or site-specific.

Default recommendation:

- If user is creating request from within a selected site context, default to that site but allow `Any site`.
- If user is creating request globally, default to `Any site`.

### 7.4 Reporting implications

Built-in insights and later PowerBI views should distinguish:

- requests by requested site scope,
- execution site derived from schedule/space,
- resource home site,
- resource current site.

Recommended reporting dimensions:

```text
request_site_id
execution_site_id
resource_home_site_id
resource_current_site_id
cross_site_assignment_flag
```

## 8. UI acceptance criteria

- Spaces still look simple and site-bound.
- People/tools can be created with home/current site.
- Request can be marked `Any site` or site-specific.
- Scheduling prevents impossible space assignments.
- Assignment dialogs highlight local vs cross-site people/tools.
- Conflict page includes site/location conflicts.
- Existing workflows remain usable for single-site tenants.
