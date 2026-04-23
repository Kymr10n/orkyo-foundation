/**
 * RequireAuth - Route guard that requires authentication
 *
 * Props:
 *   requireMembership (default true) – when true the user must also
 *     have an active tenant membership (authStage === 'ready').
 *     Set to false for routes that only need a valid BFF session
 *     (e.g. /account, /admin).
 *
 * Uses `authStage` as the single source of truth — no independent
 * re-derivation of auth state.
 */

import { useAuth, debugAuth } from "@foundation/src/contexts/AuthContext";
import { useEffect, useState } from "react";
import { logger } from "@foundation/src/lib/core/logger";
import { AUTH_STAGES, AUTH_EVENTS, AUTH_MESSAGES, LOADING_STAGES, UNAUTHENTICATED_STAGES } from "@foundation/src/constants/auth";
import { LoadingSpinner } from "@foundation/src/components/ui/LoadingSpinner";
import { AuthErrorScreen } from "@foundation/src/components/ui/AuthErrorScreen";

/** Delay before showing a spinner so fast auth checks don't flash. */
const SPINNER_DELAY_MS = 200;

interface RequireAuthProps {
  children: React.ReactNode;
  /** When false only a valid BFF session is required (no tenant membership). */
  requireMembership?: boolean;
}

export function RequireAuth({
  children,
  requireMembership = true,
}: RequireAuthProps) {
  const { authStage, error, send } = useAuth();

  const isLoading = LOADING_STAGES.has(authStage);
  const isError = authStage === AUTH_STAGES.ERROR_BACKEND || authStage === AUTH_STAGES.ERROR_NETWORK;
  const isAuthorised = requireMembership
    ? authStage === AUTH_STAGES.READY
    : !UNAUTHENTICATED_STAGES.has(authStage);

  // Delay the spinner so fast BFF cookie checks don't produce a flash.
  const [showSpinner, setShowSpinner] = useState(false);
  useEffect(() => {
    if (!isLoading) {
      setShowSpinner(false);
      return;
    }
    const id = setTimeout(() => setShowSpinner(true), SPINNER_DELAY_MS);
    return () => clearTimeout(id);
  }, [isLoading]);

  useEffect(() => {
    debugAuth(`RequireAuth check (requireMembership=${requireMembership}, authStage=${authStage})`);
  }, [requireMembership, authStage]);

  useEffect(() => {
    if (isLoading || isError) return;

    if (!isAuthorised) {
      logger.debug('[RequireAuth] Not authorised, signalling machine');
      send({ type: AUTH_EVENTS.UNAUTHORIZED });
    }
  }, [isLoading, isError, isAuthorised, send]);

  if (isError) {
    return (
      <AuthErrorScreen
        variant={authStage === AUTH_STAGES.ERROR_NETWORK ? 'network' : 'backend'}
        title={authStage === AUTH_STAGES.ERROR_NETWORK ? AUTH_MESSAGES.NETWORK_ERROR_TITLE : AUTH_MESSAGES.BACKEND_ERROR_TITLE}
        detail={error ?? (authStage === AUTH_STAGES.ERROR_NETWORK ? AUTH_MESSAGES.NETWORK_ERROR_DETAIL : AUTH_MESSAGES.BACKEND_ERROR_DETAIL)}
        onRetry={() => send({ type: AUTH_EVENTS.RETRY })}
      />
    );
  }

  if (isLoading) {
    if (!showSpinner) {
      return <div className="min-h-screen bg-background" />;
    }
    return <LoadingSpinner message={AUTH_MESSAGES.LOADING} />;
  }

  if (!isAuthorised) {
    return <LoadingSpinner message={AUTH_MESSAGES.REDIRECTING_LOGIN} />;
  }

  return <>{children}</>;
}
