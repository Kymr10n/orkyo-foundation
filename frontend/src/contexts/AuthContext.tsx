/**
 * AuthContext — thin React wrapper around authMachine.
 *
 * All auth logic lives in src/machines/authMachine.ts.
 * This file only:
 *   1. Runs the machine with useMachine()
 *   2. Exposes state + typed action functions via context
 *   3. Re-exports types consumed by the rest of the app
 *
 * To trigger auth transitions, send events via the actions below
 * or call `send` directly from useAuth().
 */

import type React from 'react';
import { createContext, useContext, useMemo } from 'react';
import { useMachine } from '@xstate/react';
import { authMachine } from '@foundation/src/machines/authMachine';
import type { AuthMachineEvent } from '@foundation/src/machines/authMachine';
import { STORAGE_KEYS } from '@foundation/src/constants/storage';
import {
  AUTH_STAGES,
  AUTH_EVENTS,
  LOADING_STAGES,
  UNAUTHENTICATED_STAGES,
} from '@foundation/src/constants/auth';
import type { AuthStage } from '@foundation/src/constants/auth';
import { logger } from '@foundation/src/lib/core/logger';

// ── Re-exported types (consumed by pages, components, api-utils) ──────────────

export type ServiceTier = 'Free' | 'Professional' | 'Enterprise';

export interface TenantMembership {
  tenantId: string;
  slug: string;
  displayName: string;
  role: string;
  state: string;
  tier?: ServiceTier;
  isTenantAdmin?: boolean;
  isBreakGlass?: boolean;
  breakGlassSessionId?: string;
  isOwner?: boolean;
  suspensionReason?: string;
  suspendedAt?: string;
  canReactivate?: boolean;
}

export interface AppUser {
  id: string;
  email: string;
  displayName: string;
  keycloakId?: string;
  hasSeenTour: boolean;
}

export interface SessionBootstrapResponse {
  user: AppUser;
  tosRequired: boolean;
  requiredTosVersion?: string;
  tenants: TenantMembership[];
  suggestedTenantSlug?: string;
  isSiteAdmin?: boolean;
}

// ── Context shape ─────────────────────────────────────────────────────────────

interface AuthContextValue {
  // State
  authStage: AuthStage;
  appUser: AppUser | null;
  membership: TenantMembership | null;
  sessionData: SessionBootstrapResponse | null;
  isSiteAdmin: boolean;
  error: string | null;

  // Derived
  isAuthenticated: boolean;
  isLoading: boolean;
  tenantSlug: string | null;
  /** True when the user may access /account directly (any authenticated stage). */
  canAccessAccountPage: boolean;
  /** True when the user may access /admin directly (site admin + any authenticated stage). */
  canAccessAdminPage: boolean;

  // Actions — wrap send() for backward compatibility
  login: (returnTo?: string) => void;
  logout: () => void;
  refresh: () => void;
  setMembership: (membership: TenantMembership) => void;
  clearMembership: () => void;
  /** Switch back to the tenant selector. In production, navigate to apex first. */
  switchTenant: () => void;
  setAppUser: (user: AppUser) => void;

  // Raw send for advanced use (e.g. UNAUTHORIZED from API error handlers)
  send: (event: AuthMachineEvent) => void;

  /** @deprecated BFF mode — tokens are server-side. Always null. */
  oidcUser: null;
  /** @deprecated BFF mode — tokens are server-side. Always null. */
  getAccessToken: () => string | null;
}

// ── Debug helper ──────────────────────────────────────────────────────────────

 
export function debugAuth(context: string) {
  if (import.meta.env.PROD) return;
  const params = new URLSearchParams(window.location.search);
  logger.debug(`[Auth Debug: ${context}]`, {
    href: window.location.href,
    pathname: window.location.pathname,
    hasError: params.has('error'),
  });
}

// ── Context ───────────────────────────────────────────────────────────────────

const AuthContext = createContext<AuthContextValue | null>(null);

// ── Provider ──────────────────────────────────────────────────────────────────

export function AuthProvider({ children }: { children: React.ReactNode }) {
  const [state, send] = useMachine(authMachine);

  const ctx = state.context;
  const authStage = state.value as AuthStage;

  const value = useMemo<AuthContextValue>(() => ({
    // State from machine context
    authStage,
    appUser: ctx.appUser,
    membership: ctx.membership,
    sessionData: ctx.sessionData,
    isSiteAdmin: ctx.isSiteAdmin,
    error: ctx.error,

    // Derived
    isAuthenticated: authStage === AUTH_STAGES.READY,
    isLoading: LOADING_STAGES.has(authStage),
    tenantSlug: ctx.membership?.slug ?? null,
    canAccessAccountPage: !UNAUTHENTICATED_STAGES.has(authStage),
    canAccessAdminPage: ctx.isSiteAdmin && !UNAUTHENTICATED_STAGES.has(authStage),

    // Actions
    login:          (returnTo) => send({ type: AUTH_EVENTS.LOGIN, returnTo }),
    logout:         () => send({ type: AUTH_EVENTS.LOGOUT }),
    refresh:        () => send({ type: AUTH_EVENTS.REFRESH }),
    setMembership:  (membership) => send({ type: AUTH_EVENTS.MEMBERSHIP_SET, membership }),
    clearMembership: () => send({ type: AUTH_EVENTS.MEMBERSHIP_CLEARED }),
    switchTenant:   () => send({ type: AUTH_EVENTS.SWITCH_TENANT }),
    setAppUser:     (user) => send({ type: AUTH_EVENTS.USER_UPDATED, user }),

    // Raw send
    send,

    // Deprecated stubs
    oidcUser: null,
    getAccessToken: () => null,
  }), [authStage, ctx, send]);

  return (
    <AuthContext.Provider value={value}>
      {children}
    </AuthContext.Provider>
  );
}

// ── Hook ──────────────────────────────────────────────────────────────────────

 
export function useAuth(): AuthContextValue {
  const context = useContext(AuthContext);
  if (!context) {
    throw new Error('useAuth must be used within an AuthProvider');
  }
  return context;
}

// ── Sync utilities for non-hook contexts ─────────────────────────────────────

 
export function getTenantSlugSync(): string | null {
  return localStorage.getItem(STORAGE_KEYS.TENANT_SLUG);
}
