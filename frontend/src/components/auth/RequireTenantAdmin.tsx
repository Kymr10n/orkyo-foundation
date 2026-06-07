/**
 * RequireTenantAdmin — route guard for the tenant Administration area.
 *
 * Renders children only for tenant admins (`membership.isTenantAdmin`, i.e.
 * tenant `role === "admin"`). Non-admins are redirected to the app root.
 *
 * Assumes it sits inside <RequireAuth> (membership already resolved), so it
 * makes a synchronous role decision without re-deriving auth state. This is
 * the tenant-scoped counterpart to the platform `canAccessAdminPage`
 * (isSiteAdmin) gate used for the /site-admin panel.
 */

import { useEffect } from "react";
import { Navigate } from "react-router-dom";
import { toast } from "sonner";
import { useAuth } from "@foundation/src/contexts/AuthContext";

export function RequireTenantAdmin({ children }: { children: React.ReactNode }) {
  const { membership } = useAuth();
  const isTenantAdmin = membership?.isTenantAdmin === true;

  useEffect(() => {
    if (!isTenantAdmin) {
      toast.error("Administration is available to tenant administrators only.");
    }
  }, [isTenantAdmin]);

  if (!isTenantAdmin) {
    return <Navigate to="/" replace />;
  }

  return <>{children}</>;
}
