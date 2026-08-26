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
  calendarSubscriptions: () => ["calendar-subscriptions"] as const,
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
    /** Full request list incl. hierarchy — the Requests page. */
    list: () => ["requests", "list"] as const,
  },

  sites: {
    /** The tenant's site list (also its own invalidation prefix). */
    list: () => ["sites"] as const,
  },

  resources: {
    /**
     * Broad prefix over the `["resources", …]` namespace — reaches `byType` and
     * `utilizationGrid`, so one invalidation refreshes every per-type list. Deliberately does NOT
     * reach `allFlat`, which lives under its own root for the reason documented there.
     */
    all: () => ["resources"] as const,
    /** Resources of one type (e.g. the People list; also its own invalidation prefix). */
    byType: (resourceTypeKey: string) => ["resources", resourceTypeKey] as const,
    /**
     * Placeable resources at one site — everything the floorplan holds, across every type that
     * declares geometry. Sits under the `["resources", …]` prefix so `all()` reaches it.
     */
    placeable: (siteId: string | null) => ["resources", "placeable", siteId] as const,
    /**
     * Resources of one type backing that type's utilization grid — name/metadata lookup.
     * Deliberately a distinct key from `byType(typeKey)` (different fetch scope/staleness); do
     * not fold into it (that would change invalidation semantics). Keeps the
     * `["resources", typeKey]` prefix so per-type invalidations still reach it.
     */
    utilizationGrid: (typeKey: string) => ["resources", typeKey, "utilization-grid"] as const,
    /** Absences recorded for one resource. */
    absences: (resourceId: string) => ["resource-absences", resourceId] as const,
    /** Capability/skill assignments for one resource. */
    capabilities: (resourceId: string) => ["resource-capabilities", resourceId] as const,
    /**
     * Flat list of ALL active resources across types (availability-event scope picker).
     * Deliberately a distinct key from `byType` — different fetch/payload; do not fold
     * into the `["resources", …]` namespace (that would change invalidation semantics).
     */
    allFlat: () => ["resources-all"] as const,
  },

  resourceGroups: {
    /** Broad prefix — every resource-group query (use for invalidation). */
    all: () => ["resource-groups"] as const,
    /** Groups of one resource type (person teams, space groups, …). */
    byType: (typeKey: string) => ["resource-groups", typeKey] as const,
    /** Members of one resource group. */
    members: (groupId: string) => ["resource-group-members", groupId] as const,
    /**
     * Flat list of groups across ALL resource types (availability-event scope picker).
     * Deliberately a distinct key from `byType` — different fetch/payload; do not fold
     * into the `["resource-groups", …]` namespace (that would change invalidation semantics).
     */
    allFlat: () => ["resource-groups-all"] as const,
  },

  lists: {
    /** Every list key hangs off this, so a column change can invalidate rows too. */
    all: () => ["lists"] as const,
    definitions: () => ["lists", "definitions"] as const,
    definition: (definitionId: string) => ["lists", "definitions", definitionId] as const,
    sharedInstances: (definitionId: string) =>
      ["lists", "definitions", definitionId, "instances"] as const,
    instance: (instanceId: string) => ["lists", "instances", instanceId] as const,
    instanceRows: (instanceId: string) => ["lists", "instances", instanceId, "rows"] as const,
    /** Keyed by the pair, not the instance: the caller has these before an instance exists. */
    resourceInstance: (resourceId: string, fieldId: string) =>
      ["lists", "resource-instance", resourceId, fieldId] as const,
  },

  resourceTypeCatalog: {
    /** The catalog with each entry's tenant state (also its own invalidation prefix). */
    all: () => ["resource-type-catalog"] as const,
  },

  resourceTypes: {
    /** The tenant's resource types (also its own invalidation prefix). */
    all: () => ["resource-types"] as const,
    /** Custom field definitions of one resource type. */
    customFields: (resourceTypeId: string) =>
      ["resource-types", resourceTypeId, "custom-fields"] as const,
  },

  criteria: {
    /** Broad prefix — every criteria query (use for invalidation). */
    all: () => ["criteria"] as const,
    /** Criteria applicable to one resource type (e.g. person skills editor). */
    byResourceType: (resourceType: string) => ["criteria", { resourceType }] as const,
  },

  floorplan: {
    /** Floorplan view data (metadata + image) for one site. */
    viewData: (siteId: string | null) => ["floorplan-view-data", siteId] as const,
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
    /** Prefix matching every site's availability events (invalidation). */
    availabilityEventsAll: () => ["availability-events"] as const,
  },

  announcements: {
    /** Active announcements (with read state) for the current user. Returns the bare ["announcements"] key, so it doubles as the family's invalidation prefix. */
    active: () => ["announcements"] as const,
    /** Unread-count badge in the top bar. */
    unread: () => ["unread-announcements"] as const,
  },

  /**
   * Templates for one entity type (request/space/group). The single-element
   * `templates-${entityType}` shape is historical — a future improvement is
   * `["templates", entityType]` with a broad `["templates"]` prefix, but that
   * changes invalidation semantics, so the shape is kept byte-identical here.
   */
  templates: (entityType: string) => [`templates-${entityType}`] as const,

  users: {
    /** The tenant's user list (also its own invalidation prefix). */
    all: () => ["users"] as const,
  },

  invitations: {
    /** The tenant's pending invitations (also its own invalidation prefix). */
    all: () => ["invitations"] as const,
  },

  security: {
    /** The current user's security info (lock state, federation, MFA). */
    info: () => ["security-info"] as const,
  },

  sessions: {
    /** The current user's active sessions (also its own invalidation prefix). */
    all: () => ["sessions"] as const,
  },

  mfa: {
    /** The current user's MFA/TOTP status (also its own invalidation prefix). */
    status: () => ["mfa-status"] as const,
  },

  reportingTokens: {
    /** The tenant's reporting API tokens (also its own invalidation prefix). */
    all: () => ["reporting-tokens"] as const,
  },

  ai: {
    /** Everything AI-assistant related — the shared invalidation prefix. */
    all: () => ["ai"] as const,
    /** Whether a key is configured, and its display hint. */
    credential: () => ["ai", "credential"] as const,
    /** Per-user grants and this month's usage. */
    allowances: () => ["ai", "allowances"] as const,
    /** The workspace's daily interaction limits. */
    dailyLimits: () => ["ai", "daily-limits"] as const,
    /** Whether the current user can use the assistant right now. */
    status: () => ["ai", "status"] as const,
    /** The caller's saved conversations (titles only — bodies are fetched on demand). */
    conversations: () => ["ai", "conversations"] as const,
  },

  userProfile: {
    /** The current user's identity-provider profile (also its own invalidation prefix). */
    all: () => ["user-profile"] as const,
  },

  presetApplications: {
    /** The tenant's preset application history (also its own invalidation prefix). */
    all: () => ["preset-applications"] as const,
  },

  notificationPreferences: {
    /** The current user's notification preferences (also its own invalidation prefix). */
    all: () => ["notification-preferences"] as const,
  },

  tenantSettings: {
    /** Broad prefix — every tenant-settings scope (use for invalidation). */
    all: () => ["tenant-settings"] as const,
    /** A single scope: "current", a tenant slug, or the "__site__" sentinel. */
    scope: (cacheKey: string) => ["tenant-settings", cacheKey] as const,
  },

  preferences: {
    /** The current user's preferences (also its own invalidation prefix). */
    all: () => ["preferences"] as const,
  },

  quotas: {
    /** The current tenant's quotas (also its own invalidation prefix). */
    tenant: () => ["tenant-quotas"] as const,
  },

  insights: {
    /** Broad prefix — every insights query (use for invalidation). */
    all: () => ["insights"] as const,
    overview: (siteId: string | null, from: Date, to: Date) =>
      ["insights", "overview", siteId, iso(from), iso(to)] as const,
    utilization: (resourceType: string, siteId: string | null, from: Date, to: Date, bucket: string) =>
      ["insights", "utilization", resourceType, siteId, iso(from), iso(to), bucket] as const,
    conflicts: (siteId: string | null, from: Date, to: Date, bucket: string) =>
      ["insights", "conflicts", siteId, iso(from), iso(to), bucket] as const,
    requests: (siteId: string | null, from: Date, to: Date, bucket: string) =>
      ["insights", "requests", siteId, iso(from), iso(to), bucket] as const,
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
    audit: (page: number, action: string | null) => ["admin", "audit", page, action] as const,
  },
} as const;
