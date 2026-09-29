/**
 * localStorage key constants.
 * Centralized to prevent string literal duplication and ensure consistent key naming.
 */

export const STORAGE_KEYS = {
  /** Current tenant slug */
  TENANT_SLUG: 'tenant_slug',
  /** Shell layout preferences (collapse flags + theme) — the layout store's persist key */
  LAYOUT: 'orkyo.layout',
  /** Last selected site in the site picker — the site store's persist key */
  SELECTED_SITE_ID: 'orkyo.site',
  /** Requests page tree: expanded node ids and tree/list mode — the request-tree store's persist key */
  REQUEST_TREE: 'orkyo.requestTree',
  /** Assistant panel width, in pixels */
  ASSISTANT_WIDTH: 'orkyo.assistant.width',
  /** Prefix of the per-tab resource-type filter; the tab's URL parameter name follows */
  TYPE_FILTER_PREFIX: 'orkyo.typeFilter.',
} as const;
