/**
 * Tenant role constants - mirrors backend RoleConstants.cs
 *
 * These MUST stay in sync with backend/api/Constants/RoleConstants.cs
 * Any changes to these values must be coordinated across FE/BE.
 */

export const Roles = {
  Admin: "admin",
  Editor: "editor",
  Viewer: "viewer",
  None: "none",
} as const;

