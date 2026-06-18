/**
 * Centralized React Query key factory.
 *
 * Why this exists: query keys were scattered as inline string-literal arrays, with
 * a query's full key and its `invalidateQueries` prefix often defined in different
 * files — easy to drift (rename a key and invalidation silently stops matching,
 * the exact bug class this prevents). `qk` is the single source of truth for the
 * high-traffic, invalidation-coupled domains.
 *
 * Conventions:
 *  - Specific helpers (e.g. `qk.requests.scheduled(...)`) return the full
 *    parameterized key a query uses.
 *  - `*All()` / broad-prefix helpers return the prefix used for invalidation
 *    (React Query matches by prefix), so a mutation can invalidate every variant.
 *  - Date params are serialized here (`toISOString`) so call sites stay terse and
 *    serialization can never drift between a query and its invalidation.
 *  - Keys are `as const` for literal-tuple types.
 *
 * Migration is incremental: the domains below are migrated; other keys still use
 * inline arrays and should be folded in here when next touched.
 */
const iso = (d: Date) => d.toISOString();

export const qk = {
  requests: {
    /** Broad prefix — every request query (use for invalidation). */
    all: () => ["requests"] as const,
    /** Scheduled requests for a site + window. */
    scheduled: (siteId: string | null, from: Date, to: Date) =>
      ["requests", "scheduled", siteId, iso(from), iso(to)] as const,
    /** Prefix matching every scheduled-window variant (optimistic updates / invalidation). */
    scheduledAll: () => ["requests", "scheduled"] as const,
    backlog: () => ["requests", "backlog"] as const,
    conflicted: () => ["requests", "conflicted"] as const,
  },

  spaces: {
    /** Broad prefix — every site's spaces (use for cross-site invalidation). */
    all: () => ["spaces"] as const,
    list: (siteId: string | null) => ["spaces", siteId] as const,
  },

  conflicts: {
    /** Broad prefix — the tenant-wide conflict registry (use for invalidation). */
    all: () => ["conflicts"] as const,
    /** Registry scoped to an optional window; "all" sentinel keeps the prefix stable. */
    window: (from?: Date, to?: Date) =>
      ["conflicts", from ? iso(from) : "all", to ? iso(to) : "all"] as const,
  },

  utilization: {
    byResource: (
      resourceTypeKey: string,
      siteId: string | null,
      from: Date,
      to: Date,
      granularity: string,
    ) =>
      ["utilization-by-resource", resourceTypeKey, siteId, iso(from), iso(to), granularity] as const,
    /** Prefix matching every utilization-by-resource variant (invalidation). */
    byResourceAll: () => ["utilization-by-resource"] as const,
    assignmentsByType: (resourceTypeKey: string, from: Date, to: Date) =>
      ["resource-assignments-by-type", resourceTypeKey, iso(from), iso(to)] as const,
    /** Prefix matching every assignments-by-type variant (invalidation). */
    assignmentsByTypeAll: () => ["resource-assignments-by-type"] as const,
  },

  scheduling: {
    settings: (siteId: string) => ["scheduling-settings", siteId] as const,
    availabilityEvents: (siteId: string) => ["availability-events", siteId] as const,
  },

  // Platform-admin surfaces (saas). Listed here because the admin-api they query
  // lives in foundation; consumed by saas via `@kymr10n/foundation/src/lib/api/query-keys`.
  // The bare-prefix helpers (e.g. `tenants()`) are for invalidation; the *Search /
  // *For helpers add the scoping param the query actually uses.
  admin: {
    tenants: () => ["admin", "tenants"] as const,
    tenantsSearch: (search: string | null) => ["admin", "tenants", search] as const,
    tenantsUsage: () => ["admin", "tenants-usage"] as const,
    users: () => ["admin", "users"] as const,
    usersSearch: (search: string | null) => ["admin", "users", search] as const,
    tenantMembers: () => ["admin", "tenant-members"] as const,
    tenantMembersFor: (tenantId: string | null) => ["admin", "tenant-members", tenantId] as const,
    subscriptionTiers: () => ["admin", "subscription-tiers"] as const,
    tenantQuotas: (tenantId: string) => ["admin", "tenant-quotas", tenantId] as const,
  },
} as const;
