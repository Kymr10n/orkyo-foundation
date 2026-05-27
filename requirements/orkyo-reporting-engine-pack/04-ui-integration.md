# UI Integration Specification

## 1. Navigation

Add a new top-level navigation item:

```text
Reports
```

Recommended placement: after Utilization and before Settings.

The navigation item should be visible only to users with `reports.view` or users whose role includes reporting access.

## 2. Route structure

Suggested routes:

```text
/reports
/reports/:reportKey
```

`/reports` shows the catalogue.

`/reports/:reportKey` shows the embedded report viewer.

## 3. Reports catalogue page

Layout:

- Page title: `Reports`
- Short description: `Operational insights for utilization, capacity, requests, and planning quality.`
- Category tabs or grouped sections:
  - Utilization
  - Requests
  - Capacity
  - Planning Quality
- Report cards with:
  - title
  - description
  - category
  - optional last refreshed timestamp
  - open button

Empty state:

```text
No reports are available for your role or tenant.
```

Access denied:

```text
You do not have permission to view reports.
```

## 4. Embedded report viewer page

Layout:

- Header with report title and breadcrumb back to Reports.
- Optional description.
- Embedded report iframe/component.
- Loading skeleton while embed token is requested.
- Error panel if token retrieval fails.

Important rules:

- The frontend must not send tenant ID.
- The frontend must not store Superset credentials.
- The frontend must not construct Superset URLs directly except from backend-provided embed payload.
- The frontend must request an embed token only for the selected `reportKey`.

## 5. Frontend API client

Suggested TypeScript types:

```ts
export type ReportDefinition = {
  key: string;
  title: string;
  description: string;
  category: string;
  isEnabled: boolean;
};

export type ReportEmbedTokenResult = {
  reportKey: string;
  embedUrl: string;
  token: string;
  expiresAtUtc: string;
};
```

Suggested API functions:

```ts
export async function getReports(): Promise<ReportDefinition[]>;
export async function createReportEmbedToken(reportKey: string): Promise<ReportEmbedTokenResult>;
```

## 6. React Query usage

Catalogue:

```ts
useQuery({
  queryKey: ["reports"],
  queryFn: getReports,
});
```

Embed token:

```ts
useQuery({
  queryKey: ["report-embed-token", reportKey],
  queryFn: () => createReportEmbedToken(reportKey),
  enabled: Boolean(reportKey),
  staleTime: 0,
  gcTime: 0,
});
```

Do not cache embed tokens longer than necessary.

## 7. Component structure

Suggested files:

```text
frontend/src/features/reports/
  api/reportApi.ts
  components/ReportCard.tsx
  components/ReportCategoryTabs.tsx
  components/ReportEmbedViewer.tsx
  pages/ReportsPage.tsx
  pages/ReportViewerPage.tsx
  types.ts
```

## 8. UX behavior

### Token expiration

If embed token expires, show a reload action:

```text
Your reporting session expired. Reload the report to continue.
```

### Reporting service unavailable

```text
Reports are currently unavailable. Core scheduling functionality is not affected.
```

### No dashboard provisioned

```text
This report has not been provisioned for this tenant yet.
```

This should be visible to tenant admins only. Non-admin users should receive a generic unavailable message.

## 9. Admin integration

In tenant/admin settings, add a read-only reporting status section later:

- Reporting enabled
- Provisioning status
- Available reports
- Last provisioning check
- Last successful data refresh

Do not expose Superset datasource IDs, database usernames, or internal dashboard IDs to tenant admins.

## 10. Design principle

The Orkyo UI owns navigation, permissions, and context.

Superset owns dashboard rendering only.
