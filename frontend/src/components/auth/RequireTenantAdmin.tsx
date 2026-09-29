/**
 * RequireTenantAdmin — route guard for the tenant Administration area.
 *
 * Renders children only when `useIsTenantAdmin()` holds (tenant admin, or a
 * site admin on break-glass). Everyone else is redirected to the app root.
 *
 * Assumes it sits inside <RequireAuth> (membership already resolved), so it
 * makes a synchronous role decision without re-deriving auth state. This is
 * the tenant-scoped counterpart to the platform `canAccessAdminPage`
 * (isSiteAdmin) gate used for the /site-admin panel.
 */

import { useEffect } from "react";
import { Navigate } from "react-router";
import { toast } from "sonner";
import { useIsTenantAdmin } from "@foundation/src/hooks/usePermissions";
import { ROUTE_HOME } from "@foundation/src/constants/auth";

export function RequireTenantAdmin({ children }: { children: React.ReactNode }) {
  const isTenantAdmin = useIsTenantAdmin();

  useEffect(() => {
    if (!isTenantAdmin) {
      toast.error("Administration is available to organization administrators only.");
    }
  }, [isTenantAdmin]);

  if (!isTenantAdmin) {
    return <Navigate to={ROUTE_HOME} replace />;
  }

  return <>{children}</>;
}
