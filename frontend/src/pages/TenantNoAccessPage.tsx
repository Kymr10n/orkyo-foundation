import { ShieldX, LayoutGrid, LogOut, Mail, Shield } from 'lucide-react';
import { Button } from '@foundation/src/components/ui/button';
import { useAuth } from '@foundation/src/contexts/AuthContext';
import { AUTH_EVENTS } from '@foundation/src/constants/auth';
import { navigateToApex } from '@foundation/src/lib/utils/tenant-navigation';
import { runtimeConfig } from '@foundation/src/config/runtime';
import { usePageTitle } from '@foundation/src/hooks/usePageTitle';

/**
 * Terminal page for an authenticated user who is not a member of the workspace
 * whose subdomain they are on.
 *
 * Before this existed, that state fell through to the route tree, where
 * RequireAuth showed "Redirecting to sign in…" and sent UNAUTHORIZED — an event
 * only the `ready` state handles, so the machine sat there forever (#102). It is
 * also deliberately *not* an automatic redirect: bouncing someone to a different
 * workspace than the URL they typed hides typos, stale links and revoked access.
 */
export function TenantNoAccessPage() {
  const { sessionData, isSiteAdmin, send } = useAuth();
  usePageTitle('No access to this workspace');

  const hasOtherWorkspaces = (sessionData?.tenants.length ?? 0) > 0;

  // Apex "/" is the static marketing page, not the SPA — /login?auto=1 always
  // loads the app and lands on the tenant selector (same reasoning as the
  // machine's performLogin action).
  const goToMyWorkspaces = () => {
    if (!navigateToApex('/login?auto=1')) window.location.href = '/login?auto=1';
  };

  return (
    <div className="flex items-center justify-center min-h-screen bg-background">
      <div className="flex flex-col items-center gap-5 text-center max-w-md px-4">
        <ShieldX className="h-14 w-14 text-muted-foreground" />

        <div className="space-y-2">
          <h1 className="text-xl font-semibold">No access to this workspace</h1>
          <p className="text-muted-foreground text-sm">
            {hasOtherWorkspaces
              ? "Your account isn't a member of this workspace. Switch to one of your workspaces, or ask this workspace's administrator for an invitation."
              : "Your account isn't a member of this workspace. Ask this workspace's administrator for an invitation."}
          </p>
        </div>

        {hasOtherWorkspaces && (
          <Button onClick={goToMyWorkspaces}>
            <LayoutGrid className="mr-2 h-4 w-4" />
            Go to my workspaces
          </Button>
        )}

        {isSiteAdmin && (
          <Button variant="outline" size="sm" onClick={() => navigateToApex('/site-admin')}>
            <Shield className="mr-2 h-4 w-4" />
            Open site admin
          </Button>
        )}

        {runtimeConfig.supportEmail && (
          <Button variant="outline" size="sm" asChild>
            <a href={`mailto:${runtimeConfig.supportEmail}`}>
              <Mail className="mr-2 h-4 w-4" />
              Contact support
            </a>
          </Button>
        )}

        <Button
          variant="ghost"
          size="sm"
          className="text-muted-foreground"
          onClick={() => send({ type: AUTH_EVENTS.LOGOUT })}
        >
          <LogOut className="mr-2 h-4 w-4" />
          Sign out
        </Button>
      </div>
    </div>
  );
}
