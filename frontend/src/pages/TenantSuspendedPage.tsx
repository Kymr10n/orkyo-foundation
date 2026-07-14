import { useState } from 'react';
import { PauseCircle, RefreshCw, LogOut, Mail } from 'lucide-react';
import { Button } from '@foundation/src/components/ui/button';
import { useAuth } from '@foundation/src/contexts/AuthContext';
import { AUTH_EVENTS, SUSPENSION_REASON, TENANT_STATUS } from '@foundation/src/constants/auth';
import { API_BASE_URL, getApiHeaders } from '@foundation/src/lib/core/api-utils';
import { runtimeConfig } from '@foundation/src/config/runtime';
import { usePageTitle } from '@foundation/src/hooks/usePageTitle';

export function TenantSuspendedPage() {
  const { membership, send } = useAuth();
  const isDeleting = membership?.state === TENANT_STATUS.DELETING;
  usePageTitle(isDeleting ? 'Workspace scheduled for deletion' : 'Organization suspended');
  const [reactivating, setReactivating] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const canReactivate = membership?.canReactivate ?? false;
  const reason = membership?.suspensionReason ?? 'unknown';
  const isInactivity = reason === SUSPENSION_REASON.INACTIVITY;

  async function handleReactivate() {
    setReactivating(true);
    setError(null);

    try {
      const res = await fetch(`${API_BASE_URL}/api/tenant/reactivate`, {
        method: 'POST',
        headers: getApiHeaders('POST'),
        credentials: 'include',
      });

      if (!res.ok) {
        const body = await res.json().catch(() => ({}));
        setError(body.message ?? body.error ?? 'Reactivation failed. Please try again.');
        return;
      }

      // Success: re-bootstrap auth state so membership refreshes.
      send({ type: AUTH_EVENTS.REACTIVATE });
    } catch {
      setError('Network error. Please check your connection and try again.');
    } finally {
      setReactivating(false);
    }
  }

  return (
    <div className="flex items-center justify-center min-h-screen bg-background">
      <div className="flex flex-col items-center gap-5 text-center max-w-md px-4">
        <PauseCircle className="h-14 w-14 text-amber-500" />

        <div className="space-y-2">
          <h1 className="text-xl font-semibold">
            {isDeleting ? 'Workspace scheduled for deletion' : 'Organization suspended'}
          </h1>
          <p className="text-muted-foreground text-sm">
            {isDeleting
              ? 'This organization is scheduled for permanent deletion after a long period of inactivity. Restore it now to keep your data.'
              : isInactivity
                ? 'This organization was automatically suspended due to 30 days of inactivity. All your data is safe.'
                : 'This organization has been suspended. Please contact support for more information.'}
          </p>
        </div>

        {canReactivate && (
          <>
            <Button onClick={handleReactivate} disabled={reactivating}>
              <RefreshCw className={`mr-2 h-4 w-4 ${reactivating ? 'animate-spin' : ''}`} />
              {reactivating
                ? isDeleting ? 'Restoring...' : 'Reactivating...'
                : isDeleting ? 'Restore workspace' : 'Reactivate organization'}
            </Button>
            {error && <p className="text-destructive text-sm">{error}</p>}
          </>
        )}

        {!canReactivate && (
          <div className="space-y-2">
            <p className="text-muted-foreground text-xs">
              {isDeleting
                ? 'Ask an organization owner or admin to restore this organization, or contact support.'
                : 'Ask an organization owner or admin to reactivate this organization, or contact support.'}
            </p>
            {runtimeConfig.supportEmail && (
              <Button variant="outline" size="sm" asChild>
                <a href={`mailto:${runtimeConfig.supportEmail}`}>
                  <Mail className="mr-2 h-4 w-4" />
                  Contact support
                </a>
              </Button>
            )}
          </div>
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
