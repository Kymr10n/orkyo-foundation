import {
  ROUTE_ASSETS,
  ROUTE_HOME,
  ROUTE_INSIGHTS_CONFLICTS,
  ROUTE_INSIGHTS_OVERVIEW,
  ROUTE_INSIGHTS_UTILIZATION,
  ROUTE_ORGANIZATION,
  ROUTE_REQUESTS,
  ROUTE_SETTINGS_CRITERIA,
  ROUTE_SETTINGS_SCHEDULING,
  ROUTE_SETTINGS_TEMPLATES,
  ROUTE_STATIONS,
  ROUTE_STATIONS_FLOORPLAN,
  ROUTE_TENANT_ADMIN_AI_ASSISTANT,
  ROUTE_TENANT_ADMIN_SITES,
  ROUTE_TENANT_ADMIN_USERS,
} from "@foundation/src/constants/auth";

/**
 * Where the assistant may take the person.
 *
 * The server sends an id from its own catalog
 * (`backend/core/Services/Ai/AiViewCatalog.cs`) and never a path — the routes live here,
 * on the side that owns routing. An id this file does not know is refused rather than
 * guessed at, so a server that grows a view before the client does moves nobody.
 *
 * Paths are the concrete destination, never a route that redirects on arrival: `/insights`
 * and `/stations` bounce to a child, and sending the router somewhere that immediately
 * redirects is what made the tour loop.
 */
export interface ViewTarget {
  /** What the panel writes in the chat log: "Opened <label>". */
  label: string;
  path: string;
}

const PAGES: Record<string, ViewTarget> = {
  scheduling: { label: "Scheduling", path: ROUTE_HOME },
  requests: { label: "Requests", path: ROUTE_REQUESTS },
  insights_overview: { label: "Insights → Overview", path: ROUTE_INSIGHTS_OVERVIEW },
  insights_utilization: { label: "Insights → Utilization", path: ROUTE_INSIGHTS_UTILIZATION },
  insights_conflicts: { label: "Insights → Conflicts", path: ROUTE_INSIGHTS_CONFLICTS },
  organization: { label: "Organization", path: ROUTE_ORGANIZATION },
  stations: { label: "Stations", path: ROUTE_STATIONS },
  assets: { label: "Assets", path: ROUTE_ASSETS },
  floorplan: { label: "Floorplan", path: ROUTE_STATIONS_FLOORPLAN },

  settings_criteria: { label: "Settings → Criteria", path: ROUTE_SETTINGS_CRITERIA },
  settings_templates: { label: "Settings → Templates", path: ROUTE_SETTINGS_TEMPLATES },
  settings_scheduling: { label: "Settings → Scheduling", path: ROUTE_SETTINGS_SCHEDULING },

  admin_sites: { label: "Administration → Sites", path: ROUTE_TENANT_ADMIN_SITES },
  admin_users: { label: "Administration → Users", path: ROUTE_TENANT_ADMIN_USERS },
  admin_ai_assistant: { label: "Administration → Assistant", path: ROUTE_TENANT_ADMIN_AI_ASSISTANT },
  configuration_resource_types: { label: "Configuration → Resource types", path: "/configuration/resource-types" },
};

/**
 * Single-record views. Each opens the record's edit dialog through the app's existing
 * `?edit=<id>` convention (see `useEditQueryParam`), so none of these needed new plumbing.
 */
const ENTITIES: Record<string, { label: string; page: string }> = {
  request: { label: "request", page: ROUTE_REQUESTS },
  site: { label: "site", page: ROUTE_TENANT_ADMIN_SITES },
  template: { label: "template", page: ROUTE_SETTINGS_TEMPLATES },
  criterion: { label: "criterion", page: ROUTE_SETTINGS_CRITERIA },
};

/**
 * Resolves a view id to somewhere to go, or null when this client does not know it.
 *
 * Role is deliberately not checked here: the server only offers each person the views their
 * role allows, and the routes themselves are guarded. Repeating the rule in a third place
 * would be a third place for it to drift.
 */
export function resolveView(view: string, entityId?: string | null): ViewTarget | null {
  const page = PAGES[view];
  if (page) return page;

  const entity = ENTITIES[view];
  if (!entity || !entityId) return null;

  return {
    label: `${entity.label} details`,
    path: `${entity.page}?edit=${encodeURIComponent(entityId)}`,
  };
}
