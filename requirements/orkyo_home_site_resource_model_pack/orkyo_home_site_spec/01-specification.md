# Specification: Home Site / Current Site Resource Model

## 1. Objective

Introduce a consistent, flexible location model for Orkyo resources while keeping the user experience simple.

The model must support:

- spaces that are permanently bound to one site,
- people that administratively belong to a site but may work at another site,
- tools that administratively belong to a site but may be temporarily moved or lent,
- requests that may be site-specific or site-neutral,
- future resource types without redesigning the domain model.

## 2. Design principles

### 2.1 Separate ownership from execution location

Do not use one `site_id` field to mean everything.

Use distinct concepts:

- **Home Site**: the administrative or owning site of a resource.
- **Current Site**: the site where the resource is currently available or physically located.
- **Request Site Scope**: the site where the request must or should be fulfilled.
- **Scheduled Execution Site**: the site implied by the scheduled space or explicit scheduling decision.

### 2.2 Keep spaces as first-class resources, but immovable

Spaces should follow the same resource model conceptually, but with strict invariants:

- home site is mandatory,
- current site is always the same as home site,
- cross-site availability is always false,
- the UI should still display only `Site` for spaces to avoid unnecessary complexity.

### 2.3 Requests should not always require a site

A request may be:

- **site-specific**: must be fulfilled at a defined site,
- **site-neutral**: can be fulfilled at any suitable site,
- later: **site-preferred** or **multi-site eligible** if needed.

Initial recommendation:

```text
requests.site_id nullable
NULL = site-neutral / can be scheduled anywhere
value = site-scoped / must be scheduled at this site
```

## 3. Resource model

### 3.1 Space

A space has:

```text
home_site_id        required
current_site_id     derived or stored as same as home_site_id
cross_site_allowed  always false
```

Business rules:

- A space cannot be assigned to a request at another site.
- A space cannot be moved to another site.
- If a space changes site, that is a master-data correction, not operational movement.

### 3.2 Person

A person has:

```text
home_site_id        required or nullable during migration only
current_site_id     optional initially, default home_site_id
cross_site_allowed  configurable
```

Business rules:

- A person may be assigned to a request at their home site.
- A person may be assigned to another site if `cross_site_allowed = true`.
- If `cross_site_allowed = false`, assignment to another site should create a validation conflict or be blocked, depending on configured enforcement.
- Current site may be changed operationally to model temporary assignment/lending.

### 3.3 Tool

A tool has:

```text
home_site_id        required or nullable during migration only
current_site_id     optional initially, default home_site_id
cross_site_allowed  configurable
```

Business rules:

- A tool may be scheduled at its current site.
- A tool may be lent or moved to another site if cross-site movement is allowed.
- Cross-site assignment should be validated against current location and movement policy.

### 3.4 Request

A request has:

```text
site_id nullable
```

Meaning:

```text
NULL     = no fixed site; scheduler may choose any suitable site
site_id  = request is scoped to this site
```

Business rules:

- If a request has a site, selected space must belong to that site.
- If a request has no site and a space is selected, the execution site is derived from the space.
- If a request has no site and no space is selected, people/tools can be assigned, but site-based validation may be incomplete until execution site is known.
- The UI should clearly show whether the request is site-neutral or site-scoped.

## 4. Scheduling and validation rules

### 4.1 Execution site resolution

Resolve execution site in this order:

1. Scheduled space site, if a space is assigned.
2. Request `site_id`, if set.
3. Explicit schedule execution site, if introduced later.
4. Unknown / site-neutral.

Initial implementation may use only rules 1 and 2.

### 4.2 Space validation

- Space home site must equal execution site.
- If request has `site_id`, the selected space must have the same home site.
- Space cannot be cross-site.

### 4.3 People validation

For each assigned person:

- If execution site is unknown, only generic availability and criteria checks run.
- If execution site equals person current site: valid.
- If execution site differs from current site:
  - valid if `cross_site_allowed = true`, optionally with warning,
  - conflict if `cross_site_allowed = false`.

### 4.4 Tool validation

For each assigned tool:

- If execution site is unknown, only generic availability and criteria checks run.
- If execution site equals tool current site: valid.
- If execution site differs from current site:
  - valid if `cross_site_allowed = true`, optionally with warning,
  - conflict if `cross_site_allowed = false`.

### 4.5 Conflict types

Add or reuse conflict categories:

```text
SITE_MISMATCH_SPACE
SITE_MISMATCH_PERSON
SITE_MISMATCH_TOOL
CROSS_SITE_NOT_ALLOWED
EXECUTION_SITE_UNKNOWN
```

Severity recommendation:

- `SITE_MISMATCH_SPACE`: error
- `CROSS_SITE_NOT_ALLOWED`: error
- `SITE_MISMATCH_PERSON`: warning or error, depending on policy
- `SITE_MISMATCH_TOOL`: warning or error, depending on policy
- `EXECUTION_SITE_UNKNOWN`: info/warning

## 5. API behavior

### 5.1 Resource payloads

People/tools API responses should include:

```json
{
  "homeSiteId": "...",
  "currentSiteId": "...",
  "crossSiteAllowed": true
}
```

Spaces API responses should include either:

```json
{
  "homeSiteId": "...",
  "currentSiteId": "...",
  "crossSiteAllowed": false
}
```

or continue exposing only `siteId`, while internally mapping it to `homeSiteId`.

Recommendation: expose normalized fields in new API versions or DTOs, but keep `siteId` for spaces for backwards compatibility.

### 5.2 Request payloads

Requests should include:

```json
{
  "siteId": null
}
```

or:

```json
{
  "siteId": "site-uuid"
}
```

The meaning must be documented as site scope, not necessarily execution site.

## 6. Non-goals for initial implementation

Do not implement these in the first iteration unless already trivial:

- multi-site request scope,
- travel time calculation,
- cost allocation between sites,
- formal lending workflow with approvals,
- historical location timeline for people/tools,
- per-resource calendar for relocation events.

Prepare the model so these can be added later.
