# Authorization & roles

Single source of truth for the tenant authorization model. Both the backend gates and the frontend
read-only UI follow this contract. **AI agents and contributors: read this before adding or changing
any endpoint.**

## The three-tier contract

| Tier | Core content<br>(Requests, People, Teams, Spaces, Availability, Utilization, Conflicts, Search) | Settings area<br>(`/settings`: Criteria, Templates, Presets, Scheduling) | Administration area<br>(`/tenant-admin`: Users, Organization, Configuration, Integrations/reporting-tokens, Usage & Limits/quotas, tenant Settings, Export) | Sites |
|------|------|------|------|------|
| **Viewer** | read | **no access** | **no access** | read (list/get) |
| **Editor** | read + write | read + write | **no access** | read (list/get) |
| **Admin**  | read + write | read + write | read + write | read + write |

- "Write" = `POST` / `PUT` / `PATCH` / `DELETE`. "Read" = `GET` / `HEAD`.
- **Sites are special:** the site list/get must stay readable by every member (Requests, Utilization
  and Spaces all need it), but creating/editing/deleting a site is Admin-only (site management lives
  in the Administration area).
- Role ordering is `None < Viewer < Editor < Admin`
  ([AuthorizationContext.cs](../backend/core/Security/AuthorizationContext.cs)). `CanEdit` = Role ≥ Editor.

## Backend — verb-aware group conventions

Declare a group's policy **once**, at the `MapGroup`. A filter gates by HTTP method, so every new
write endpoint is protected by default. Defined in
[AuthorizationExtensions.cs](../backend/src/Middleware/AuthorizationExtensions.cs).

| Convention | Reads | Writes | Use for |
|------------|-------|--------|---------|
| `RequireMemberReadEditorWrite()` | member | Editor+ | general tenant content (the default) |
| `RequireMemberReadAdminWrite()` | member | Admin | content read app-wide but governed (Sites) |
| `RequireAdminArea()` | Admin | Admin | the Administration area |
| `AllowMemberWrite()` *(per-route)* | — | — | opt a **non-mutating** POST (validate/preview) out of the write gate |

```csharp
var group = app.MapGroup("/api/requests").RequireAuthorization().RequireMemberReadEditorWrite();
group.MapPost("/validate", Validate).AllowMemberWrite();   // computes, does not persist

var sites = app.MapGroup("/api/sites").RequireAuthorization().RequireMemberReadAdminWrite();
var users = app.MapGroup("/api/users").RequireAuthorization().RequireAdminArea();
```

### Rules for every new endpoint

1. Every tenant-scoped group **must** declare exactly one of the three conventions.
2. A non-mutating POST (validation/preview that does not persist) uses `.AllowMemberWrite()`.
3. Never leave a write ungated; never gate general content at Admin, nor admin content below Admin.
4. Genuinely self-service / pre-login routes (`/api/auth`, `/api/session`, `/api/account`,
   `/api/preferences`, `/api/contact`, `/api/feedback`, `/api/announcements`, `/api/invitations`)
   are the only writes allowed without a convention — they are allow-listed in the conformance test.
5. Platform/site-admin routes use `RequireSiteAdmin()` (also stamps the governance marker).

### The guardrail

[`AuthorizationContractTests`](../backend/tests/Authorization/AuthorizationContractTests.cs)
enumerates the live endpoint graph and **fails CI if any mutating `/api` route is neither governed
nor allow-listed**. [`AuthorizationMatrixTests`](../backend/tests/Authorization/AuthorizationMatrixTests.cs)
locks the Viewer/Editor/Admin behaviour per tier. Add a write without a convention and these fail.

## Frontend — Viewer read-only UI

The backend enforces security; the frontend mirrors it so Viewers see a read-only UI instead of
buttons that 403.

- [`useCanEdit()`](../frontend/src/hooks/usePermissions.ts) — true for Editor/Admin (and site
  admins). Use it to `disabled`/hide write affordances. Mirrors the backend `CanEdit`.
- [`useIsTenantAdmin()`](../frontend/src/hooks/usePermissions.ts) — gates the Administration nav item
  and the `/tenant-admin` route (`RequireTenantAdmin`).
- **Every edit dialog's Save/submit (and destructive) control is disabled when `!canEdit`** — either
  directly or via the shared [`DialogFormFooter`](../frontend/src/components/ui/DialogFormFooter.tsx).
  A Viewer can open a dialog read-only but cannot submit.
- **Route guards:**
  - [`RequireEditor`](../frontend/src/components/auth/RequireEditor.tsx) — wraps `/settings`;
    Viewers are redirected to `/` with a toast. Settings nav link is also hidden from Viewers in
    [`SidebarNav`](../frontend/src/components/layout/SidebarNav.tsx).
  - [`RequireTenantAdmin`](../frontend/src/components/auth/RequireTenantAdmin.tsx) — wraps
    `/tenant-admin`; non-admins are redirected. Administration nav link hidden from non-admins.
