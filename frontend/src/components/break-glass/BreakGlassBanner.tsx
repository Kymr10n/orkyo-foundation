/**
 * Site-admin break-glass countdown banner.
 *
 * Renders only when the current membership was opened via break-glass
 * (`membership.isBreakGlass`). Shows a live countdown to expiry, lets the
 * admin extend the session up to the absolute hard cap, and lets them exit
 * cleanly back to /admin.
 *
 * The banner reconciles with the backend on mount via
 * `getBreakGlassSessionStatus` so the absolute hard cap and createdAt are
 * authoritative even when the page was reloaded mid-session.
 */
import { useCallback, useEffect, useState } from 'react';
import { Clock, RefreshCw, Shield, X } from 'lucide-react';

import { Button } from '@/components/ui/button';
import { useAuth } from '@/contexts/AuthContext';
import {
  type BreakGlassSessionStatus,
  auditBreakGlassExit,
  getBreakGlassSessionStatus,
  renewBreakGlassSession,
} from '@/lib/api/admin-api';
import { navigateToApex } from '@/lib/utils/tenant-navigation';
import { logger } from '@/lib/core/logger';

/**
 * Below this threshold we switch the banner to a destructive treatment to
 * give the admin a visible nudge to either extend or exit before things
 * fail under them.
 */
const URGENT_THRESHOLD_MS = 5 * 60 * 1000;

function formatRemaining(ms: number): string {
  if (ms <= 0) return '0:00';
  const totalSeconds = Math.floor(ms / 1000);
  const hours = Math.floor(totalSeconds / 3600);
  const minutes = Math.floor((totalSeconds % 3600) / 60);
  const seconds = totalSeconds % 60;
  if (hours > 0) {
    return `${hours}:${minutes.toString().padStart(2, '0')}:${seconds.toString().padStart(2, '0')}`;
  }
  return `${minutes}:${seconds.toString().padStart(2, '0')}`;
}

interface BannerProps {
  /** Override the now() source — for tests. */
  now?: () => number;
}

export function BreakGlassBanner({ now = Date.now }: BannerProps = {}) {
  const { membership, clearMembership } = useAuth();
  const [session, setSession] = useState<BreakGlassSessionStatus | null>(null);
  const [renewing, setRenewing] = useState(false);
  const [tick, setTick] = useState(now());

  const sessionId = membership?.breakGlassSessionId;
  const tenantSlug = membership?.slug;

  const handleExit = useCallback(() => {
    const id = sessionId;
    clearMembership();
    if (!navigateToApex('/admin')) {
      window.location.href = '/admin';
    }
    if (id) {
      auditBreakGlassExit(id).catch((err: unknown) => {
        logger.warn('Failed to audit break-glass exit:', err);
      });
    }
  }, [clearMembership, sessionId]);

  // Initial reconciliation with the backend so we have the authoritative hard cap.
  useEffect(() => {
    if (!membership?.isBreakGlass || !tenantSlug) return;
    let cancelled = false;
    getBreakGlassSessionStatus(tenantSlug).then((status) => {
      if (cancelled) return;
      if (status) {
        setSession(status);
      } else {
        // Server says no active session — the session expired or was revoked
        // externally (e.g. server restart, another admin). Exit gracefully.
        handleExit();
      }
    });
    return () => {
      cancelled = true;
    };
  }, [membership?.isBreakGlass, tenantSlug, handleExit]);

  // 1Hz tick for the countdown text.
  useEffect(() => {
    if (!membership?.isBreakGlass) return;
    const id = window.setInterval(() => setTick(now()), 1000);
    return () => window.clearInterval(id);
  }, [membership?.isBreakGlass, now]);

  const expiresAtMs = session?.expiresAt ? Date.parse(session.expiresAt) : null;
  const absoluteExpiresAtMs = session?.absoluteExpiresAt ? Date.parse(session.absoluteExpiresAt) : null;
  const remainingMs = expiresAtMs != null ? expiresAtMs - tick : null;
  const canExtend = absoluteExpiresAtMs != null ? tick < absoluteExpiresAtMs : true;
  const isUrgent = remainingMs != null && remainingMs <= URGENT_THRESHOLD_MS;

  const handleExtend = async () => {
    if (!sessionId || renewing) return;
    setRenewing(true);
    try {
      const renewed = await renewBreakGlassSession(sessionId);
      setSession(renewed);
    } catch (err) {
      // handleApiError already routes 410 / 404 cases. Anything else is a no-op.
      logger.warn('Break-glass renewal failed:', err);
    } finally {
      setRenewing(false);
    }
  };

  // Auto-exit when the local clock crosses ExpiresAt with no successful renewal.
  // Backend will already 403 the next API call; this just avoids a stale UI.
  useEffect(() => {
    if (remainingMs != null && remainingMs <= 0) {
      handleExit();
    }
  }, [remainingMs, handleExit]);

  const containerClass = `flex items-center gap-3 px-4 py-2 border-b text-sm ${
    isUrgent
      ? 'bg-destructive/10 text-destructive border-destructive/40'
      : 'bg-amber-500/10 text-amber-900 dark:text-amber-200 border-amber-500/40'
  }`;

  if (!membership?.isBreakGlass) return null;

  return (
    <div role="status" aria-live="polite" data-testid="break-glass-banner" className={containerClass}>
      <Shield className="h-4 w-4 flex-shrink-0" aria-hidden />
      <span className="font-medium">Break-glass session active</span>
      <span className="text-muted-foreground hidden sm:inline">·</span>
      <span className="hidden sm:flex items-center gap-1">
        <Clock className="h-3.5 w-3.5" aria-hidden />
        {remainingMs != null ? (
          <span data-testid="break-glass-remaining">
            {formatRemaining(remainingMs)} remaining
          </span>
        ) : (
          <span className="text-muted-foreground">Loading…</span>
        )}
      </span>

      <div className="ml-auto flex items-center gap-2">
        <Button
          type="button"
          variant="outline"
          size="sm"
          disabled={renewing || !canExtend || !sessionId}
          onClick={() => {
            void handleExtend();
          }}
          data-testid="break-glass-extend"
        >
          <RefreshCw className={`h-3.5 w-3.5 mr-1 ${renewing ? 'animate-spin' : ''}`} />
          {canExtend ? 'Extend' : 'Cap reached'}
        </Button>
        <Button
          type="button"
          variant="ghost"
          size="sm"
          onClick={handleExit}
          data-testid="break-glass-exit"
        >
          <X className="h-3.5 w-3.5 mr-1" />
          Exit tenant
        </Button>
      </div>
    </div>
  );
}
