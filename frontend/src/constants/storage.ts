/**
 * localStorage key constants.
 * Centralized to prevent string literal duplication and ensure consistent key naming.
 */

export const STORAGE_KEYS = {
  /** Active tenant membership object */
  ACTIVE_MEMBERSHIP: 'active_membership',
  /** Current tenant slug */
  TENANT_SLUG: 'tenant_slug',
  /** Shell layout preferences (collapse flags + theme) — the layout store's persist key */
  LAYOUT: 'orkyo.layout',
  /** Last selected site in the site picker — the site store's persist key */
  SELECTED_SITE_ID: 'orkyo.site',
  /** Expanded node ids of the request tree */
  REQUEST_TREE_EXPANDED: 'requestTree.expandedIds',
  /** Tree vs. list view mode on the Requests page */
  REQUEST_VIEW_MODE: 'requestTree.viewMode',
} as const;
