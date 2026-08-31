# Authorization & roles

Single source of truth for the tenant authorization model. Both the backend gates and the frontend
read-only UI follow this contract. **AI agents and contributors: read this before adding or changing
any endpoint.**

## The three-tier contract

| Tier | Core content<br>(Requests, People, Teams, Spaces, Availability, Utilization, Conflicts, Search) | Settings area<br>(`/settings`: Criteria, Templates, Presets, Scheduling) | Administration area<br>(`/tenant-admin`: Users, Organization, Configuration, Integrations/reporting-tokens, Usage & Limits/quotas, Export) | Sites & tenant settings |
|------|------|------|------|------|
| **Viewer** | read | **no access** | **no access** | read (list/get) |
| **Editor** | read + write | read + write | **no access** | read (list/get) |
| **Admin**  | read + write | read + write | read + write | read + write |

- "Write" = `POST` / `PUT` / `PATCH` / `DELETE`. "Read" = `GET` / `HEAD`.
- **Sites are special:** the site list/get must stay readable by every member (Requests, Utilization
  and Spaces all need it), but creating/editing/deleting a site is Admin-only (site management lives
  in the Administration area).
- **Tenant settings (`/api/settings`) are the same shape as Sites:** the GET is read by every member
  (e.g. the auto-schedule flow reads tenant config — scheduling/working-hours — via
  `useTenantSettings`), but PUT/DELETE are Admin-only (managed in Administration → Configuration).
- Role ordering is `None < Viewer < Editor < Admin`
  ([AuthorizationContext.cs](../backend/core/Security/AuthorizationContext.cs)). `CanEdit` = Role ≥ Editor.

## Backend — verb-aware group conventions

Declare a group's policy **once**, at the `MapGroup`. A filter gates by HTTP method, so every new
write endpoint is protected by default. Defined in
[AuthorizationExtensions.cs](../backend/src/Middleware/AuthorizationExtensions.cs).

| Convention | Reads | Writes | Use for |
|------------|-------|--------|---------|
| `RequireMemberReadEditorWrite()` | member | Editor+ | general tenant content (the default) |
| `RequireMemberReadAdminWrite()` | member | Admin | content read app-wide but governed (Sites, tenant settings, resource types and their custom fields — the reads serve resource pages and forms) |
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

### The one documented exception: the MCP server

`/api/mcp` carries every call — reads and writes alike — over a single `POST`, because that is what
the Model Context Protocol's transport does. A verb-aware write gate cannot tell `tools/list` from
`tools/call`, so applying one would demand Editor merely to *discover* the tools, and a read-only
token could do nothing at all.

So that group declares `RequireTenantMembership()` plus an explicit `AuthorizationGoverned` marker,
and each **tool** gates itself through one shared guard:

```csharp
// McpToolGuards — every mutating tool checks the same threshold the HTTP write gate does.
public static void RequireWrite(IAuthorizationContext authorization, string tool)
{
    if (!authorization.CanEdit)
        throw new McpException($"The '{tool}' tool needs the 'schedule:write' scope. …");
}
```

The threshold is not duplicated: `IAuthorizationContext.CanEdit` (`Role >= Editor`) is the single
source of truth both paths read. A token's role comes from its scopes —
`schedule:write` → Editor, `schedule:read` → Viewer — resolved in `ContextEnrichmentMiddleware`, so
an automated caller passes through the *same* membership and role checks a human does.

The surface is 17 tools across four `[McpServerToolType]` classes — `ScheduleTools` (the board),
`PlanningTools` (critical path, dependencies, capacity), `AutoScheduleTools` (solver preview and
apply) and `LifecycleTools` (creating work, blocking resource time). One asymmetry is deliberate:
`auto_schedule_preview` needs only `schedule:read`, matching the HTTP `/preview` endpoint, which
carries `AllowMemberWrite()` because it persists nothing.

**`schedule:write` is a broader grant than its name suggests.** It now covers creating requests,
drawing dependency edges and marking resources unavailable — not just moving existing work. Tokens
issued before those tools existed gained the ability the moment they shipped, without anyone
re-consenting, which is why it is recorded here rather than left implicit. The scope was not split
because the alternative — a third scope — buys precision at the cost of a vocabulary every admin
must understand, and no customer has yet asked to grant rescheduling without creation. When one
does, `PlatformApiScopes.ScopeToRole` is where it lands. Until then three things contain the
breadth: `create_request` deliberately exposes no start or end timestamp, so new work cannot be
placed without going through a conflict-checked path; every tool carries honest `ReadOnly` /
`Destructive` annotations, which is the only confirmation signal a client has under our stateless
transport; and every tool call is logged by a single pipeline filter with its tool, arguments and acting token id — attribution a tool cannot forget to provide.

This is the only place a group may skip the three conventions, and it is covered by
`McpEndpointsTests`, whose refusal theory names **every** write tool by name — exhaustively, since
without a verb-aware gate an ungated write would otherwise be invisible — and asserts that list
matches exactly the tools the server advertises as destructive.

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

## Feature entitlements — plan gating is not authorization

Roles decide *what a member may do*; entitlements decide *what the workspace has bought*. They are
separate mechanisms and must not be conflated.

**The server computes and enforces entitlements.** `IFeatureGate` resolves them from the tenant's
plan plus any per-tenant overrides, and the gated endpoints enforce them (402/404). SaaS backs this
with subscription tiers; Community's `AllFeaturesEnabledGate` allows everything.

**The client reads the result — it never re-derives it.** The session payload carries an
`entitlements` map per membership (`FeatureKeys.Enforced`, produced by `ITenantEntitlementProvider`).
Gate presentation with `useFeatureEnabled(FeatureKeys.X)`; it fails closed on a missing key, matching
the server's default-deny.

> **Never gate a server-enforced feature on the plan code.** That duplicates the plan → feature table,
> ignores per-tenant overrides, and is exactly how a single casing slip — the session shipping the
> display label `"Enterprise"` where clients compare the code `"enterprise"` — silently locked five
> features for every paying tenant, and showed self-hosted Community users padlocks advertising plans
> they cannot buy.

The plan code (`contracts/plans.ts`, `planIncludesPremiumFeatures`) is only for product decisions the
server computes no entitlement for. Today that is auto-schedule availability and the Sites tab.

**Wire rule:** the plan travels as its machine code (lowercase, `subscription_tiers.code`), never
`display_name`. `TenantPlanInfo` carries both; only `PlanCode` goes on the wire.

**Known gap:** `FeatureKeys.AutoSchedule` has no entitlement row and no endpoint gate, so the
"premium plan" rule for auto-schedule lives only in the frontend hook — the endpoint is reachable by
an unentitled tenant. Fixing it means seeding the quota row and adding `EnsureEnabledAsync` to
`AutoScheduleEndpoints`, after which it moves onto the entitlement map like the other four.
