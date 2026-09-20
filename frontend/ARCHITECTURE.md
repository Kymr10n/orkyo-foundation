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

## The product shell: `App.tsx`

Each product composes the shell in its own `src/App.tsx` (`orkyo-saas/frontend/src/App.tsx`,
`orkyo-community/frontend/src/App.tsx`) and adds its slots (below). Foundation ships the parts,
not an `App`. The shape is the same in both:

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

## Data fetching: components never talk to the server

A component or a page must not import `@tanstack/react-query` or `src/lib/core/api-client`.
Every query and every mutation lives in a domain hook under `src/hooks/use*.ts`, and the
component consumes the hook's result.

The hook owns the query key (`qk` from `src/lib/api/query-keys.ts`), the query function, the
freshness tier (`STALE.*`), and the mutation `meta` block that drives the central toast and
invalidation. The component owns rendering. A component that needs the query client — to
invalidate after an out-of-band change, for example — gets a named hook for that one action
(`useInvalidateRequestData`, `useInvalidateUserProfile`), not the client itself.

Two properties come out of this. One query key has one definition, so a key and its
invalidation prefix cannot drift apart. And a component test mocks one hook instead of
building a `QueryClient`.

`no-restricted-imports` in `eslint.config.js` enforces the rule for `src/components/**` and
`src/pages/**`, and the exception list is **empty**. The raw-dialog exemption block is the one
place that could have opened a hole, so it restates this ban instead of switching the rule off:
a dialog exemption can never hand a component its own `useQuery` back.

What the rule enforces is the two imports, not the whole of the heading. A component can still
call a `src/lib/api/*` function directly, from an effect or an event handler, and a substantial
minority still do. Those are the remaining migration, not sanctioned exceptions.

Count them rather than trusting a number written here, which rots:

```sh
grep -rl "from '.*lib/api/" src/components src/pages --include='*.tsx' | grep -v '\.test\.'
```

Widening the ban to the API modules would finish the job in one move. That is a separate decision
about a permanent rule, not something to add in passing.

## Error display: one surface per error

A failed mutation and a failed query surface differently, and never both ways at once.

- A **mutation** reports through the toast the central `MutationCache` fires from its
  `meta.successMessage` / `meta.errorMessage`. Do not add a second `toast.error` in a callback.
- A **query** reports inline, with `ErrorAlert` in the region the data was for. The page stays
  usable and the message sits where the missing content is.
- A **dialog** that keeps itself open on failure shows the message inline in its own
  `ErrorAlert`. Its mutation then declares `meta.suppressErrorToast: true`, because the user
  is already looking at the message. `useEntityFormDialog` does this for every dialog built
  on it. A dialog that **closes** itself on failure is the converse: its inline alert goes
  away with it, so it keeps the toast and does not suppress.

The test for which one applies is where the user's attention is. A dialog holds it; a
background mutation does not.

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
