# Foundation placement audit — 2026-05

> Per the "Foundation placement is the default" and "no unilateral architecture changes" rules: this audit identifies code in the two product repos that *might* belong in foundation. **No code moves are made as part of this audit.** Each candidate becomes its own follow-up with explicit approval.

Scope of this pass: `orkyo-saas/backend/src/` and `orkyo-community/backend/src/`. The Api/Worker/Migrator layers were not surveyed; they're wiring code and typically product-specific.

## Method

For each non-trivial type in a product `src/` tree, ask: does the *other* product have an analogue or a hole where this would slot in via the existing `OrgContext`/`IDbConnectionFactory` abstractions? If yes → candidate for foundation. Multi-tenancy or a `tenantId` parameter alone is **not** a reason to keep something in SaaS.

## Candidates (in priority order)

### 1. Audit service — `IAuditService` abstraction

| Where today | Type | Notes |
|---|---|---|
| SaaS | `ControlPlaneAuditService.cs` | Writes audit rows scoped to control-plane (`tenants`, `tenant_users`). |
| Community | `CommunityAuditService.cs` | Writes audit rows for a single-org. |

**Recommendation:** extract an `IAuditService` interface to foundation with a default `AuditService` implementation parameterised by table/columns. Products keep a thin adapter that knows their schema specifics.

**Why now:** both implementations exist independently. Any new audit row type currently requires touching both files.

### 2. Quota enforcement — `IQuotaEnforcer`

| Where today | Type | Notes |
|---|---|---|
| SaaS | `ServiceTierPolicyProvider.cs`, `IServiceTierPolicyProvider.cs` + tier policies under `Security/Quotas/` | Tier-driven quota model. |
| Community | `CommunityQuotaEnforcer.cs` | Single-org quota; flat limits. |

**Recommendation:** extract an `IQuotaEnforcer` to foundation with a generic policy contract. SaaS supplies the tier-aware policy; Community supplies the flat policy.

**Why now:** quota errors are user-visible; divergence between the two means UI/UX drift. Unifying the contract reduces that risk.

### 3. Break-glass session store — `IBreakGlassSessionStore`

| Where today | Type | Notes |
|---|---|---|
| SaaS | `ValkeyBreakGlassSessionStore.cs` + `BreakGlassSessionService.cs` | Valkey-backed break-glass admin sessions. |
| Community | `NullBreakGlassSessionStore.cs` | No-op (single-org, no separate admin plane). |

**Recommendation:** lift the `IBreakGlassSessionStore` interface (likely already in SaaS) plus `BreakGlassSessionService` orchestrator into foundation. Community's `Null*` implementation is a textbook null-object adapter — it already proves the abstraction works.

**Why now:** the `Null*` adapter in Community is a placement-rule code smell — it exists *only* because the abstraction sits in SaaS, when it should sit in foundation with both implementations as adapters.

### 4. Configuration extensions — `BotProtectionServiceExtensions`, `RateLimitingServiceExtensions`

| Where today | Type | Notes |
|---|---|---|
| SaaS | `Configuration/BotProtectionServiceExtensions.cs`, `Configuration/RateLimitingServiceExtensions.cs` | DI wire-up for bot-protection and rate-limiting. |
| Community | (absent) | Community has no bot-protection or rate-limiting wired. |

**Recommendation:** consider whether Community *should* have these and, if so, move the extensions to foundation as opt-in `Add*` methods. If Community deliberately ships without rate-limiting (acceptable for a single-org self-hosted edition), then these extensions are legitimately SaaS-specific and should stay.

**Why now:** the gap means Community is less hardened than SaaS by default. The fix is either to wire Community with the foundation helper (and accept the cost) or to make the omission deliberate in docs.

### 5. Tenant resolvers — `SingleTenantResolver` vs `SaasTenantResolver`

| Where today | Type | Notes |
|---|---|---|
| SaaS | `Services/SaasTenantResolver.cs` + `Services/SaasTenantContext.cs` + `Middleware/TenantMiddleware.cs` | Resolves tenant per request via host/path. |
| Community | `Tenant/SingleTenantResolver.cs` + `Tenant/SingleTenantOptions.cs` + `Middleware/SingleTenantMiddleware.cs` | Always resolves to the single configured org. |

**Recommendation:** confirm there's already an `IOrgContextAccessor` / `IOrgContextResolver` in foundation (the README claims so). If yes, both implementations are *already* foundation-shaped — keep them in product repos as adapters. If not, lift the contract to foundation and rebase both resolvers on it.

**Why now:** tenant resolution is the single hottest cross-cutting concern; a fragmented contract makes every middleware reviewer's life harder.

### 6. DB connection factories — `DbConnectionFactory` vs `CommunityDbConnectionFactory`

| Where today | Type | Notes |
|---|---|---|
| SaaS | `Services/DbConnectionFactory.cs` | Resolves per-tenant + control-plane connections. |
| Community | `Services/CommunityDbConnectionFactory.cs` | Resolves the single DB. |

**Recommendation:** the foundation README claims an `IDbConnectionFactory` abstraction already exists. If both products *do* implement it, this is fine as-is. If they don't, that's the gap to close — foundation owns the interface, products own the impl.

**Why now:** lowest urgency on the list; only worth touching if a future story needs to share connection logic.

## Items that are correctly SaaS-only (NOT candidates)

These have no Community analogue and the architecture cleanly says so:

- `TenantProvisioningService` and `InvitationService` — multi-tenant provisioning flows; Community is single-tenant by design.
- `TenantActivityFlushService` — flushes per-tenant activity metadata to the control plane.
- `Endpoints/TenantEndpoints.cs`, `Endpoints/TenantReactivationEndpoints.cs`, `Endpoints/InterestEndpoints.cs` — SaaS marketing + lifecycle surfaces.
- `Models/ServiceTierLimits.cs`, `Models/InterestRegistration.cs` — SaaS-only data shapes.

## Items that are correctly Community-only

- `CommunityJitProvisioningMiddleware.cs` — auto-creates the single org on first request. No analogue makes sense for SaaS (which explicitly provisions tenants).

## Suggested order of follow-ups

1. **Audit service** (#1) — small, clean abstraction; immediate win.
2. **Break-glass session store** (#3) — the `Null*` adapter is the strongest "this belongs in foundation" tell.
3. **Quota enforcer** (#2) — bigger surface, more design work needed.
4. Configuration extensions (#4) — decide product policy first (does Community get rate-limiting?), then refactor.
5. Tenant resolvers (#5) and DB connection factory (#6) — only if a current bug or feature motivates touching them; otherwise leave alone.

Each item is a separate PR with explicit approval. Do not bundle.
