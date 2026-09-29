import { useMutation, useQuery } from "@tanstack/react-query";
import {
  auditBreakGlassExit,
  getBreakGlassSessionStatus,
  renewBreakGlassSession,
} from "@foundation/src/lib/api/admin-api";
import { qk } from "@foundation/src/lib/api/query-keys";
import { logger } from "@foundation/src/lib/core/logger";

/**
 * The server's view of the caller's break-glass session, read once when the banner mounts so the
 * hard cap is authoritative after a reload. `null` data means no active session. Any other
 * failure says nothing about the session, so it is logged and not retried.
 */
export const useBreakGlassSessionStatus = (tenantSlug: string | null) =>
  useQuery({
    queryKey: qk.admin.breakGlassSession(tenantSlug ?? "none"),
    queryFn: () =>
      getBreakGlassSessionStatus(tenantSlug!).catch((err: unknown) => {
        logger.warn("Failed to read break-glass session status:", err);
        throw err;
      }),
    enabled: tenantSlug !== null,
    staleTime: Infinity,
    retry: false,
    refetchOnWindowFocus: false,
  });

/** Extend the session. `handleApiError` already routes 410 / 404; anything else is logged. */
export const useRenewBreakGlassSession = () =>
  useMutation({
    mutationFn: (sessionId: string) => renewBreakGlassSession(sessionId),
    onError: (err) => logger.warn("Break-glass renewal failed:", err),
  });

/** Record the exit. Fire-and-forget: the browser is already leaving the tenant. */
export const useAuditBreakGlassExit = () =>
  useMutation({
    mutationFn: (sessionId: string) => auditBreakGlassExit(sessionId),
    onError: (err) => logger.warn("Failed to audit break-glass exit:", err),
  });
