# Foundation Frontend Architecture

## Shared Components and Routing Strategy

`orkyo-foundation` provides a **domain-based rendering split** that routes based on DNS domain + authentication state. This pattern is used by all consuming products (SaaS, Community) to avoid duplicating routing logic.

## The Three Rendering Modes

### 1. Apex Domain (orkyo.com in production)

**Entry:** `ApexGateway`  
**Purpose:** Authentication pipeline

- Handle sign-up, login, password reset, email verification
- Manage Keycloak OAuth flows
- No business-logic routes; pure auth state machine
- Terminal state: redirect to tenant subdomain or local dev app

### 2. Tenant Subdomain (tenant.orkyo.com in production)

**Entry:** `TenantApp`  
**Routes:** All shared business-logic routes (requests, spaces, scheduling, feedback, etc.)  
**Authentication:** Delegated to `AuthContext`; assumes user is authenticated

### 3. Local Development (localhost:5173)

**Entry:** `LocalDevShell` wrapper  
**Behavior:**
- When not authenticated → render `ApexGateway` (exact same as apex domain)
- When authenticated → render `TenantApp` (exact same as tenant subdomain)
- **Single state machine, zero route duplication**

## How Foundation's App.tsx Works

```typescript
function App() {
  const isLocalDev = !runtimeConfig.baseDomain;  // localhost → true
  const subdomain = getCurrentSubdomain();         // tenant.orkyo.com → "tenant"

  if (isLocalDev) {
    // Local dev: mount LocalDevShell which handles auth→SPA transition
    return <LocalDevShell />;
  }

  if (!subdomain) {
    // Apex domain: mount ApexGateway for auth pipeline
    return <ApexGateway />;
  }

  // Tenant subdomain: mount TenantApp with business-logic routes
  return <TenantApp />;
}
```

**Key property:** `TenantApp` routes are defined **once**, here in foundation. Both local dev and tenant subdomains use the same route tree.

## How Products Consume Foundation's Components

### SaaS (`orkyo-saas/frontend/src/App.tsx`)

SaaS **imports and re-exports** foundation's components without duplication:

```typescript
import { ApexGateway } from "@foundation/src/components/auth/ApexGateway";
import { TenantApp } from "@foundation/src/components/auth/TenantApp";
import { AuthContext, useAuth } from "@foundation/src/contexts/AuthContext";

function App() {
  // Same logic as foundation
  // When authenticated, renders TenantApp (foundation's shared routes)
  // Can extend with SaaS-specific routes later (e.g., /admin)
}
```

**No duplicate auth pipeline, no duplicate SPA routes.**

### Community (orkyo-community/frontend/src/App.tsx)

Would use the **exact same pattern**—import foundation's components and extend with community-specific (standalone) routes.

## Extension Points for Products

### Multi-Tenant Admin Routes (SaaS)

SaaS can wrap `TenantApp` with a route-based branching layer:

```typescript
function SaasTenantAppWithAdmin() {
  // Route on /admin/* → AdminPage
  // Everything else → TenantApp (foundation routes)
}
```

The auth pipeline remains foundation's concern. The routing split is SaaS's extension.

### Standalone Bootstrap Routes (Community)

Community can wrap `TenantApp` with:

```typescript
function CommunityTenantAppWithSetup() {
  // Route on /setup/* → SetupWizard
  // Route on /admin/* → StandaloneAdminPage
  // Everything else → TenantApp (foundation routes)
}
```

Again, auth pipeline stays in foundation.

## Import Boundary and Bridge Pattern

To maintain clear separation while enabling gradual migration:

### Foundation

- Owns all shared auth components (`ApexGateway`, `TenantApp`, `AuthContext`, etc.)
- Owns all shared business-logic routes (requests, spaces, scheduling, etc.)
- Owns shared domain contracts and hooks

### SaaS

- Imports foundation's components via `@foundation/*` aliases
- Uses a **bridge module** (`src/lib/foundation/`) to re-export shared utilities for consistent import patterns
- Keeps SaaS-specific logic in SaaS (admin flows, tenant provisioning surfaces, multi-tenant UI enhancements)
- Gradually migrates SaaS-specific component implementations to foundation when shared with Community

### No Circular Dependencies

- Foundation **never imports from SaaS or Community**
- SaaS and Community only **consume, not extend for the other product**

## Local Development  

All products follow the same local startup pattern:

1. Start any required infrastructure (PostgreSQL, Redis, Keycloak, etc.) via `orkyo-infra` Docker Compose
2. Run backend services (**migrator**, API, worker) on host
3. Run frontend via `npm run dev` (Vite dev server)
4. Browser accesses `http://localhost:5173`
5. `runtimeConfig.baseDomain` is unset → local dev mode activated
6. AuthContext manages local login flow → `LocalDevShell` transitions to `TenantApp`
7. Frontend routes work identically to production tenant subdomain

No duplication of routing logic across products. No private package feeds required for local development.

## Summary

| Concern | Owner | Reuse Pattern |
|---------|-------|---------------|
| Auth pipeline (login/signup/oauth) | Foundation | All products import `ApexGateway` directly |
| Business-logic routes | Foundation | All products import `TenantApp` directly |
| SaaS-specific admin routes | SaaS | SaaS wraps foundation's `TenantApp` with route branching |
| Community-specific bootstrap routes | Community | Community wraps foundation's `TenantApp` with route branching |
| Domain hooks/contracts | Foundation | Products import via bridge modules |
| Product-specific UI/UX | SaaS/Community | Encapsulated in product repos |
