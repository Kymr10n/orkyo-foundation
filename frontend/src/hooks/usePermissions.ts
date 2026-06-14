import { useAuth } from "@foundation/src/contexts/AuthContext";

/** Tenant role strings as emitted by the backend (RoleConstants). */
export const TENANT_ROLE = {
  Admin: "admin",
  Editor: "editor",
  Viewer: "viewer",
  None: "none",
} as const;

/**
 * Whether the current user may edit tenant content (create/update/delete people,
 * requests, spaces, criteria, …). Mirrors the backend contract
 * (`AuthorizationContext.CanEdit` = Role ≥ Editor): Editors and Admins can edit, Viewers
 * cannot. Tenant admins and site admins (break-glass) are always editors.
 *
 * Use this to disable/hide write affordances so Viewers get a genuine read-only UI rather
 * than buttons that 403. Authorization is still enforced server-side — this is UX only.
 */
export function useCanEdit(): boolean {
  const { membership, isSiteAdmin } = useAuth();
  if (isSiteAdmin) return true;
  if (membership?.isTenantAdmin) return true;
  const role = membership?.role?.toLowerCase();
  return role === TENANT_ROLE.Editor || role === TENANT_ROLE.Admin;
}

/**
 * Whether the current user may access the Administration area (tenant admin or site admin).
 * The Administration nav item and `/tenant-admin` route are gated on this.
 */
export function useIsTenantAdmin(): boolean {
  const { membership, isSiteAdmin } = useAuth();
  return isSiteAdmin || membership?.isTenantAdmin === true;
}
