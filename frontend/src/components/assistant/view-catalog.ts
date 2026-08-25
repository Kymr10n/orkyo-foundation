import { ROUTE_SETTINGS, ROUTE_TENANT_ADMIN } from "@foundation/src/constants/auth";

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
  scheduling: { label: "Scheduling", path: "/" },
  requests: { label: "Requests", path: "/requests" },
  insights_overview: { label: "Insights → Overview", path: "/insights/overview" },
  insights_utilization: { label: "Insights → Utilization", path: "/insights/utilization" },
  insights_conflicts: { label: "Insights → Conflicts", path: "/insights/conflicts" },
  organization: { label: "Organization", path: "/organization" },
  stations: { label: "Stations", path: "/stations" },
  assets: { label: "Assets", path: "/assets" },
  floorplan: { label: "Floorplan", path: "/stations/floorplan" },

  settings_criteria: { label: "Settings → Criteria", path: `${ROUTE_SETTINGS}/criteria` },
  settings_templates: { label: "Settings → Templates", path: `${ROUTE_SETTINGS}/templates` },
  settings_scheduling: { label: "Settings → Scheduling", path: `${ROUTE_SETTINGS}/scheduling` },

  admin_sites: { label: "Administration → Sites", path: `${ROUTE_TENANT_ADMIN}/sites` },
  admin_users: { label: "Administration → Users", path: `${ROUTE_TENANT_ADMIN}/users` },
  admin_ai_assistant: { label: "Administration → Assistant", path: `${ROUTE_TENANT_ADMIN}/ai-assistant` },
  configuration_resource_types: { label: "Configuration → Resource types", path: "/configuration/resource-types" },
};

/**
 * Single-record views. Each opens the record's edit dialog through the app's existing
 * `?edit=<id>` convention (see `useEditQueryParam`), so none of these needed new plumbing.
 */
const ENTITIES: Record<string, { label: string; page: string }> = {
  request: { label: "request", page: "/requests" },
  site: { label: "site", page: `${ROUTE_TENANT_ADMIN}/sites` },
  template: { label: "template", page: `${ROUTE_SETTINGS}/templates` },
  criterion: { label: "criterion", page: `${ROUTE_SETTINGS}/criteria` },
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
