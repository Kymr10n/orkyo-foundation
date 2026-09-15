# Foundation Frontend Architecture

## Shared components and routing strategy

`orkyo-foundation` provides a **domain-based rendering split** that routes on DNS domain plus
authentication state. Both products (SaaS, Community) use the same split, so routing logic exists
once.

> **See also:**
> - [`docs/UI-GUIDELINES.md`](docs/UI-GUIDELINES.md) — **canonical UI coding rules** (scroll ownership, `min-h-0`, dialogs/`FormDialog`, virtualization, shared primitives). Read this before building UI.
> - [`docs/UX-CONSISTENCY.md`](docs/UX-CONSISTENCY.md) — the UX friction audit those rules came from, plus the staged remediation backlog.
> - [`docs/COVERAGE.md`](docs/COVERAGE.md) — test-coverage policy (≥80% target), current numbers, and documented exceptions.
> - `orkyo-saas/frontend/INTEGRATION.md` — the product-side view: import model, dual-mode resolution, peer checks.

## The three rendering modes

### 1. Apex domain (orkyo.com in production)

**Entry:** `ApexGateway` (`src/components/auth/ApexGateway.tsx`)
**Purpose:** authentication pipeline

- Sign-up, login, password reset, email verification
- Keycloak OAuth flows
- No business-logic routes; a pure auth state machine (`src/machines/authMachine.ts`)
- Terminal state: redirect to the tenant subdomain, or hand over to `TenantApp` in local dev

### 2. Tenant subdomain (tenant.orkyo.com in production)

**Entry:** `TenantApp` (`src/components/auth/TenantApp.tsx`)
**Routes:** the whole application route tree: requests, resources (`ResourceClassPage` for
stations and assets, with `/spaces`, `/people` and `/floorplan` kept as redirects into it),
utilization, insights, settings, tenant admin, account.
**Authentication:** delegated to `AuthContext`; assumes the user is authenticated.

### 3. Local development (localhost:5173)

**Entry:** `LocalDevShell`
**Behaviour:**
- not authenticated → render `ApexGateway` (exactly as on the apex domain)
- authenticated → render `TenantApp` (exactly as on a tenant subdomain)
- one state machine, zero route duplication

## The reference shell: `src/App.tsx`

Foundation's `src/App.tsx` is the reference composition. Each product carries its own copy of the
same shape and adds its slots (below).

```typescript
function App() {
  const isLocalDev = !runtimeConfig.baseDomain;  // localhost → true
  if (isLocalDev) return <LocalDevShell />;      // ApexGateway until ready, then TenantApp

  const subdomain = getCurrentSubdomain();       // tenant.orkyo.com → "tenant"
  if (!subdomain) return <ApexGateway />;        // apex: auth pipeline
  return <TenantApp />;                          // tenant subdomain: the route tree
}
```

**Key property:** `TenantApp` routes are defined **once**, in foundation. Local dev and tenant
subdomains use the same route tree.

## How products consume foundation

Products import foundation by its **published package name**, never by a path alias:

```typescript
import { ApexGateway } from "@kymr10n/foundation/src/components/auth/ApexGateway";
import { TenantApp } from "@kymr10n/foundation/src/components/auth/TenantApp";
import type { Roles } from "@kymr10n/foundation/contracts/roles";
```

The `@foundation/*` alias exists only inside this repository (its `tsconfig.json` and
`vitest.config.ts`). In a product it is forbidden and rejected by CI
(`orkyo-saas/frontend/scripts/validate-foundation-imports.mjs`), because TypeScript `paths` does
not honour the package `exports` map and produces silently `any`-typed imports.

Resolution is dual-mode, chosen in each product's `vite.config.ts`: the sibling source tree when
`../../orkyo-foundation/frontend` exists on disk, the installed package's `.tsbuild` output
otherwise. `INTEGRATION.md` in orkyo-saas documents the mechanism.

There is no bridge module. A product re-exports a foundation component only where it needs a
product-local name for it (two such files exist in orkyo-saas, under `components/admin/`).

## Extension points: render slots

Products extend the shell through **render-prop slots**, not by adding routes. The slots are
optional props on the two foundation entry components; a product that passes none gets the
foundation behaviour.

| Slot | Component | What it renders |
|---|---|---|
| `renderAdminPage` | `ApexGateway` | the platform-admin page for a site admin (reached directly at `/site-admin`, or from the `no_tenants_admin` state) |
| `renderTenantSelectPage` | `ApexGateway` | the multi-tenant hub when a user belongs to several organisations |
| `accountTabs` | `ApexGateway`, `TenantApp` | extra tabs on the account page (SaaS adds "Plans") |
| `renderOnboardingExtraContent` | `ApexGateway` | extra content on the onboarding page |
| `reportingApiUnavailableRedirectTo`, `aiAssistantUnavailableRedirectTo` | `TenantApp` | where an unentitled feature sends the user |

SaaS (`orkyo-saas/frontend/src/App.tsx`) declares its slots once and passes the same set to every
branch of its shell, so the wiring cannot drift between local dev, apex and tenant subdomain.

Community (`orkyo-community/frontend/src/App.tsx`) has a single admin surface and no apex domain.
It currently renders its admin page by intercepting the pathname before `ApexGateway`/`TenantApp`;
the September 2026 design review (F1) converges it on the same slot API.

## Import boundary

- Foundation **never imports from SaaS or Community**.
- SaaS and Community only **consume**; neither extends foundation on behalf of the other.
- Product-specific UI (tenant admin, provisioning surfaces, plan cards, self-host setup) stays in
  the product repo. Behaviour with an analogue in the other product moves to foundation
  (`CLAUDE.md`, placement rule).

## Local development

All products follow the same startup pattern:

1. Start the infrastructure (Postgres, Valkey, Keycloak, Mailhog) with the product's `compose.local.yml` (`./dev.sh infra`)
2. Run the backend services (migrator, API, worker) on the host
3. Run the frontend with `npm run dev` (Vite dev server)
4. Open `http://localhost:5173` (SaaS) or `http://localhost:5174` (Community)
5. `runtimeConfig.baseDomain` is unset → local dev mode
6. `AuthContext` drives the local login flow → `LocalDevShell` switches to `TenantApp`
7. Routes behave exactly as on a production tenant subdomain

No duplication of routing logic across products. No private package feed is needed for local
development: the sibling checkout is used when present.

## Summary

| Concern | Owner | Reuse pattern |
|---------|-------|---------------|
| Auth pipeline (login/signup/oauth) | Foundation | products render `ApexGateway` |
| Business-logic routes | Foundation | products render `TenantApp` |
| Product-specific admin / hub / tabs | SaaS, Community | render slots on `ApexGateway` and `TenantApp` |
| Domain hooks, API modules, contracts | Foundation | imported by package path |
| Product-specific UI/UX | SaaS, Community | encapsulated in the product repo |
