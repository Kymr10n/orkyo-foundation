# 04 — UI and Admin Integration

## Navigation

Add a tenant-admin page:

```text
Settings -> Integrations -> Reporting API
```

This page is only visible to tenant administrators or users with an explicit integration-management permission.

## Page Goals

The page should let a tenant admin:

- understand what the Reporting API is for
- create a read-only reporting token
- revoke existing tokens
- see usage status
- access API documentation
- copy basic Power BI connection guidance

## UI Sections

### 1. Overview Panel

Text:

```text
Use the Reporting API to connect Orkyo data to external tools such as Power BI, Excel, Metabase, Superset, or your own data platform. Reporting tokens are read-only and scoped to this tenant.
```

Include warning:

```text
Treat reporting tokens like passwords. Anyone with a valid token can read reporting datasets for this tenant.
```

### 2. Token List

Columns:

- Name
- Prefix
- Created by
- Created at
- Expires at
- Last used
- Status
- Actions

Actions:

- Revoke
- View usage log, optional later

Never show full token value after creation.

### 3. Create Token Dialog

Fields:

- Token name, required
- Expiry date, optional but recommended
- Scopes, optional for MVP; default `reporting:read`

After creation, show token once:

```text
Copy this token now. It will not be shown again.
```

Provide copy button.

### 4. Power BI Quick Start

Example guidance:

```text
Power BI Desktop -> Get Data -> Web
URL: https://<tenant>.orkyo.com/api/reporting/v1/allocations?from=2026-01-01&to=2026-01-31
Header: Authorization = Bearer <your-reporting-token>
```

Mention:

- use date filters for performance
- use incremental refresh with `updatedSince` where applicable
- prefer stable endpoints over scraping UI data

### 5. API Documentation Link

Link to OpenAPI/Swagger documentation filtered to Reporting API endpoints.

## Permissions

Create a permission such as:

```text
integrations.reporting.manage
```

Read-only reporting token creation should require this permission.

Using a reporting token must not grant interactive UI access.

## UX Safety Rules

- Do not allow token creation by ordinary users.
- Do not allow cross-tenant token administration.
- Do not show token secret after creation.
- Do not show database names, connection strings, or implementation details.
- Add confirmation dialog for revoke.

## Optional Future Enhancements

Do not include in MVP unless trivial:

- per-token endpoint scopes
- endpoint usage charts
- downloadable Power BI template file
- webhook for token usage anomaly
- token rotation helper
- IP allowlisting
- custom data retention configuration
