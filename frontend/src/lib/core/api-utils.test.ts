/* eslint-disable @typescript-eslint/no-explicit-any */
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import {
    API_BASE_URL,
    getApiHeaders,
    getApiUrl,
    getTenantSlug,
    handleApiError,
} from './api-utils';
import { runtimeConfig } from '../../config/runtime';
import * as AuthContext from '../../contexts/AuthContext';

vi.mock('@foundation/src/contexts/AuthContext', () => ({
  getAuthTokenSync: vi.fn(),
  getTenantSlugSync: vi.fn(),
}));

const { mockRedirectToLogin, mockNavigateToApex } = vi.hoisted(() => ({
  mockRedirectToLogin: vi.fn(),
  mockNavigateToApex: vi.fn(() => true),
}));

vi.mock(import('@foundation/src/lib/utils/tenant-navigation'), async (importOriginal) => {
  const actual = await importOriginal();
  return {
    ...actual,
    redirectToLogin: () => mockRedirectToLogin(),
    navigateToApex: mockNavigateToApex,
  };
});

vi.mock('@foundation/src/lib/core/csrf', () => ({
  getCsrfToken: vi.fn(() => null),
  CSRF_HEADER_NAME: 'X-CSRF-Token',
  isMutatingMethod: (m: string) => ['POST','PUT','PATCH','DELETE'].includes(m.toUpperCase()),
}));

describe('api-utils', () => {
  const originalLocation = window.location;

  beforeEach(() => {
    vi.clearAllMocks();
  });

  afterEach(() => {
    Object.defineProperty(window, 'location', {
      writable: true,
      value: originalLocation,
    });
  });

  describe('getApiHeaders', () => {
    it('includes Content-Type and tenant slug', () => {
      vi.mocked(AuthContext.getTenantSlugSync).mockReturnValue('demo');

      const headers = getApiHeaders();

      expect(headers['Content-Type']).toBe('application/json');
      expect(headers['X-Tenant-Slug']).toBe('demo');
    });

    it('includes tenant slug when available', () => {
      vi.mocked(AuthContext.getTenantSlugSync).mockReturnValue('acme');

      const headers = getApiHeaders();

      expect(headers['X-Tenant-Slug']).toBe('acme');
    });

    it('includes X-Correlation-ID as a valid UUID', () => {
      vi.mocked(AuthContext.getTenantSlugSync).mockReturnValue('demo');

      const headers = getApiHeaders();

      expect(headers['X-Correlation-ID']).toBeDefined();
      // crypto.randomUUID() produces a standard UUID v4 format
      expect(headers['X-Correlation-ID']).toMatch(
        /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i
      );
    });

    it('generates unique correlation IDs per call', () => {
      vi.mocked(AuthContext.getTenantSlugSync).mockReturnValue('demo');

      const id1 = getApiHeaders()['X-Correlation-ID'];
      const id2 = getApiHeaders()['X-Correlation-ID'];

      expect(id1).not.toBe(id2);
    });
  });

  describe('getTenantSlug', () => {
    const originalBaseDomain = runtimeConfig.baseDomain;

    afterEach(() => {
      // Restore original baseDomain after each test
      (runtimeConfig as any).baseDomain = originalBaseDomain;
    });

    it('extracts tenant from subdomain when baseDomain is set', () => {
      vi.mocked(AuthContext.getTenantSlugSync).mockReturnValue('default');
      delete (window as any).location;
      (window as any).location = { hostname: 'acme.orkyo.app' };
      // Set baseDomain via runtimeConfig for testing
      (runtimeConfig as any).baseDomain = 'orkyo.app';

      const slug = getTenantSlug();

      expect(slug).toBe('acme');
    });

    it('uses auth store for localhost', () => {
      vi.mocked(AuthContext.getTenantSlugSync).mockReturnValue('demo');
      delete (window as any).location;
      (window as any).location = { hostname: 'localhost' };
      (runtimeConfig as any).baseDomain = '';

      const slug = getTenantSlug();

      expect(slug).toBe('demo');
    });

    it('uses auth store when baseDomain is not set', () => {
      vi.mocked(AuthContext.getTenantSlugSync).mockReturnValue('demo');
      delete (window as any).location;
      (window as any).location = { hostname: 'orkyo.endpoint.servebeer.com' };
      (runtimeConfig as any).baseDomain = '';

      const slug = getTenantSlug();

      // Without baseDomain, uses auth store
      expect(slug).toBe('demo');
    });

    it('falls back to active_membership slug when tenant_slug is missing', () => {
      vi.mocked(AuthContext.getTenantSlugSync).mockReturnValue(null);
      delete (window as any).location;
      (window as any).location = { hostname: 'localhost' };
      (runtimeConfig as any).baseDomain = '';
      localStorage.setItem('active_membership', JSON.stringify({ slug: 'demo' }));

      const slug = getTenantSlug();

      expect(slug).toBe('demo');
    });
  });

  describe('getApiUrl', () => {
    it('returns the configured API base URL as a string', () => {
      const url = getApiUrl();
      expect(typeof url).toBe('string');
      // apiBaseUrl may be empty (same-origin / subdomain mode) or a full URL
      expect(url).toBe(runtimeConfig.apiBaseUrl);
    });

    it('returns same value as API_BASE_URL', () => {
      expect(getApiUrl()).toBe(API_BASE_URL);
    });

    it('API_BASE_URL matches runtimeConfig.apiBaseUrl', () => {
      // Guards against drift — the re-export must always track the config value
      expect(API_BASE_URL).toBe(runtimeConfig.apiBaseUrl);
    });
  });

  describe('handleApiError', () => {
    it('throws error with status and message', async () => {
      const response = {
        status: 500,
        statusText: 'Internal Server Error',
        json: async () => ({ detail: 'Database connection failed' }),
      } as Response;

      await expect(handleApiError(response)).rejects.toThrow(
        'API Error (500): Database connection failed'
      );
    });

    it('uses statusText when JSON parsing fails', async () => {
      const response = {
        status: 404,
        statusText: 'Not Found',
        json: async () => {
          throw new Error('Not JSON');
        },
      } as unknown as Response;

      await expect(handleApiError(response)).rejects.toThrow(
        'API Error (404): Not Found'
      );
    });

    it('handles 401 token error by clearing app state and redirecting', async () => {
      localStorage.setItem('active_membership', '{"tenantId":"test"}');
      localStorage.setItem('tenant_slug', 'test');
      localStorage.setItem('oidc.user:test', '{"access_token":"token"}');

      const response = {
        status: 401,
        statusText: 'Unauthorized',
        headers: new Headers(),
        json: async () => ({ detail: 'Token expired' }),
      } as unknown as Response;

      await expect(handleApiError(response)).rejects.toThrow(
        'Token expired'
      );

      // App keys cleared
      expect(localStorage.getItem('active_membership')).toBeNull();
      expect(localStorage.getItem('tenant_slug')).toBeNull();
      // oidc.* keys left intact — UserManager owns those
      expect(localStorage.getItem('oidc.user:test')).toBe('{"access_token":"token"}');
      // Redirect via centralized redirectToLogin()
      expect(mockRedirectToLogin).toHaveBeenCalled();
    });

    it('sends an ephemeral (demo) session back to the marketing site, not to login', async () => {
      // A demo visitor never had credentials, so ending their session at a Keycloak password
      // form is a dead end. AuthContext marks the session on bootstrap; this is the payoff.
      sessionStorage.setItem('orkyo:session-end-redirect', 'https://orkyo.com/');
      const replace = vi.fn();
      // A minimal stand-in rather than a spread of the real Location: spreading a class
      // instance drops its prototype (and eslint rightly flags it). The 401 branch only
      // needs replace() and the hostname clearTenantState reads.
      Object.defineProperty(window, 'location', {
        writable: true,
        value: { hostname: 'acme.orkyo.com', href: 'https://acme.orkyo.com/app', replace },
      });

      const response = {
        status: 401,
        statusText: 'Unauthorized',
        headers: new Headers(),
        json: async () => ({ detail: 'Token expired' }),
      } as unknown as Response;

      await expect(handleApiError(response)).rejects.toThrow('Token expired');

      expect(replace).toHaveBeenCalledWith('https://orkyo.com/');
      expect(mockRedirectToLogin).not.toHaveBeenCalled();
      // Single-use: a subsequent real login must still end at the login flow.
      expect(sessionStorage.getItem('orkyo:session-end-redirect')).toBeNull();
    });

    it('handles 401 API key error by clearing session', async () => {
      localStorage.setItem('active_membership', '{"tenantId":"test"}');
      localStorage.setItem('tenant_slug', 'test');
      localStorage.setItem('oidc.user:test', '{"access_token":"token"}');

      const response = {
        status: 401,
        statusText: 'Unauthorized',
        headers: new Headers(),
        json: async () => ({ detail: 'API key is required' }),
      } as unknown as Response;

      await expect(handleApiError(response)).rejects.toThrow(
        'API key is required'
      );

      // Session state cleared
      expect(localStorage.getItem('active_membership')).toBeNull();
      expect(localStorage.getItem('tenant_slug')).toBeNull();
      // oidc.* keys left intact — UserManager owns those
      expect(localStorage.getItem('oidc.user:test')).toBe('{"access_token":"token"}');
      expect(mockRedirectToLogin).toHaveBeenCalled();
    });

    it('handles 401 invalid API key by clearing session', async () => {
      localStorage.setItem('active_membership', '{"tenantId":"test"}');

      const response = {
        status: 401,
        statusText: 'Unauthorized',
        headers: new Headers(),
        json: async () => ({ detail: 'Invalid API key' }),
      } as unknown as Response;

      await expect(handleApiError(response)).rejects.toThrow(
        'Invalid API key'
      );

      expect(localStorage.getItem('active_membership')).toBeNull();
    });

    it('prefers RFC 7807 detail over the generic title', async () => {
      const response = {
        status: 400,
        statusText: 'Bad Request',
        json: async () => ({ title: 'Bad Request', detail: 'Validation failed' }),
      } as Response;

      await expect(handleApiError(response)).rejects.toThrow(
        'API Error (400): Validation failed'
      );
    });

    it('falls back to title when the problem carries no detail', async () => {
      const response = {
        status: 409,
        statusText: 'Conflict',
        json: async () => ({ title: 'Conflict', code: 'conflict' }),
      } as Response;

      await expect(handleApiError(response)).rejects.toThrow('API Error (409): Conflict');
    });

    it('surfaces field-level validation messages instead of the generic detail', async () => {
      // The backend's validation problem carries a generic detail plus per-field messages;
      // showing the user "One or more fields failed validation." tells them nothing.
      const response = {
        status: 400,
        statusText: 'Bad Request',
        json: async () => ({
          title: 'Bad Request',
          detail: 'One or more fields failed validation.',
          code: 'validation_error',
          errors: { Name: ['Name must not be empty.'], EndUtc: ['EndUtc must be after StartUtc'] },
        }),
      } as Response;

      await expect(handleApiError(response)).rejects.toThrow(
        'API Error (400): Name must not be empty. EndUtc must be after StartUtc'
      );
    });

    it('handles break_glass_expired by clearing state and navigating to /site-admin', async () => {
      localStorage.setItem('active_membership', '{"tenantId":"test"}');
      localStorage.setItem('tenant_slug', 'test');

      const response = {
        status: 403,
        statusText: 'Forbidden',
        json: async () => ({
          detail: 'Break-glass session ended',
          code: 'break_glass_expired',
          returnTo: '/site-admin',
        }),
      } as unknown as Response;

      await expect(handleApiError(response)).rejects.toThrow(
        'Break-glass session ended'
      );

      expect(localStorage.getItem('active_membership')).toBeNull();
      expect(localStorage.getItem('tenant_slug')).toBeNull();
      expect(mockNavigateToApex).toHaveBeenCalledWith('/site-admin');
      expect(mockRedirectToLogin).not.toHaveBeenCalled();
    });

    it('handles break_glass_hard_cap_reached by navigating to /site-admin', async () => {
      localStorage.setItem('active_membership', '{"tenantId":"test"}');

      const response = {
        status: 410,
        statusText: 'Gone',
        json: async () => ({
          detail: 'Hard cap reached',
          code: 'break_glass_hard_cap_reached',
          returnTo: '/site-admin',
        }),
      } as unknown as Response;

      await expect(handleApiError(response)).rejects.toThrow('Hard cap reached');

      expect(localStorage.getItem('active_membership')).toBeNull();
      expect(mockNavigateToApex).toHaveBeenCalledWith('/site-admin');
      expect(mockRedirectToLogin).not.toHaveBeenCalled();
    });

    it('falls back to /site-admin when break-glass response has no returnTo', async () => {
      const response = {
        status: 404,
        statusText: 'Not Found',
        json: async () => ({
          detail: 'Session expired',
          code: 'break_glass_expired',
        }),
      } as unknown as Response;

      await expect(handleApiError(response)).rejects.toThrow();
      expect(mockNavigateToApex).toHaveBeenCalledWith('/site-admin');
    });
  });
});
