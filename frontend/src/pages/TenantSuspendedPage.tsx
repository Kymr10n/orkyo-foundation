import { useState } from 'react';
import { PauseCircle, RefreshCw, LogOut, Mail } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { useAuth } from '@/contexts/AuthContext';
import { AUTH_EVENTS, SUSPENSION_REASON } from '@/constants/auth';
import { API_BASE_URL, getApiHeaders } from '@/lib/core/api-utils';

export function TenantSuspendedPage() {
  const { membership, send } = useAuth();
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
          <h1 className="text-xl font-semibold">Workspace suspended</h1>
          <p className="text-muted-foreground text-sm">
            {isInactivity
              ? 'This workspace was automatically suspended due to 30 days of inactivity. All your data is safe.'
              : 'This workspace has been suspended. Please contact support for more information.'}
          </p>
        </div>

        {canReactivate && (
          <>
            <Button onClick={handleReactivate} disabled={reactivating}>
              <RefreshCw className={`mr-2 h-4 w-4 ${reactivating ? 'animate-spin' : ''}`} />
              {reactivating ? 'Reactivating...' : 'Reactivate workspace'}
            </Button>
            {error && <p className="text-destructive text-sm">{error}</p>}
          </>
        )}

        {!canReactivate && (
          <div className="space-y-2">
            <p className="text-muted-foreground text-xs">
              Ask a workspace owner or admin to reactivate this workspace, or contact support.
            </p>
            <Button variant="outline" size="sm" asChild>
              <a href="mailto:support@orkyo.com">
                <Mail className="mr-2 h-4 w-4" />
                Contact support
              </a>
            </Button>
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
