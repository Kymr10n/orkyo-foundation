/**
 * RequireEditor — route guard for the Settings area.
 *
 * Renders children only for editors and admins (role ≥ Editor per useCanEdit).
 * Viewers are redirected to the app root — they have no write access and no
 * need to see the configuration surfaces (criteria, templates, presets, scheduling).
 *
 * Assumes it sits inside <RequireAuth> (membership already resolved).
 */

import { useEffect } from "react";
import { Navigate } from "react-router-dom";
import { toast } from "sonner";
import { useCanEdit } from "@foundation/src/hooks/usePermissions";

export function RequireEditor({ children }: { children: React.ReactNode }) {
  const canEdit = useCanEdit();

  useEffect(() => {
    if (!canEdit) {
      toast.error("Settings are available to editors and administrators only.");
    }
  }, [canEdit]);

  if (!canEdit) {
    return <Navigate to="/" replace />;
  }

  return <>{children}</>;
}
