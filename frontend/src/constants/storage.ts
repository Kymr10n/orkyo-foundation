/**
 * localStorage key constants.
 * Centralized to prevent string literal duplication and ensure consistent key naming.
 */

export const STORAGE_KEYS = {
  /** Active tenant membership object */
  ACTIVE_MEMBERSHIP: 'active_membership',
  /** Current tenant slug */
  TENANT_SLUG: 'tenant_slug',
  /** Theme preference */
  THEME: 'theme',
  /** Last selected site in the site picker */
  SELECTED_SITE_ID: 'selectedSiteId',
  /** Sidebar collapsed state */
  SIDEBAR_COLLAPSED: 'sidebar-collapsed',
} as const;
