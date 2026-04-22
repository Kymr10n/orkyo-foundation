import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';

// --------------------------------------------------------------------------
// Mock runtimeConfig — vi.hoisted ensures the variable exists when the
// hoisted vi.mock factory runs.
// --------------------------------------------------------------------------

const mockConfig = vi.hoisted(() => ({ baseDomain: '', subdomainPrefix: '' }));

vi.mock('@/config/runtime', () => ({
  runtimeConfig: mockConfig,
}));

import {
  getCurrentSubdomain,
  extractSlugFromHostname,
  getTenantHostname,
  getApexHostname,
  getApexOrigin,
  navigateToTenantSubdomain,
  navigateToApex,
  redirectToLogin,
  setBreakGlassCookie,
  consumeBreakGlassCookie,
} from './tenant-navigation';

// --------------------------------------------------------------------------
// Helpers
// --------------------------------------------------------------------------

/** Stub `window.location` with a given hostname (and optional protocol). */
function stubLocation(hostname: string, protocol = 'https:') {
  Object.defineProperty(window, 'location', {
    value: { hostname, protocol, href: '' },
    writable: true,
    configurable: true,
  });
}

// --------------------------------------------------------------------------
// Tests
// --------------------------------------------------------------------------

describe('tenant-navigation', () => {
  beforeEach(() => {
    mockConfig.baseDomain = '';
    mockConfig.subdomainPrefix = '';
    stubLocation('localhost');
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  // ── getCurrentSubdomain ──────────────────────────────────────────────

  describe('getCurrentSubdomain', () => {
    it('returns null when baseDomain is not configured', () => {
      mockConfig.baseDomain = '';
      expect(getCurrentSubdomain()).toBeNull();
    });

    it('returns null when on the apex domain', () => {
      mockConfig.baseDomain = 'orkyo.com';
      stubLocation('orkyo.com');
      expect(getCurrentSubdomain()).toBeNull();
    });

    it('returns the subdomain when on a tenant subdomain', () => {
      mockConfig.baseDomain = 'orkyo.com';
      stubLocation('demo.orkyo.com');
      expect(getCurrentSubdomain()).toBe('demo');
    });

    it('returns null for nested subdomains (e.g. a.b.orkyo.com)', () => {
      mockConfig.baseDomain = 'orkyo.com';
      stubLocation('a.b.orkyo.com');
      expect(getCurrentSubdomain()).toBeNull();
    });

    it('returns null when hostname does not end with baseDomain', () => {
      mockConfig.baseDomain = 'orkyo.com';
      stubLocation('evil-orkyo.com');
      expect(getCurrentSubdomain()).toBeNull();
    });

    it('returns null for an unrelated hostname', () => {
      mockConfig.baseDomain = 'orkyo.com';
      stubLocation('example.com');
      expect(getCurrentSubdomain()).toBeNull();
    });

    it('strips prefix and returns slug on staging subdomain', () => {
      mockConfig.baseDomain = 'orkyo.com';
      mockConfig.subdomainPrefix = 'staging-';
      stubLocation('staging-acme.orkyo.com');
      expect(getCurrentSubdomain()).toBe('acme');
    });

    it('returns null for staging apex (prefix-only subdomain)', () => {
      mockConfig.baseDomain = 'orkyo.com';
      mockConfig.subdomainPrefix = 'staging-';
      stubLocation('staging.orkyo.com');
      // "staging" doesn't start with "staging-", so no match
      expect(getCurrentSubdomain()).toBeNull();
    });
  });

  // ── navigateToTenantSubdomain ────────────────────────────────────────

  describe('navigateToTenantSubdomain', () => {
    it('returns false when baseDomain is not configured (local dev)', () => {
      mockConfig.baseDomain = '';
      stubLocation('localhost');
      expect(navigateToTenantSubdomain('demo')).toBe(false);
    });

    it('returns false when already on the target subdomain', () => {
      mockConfig.baseDomain = 'orkyo.com';
      stubLocation('demo.orkyo.com');
      expect(navigateToTenantSubdomain('demo')).toBe(false);
    });

    it('redirects to the tenant subdomain and returns true', () => {
      mockConfig.baseDomain = 'orkyo.com';
      stubLocation('orkyo.com');

      const result = navigateToTenantSubdomain('demo');

      expect(result).toBe(true);
      expect(window.location.href).toBe('https://demo.orkyo.com/');
    });

    it('uses the provided path in the redirect URL', () => {
      mockConfig.baseDomain = 'orkyo.com';
      stubLocation('orkyo.com');

      navigateToTenantSubdomain('demo', '/dashboard');

      expect(window.location.href).toBe('https://demo.orkyo.com/dashboard');
    });

    it('preserves the protocol', () => {
      mockConfig.baseDomain = 'orkyo.com';
      stubLocation('orkyo.com', 'http:');

      navigateToTenantSubdomain('demo');

      expect(window.location.href).toBe('http://demo.orkyo.com/');
    });

    it('supports path with query string (e.g. /login?auto=1)', () => {
      mockConfig.baseDomain = 'orkyo.com';
      stubLocation('orkyo.com');

      navigateToTenantSubdomain('demo', '/login?auto=1');

      expect(window.location.href).toBe('https://demo.orkyo.com/login?auto=1');
    });

    it('redirects cross-tenant (from one subdomain to another)', () => {
      mockConfig.baseDomain = 'orkyo.com';
      stubLocation('alpha.orkyo.com');

      const result = navigateToTenantSubdomain('beta');

      expect(result).toBe(true);
      expect(window.location.href).toBe('https://beta.orkyo.com/');
    });

    it('includes prefix on staging', () => {
      mockConfig.baseDomain = 'orkyo.com';
      mockConfig.subdomainPrefix = 'staging-';
      stubLocation('staging.orkyo.com');

      const result = navigateToTenantSubdomain('demo');

      expect(result).toBe(true);
      expect(window.location.href).toBe('https://staging-demo.orkyo.com/');
    });

    it('returns false when already on the target staging subdomain', () => {
      mockConfig.baseDomain = 'orkyo.com';
      mockConfig.subdomainPrefix = 'staging-';
      stubLocation('staging-demo.orkyo.com');

      expect(navigateToTenantSubdomain('demo')).toBe(false);
    });
  });

  // ── navigateToApex ───────────────────────────────────────────────────

  describe('navigateToApex', () => {
    it('returns false when baseDomain is not configured', () => {
      mockConfig.baseDomain = '';
      expect(navigateToApex()).toBe(false);
    });

    it('returns false when already on the apex domain', () => {
      mockConfig.baseDomain = 'orkyo.com';
      stubLocation('orkyo.com');
      expect(navigateToApex()).toBe(false);
    });

    it('redirects to the apex domain and returns true', () => {
      mockConfig.baseDomain = 'orkyo.com';
      stubLocation('demo.orkyo.com');

      const result = navigateToApex();

      expect(result).toBe(true);
      expect(window.location.href).toBe('https://orkyo.com/');
    });

    it('uses the provided path', () => {
      mockConfig.baseDomain = 'orkyo.com';
      stubLocation('demo.orkyo.com');

      navigateToApex('/admin');

      expect(window.location.href).toBe('https://orkyo.com/admin');
    });

    it('redirects to staging apex when subdomainPrefix is set', () => {
      mockConfig.baseDomain = 'orkyo.com';
      mockConfig.subdomainPrefix = 'staging-';
      stubLocation('staging-demo.orkyo.com');

      const result = navigateToApex('/admin');

      expect(result).toBe(true);
      expect(window.location.href).toBe('https://staging.orkyo.com/admin');
    });

    it('returns false when already on the staging apex', () => {
      mockConfig.baseDomain = 'orkyo.com';
      mockConfig.subdomainPrefix = 'staging-';
      stubLocation('staging.orkyo.com');

      expect(navigateToApex('/admin')).toBe(false);
    });
  });

  // ── redirectToLogin ──────────────────────────────────────────────────

  describe('redirectToLogin', () => {
    it('redirects to apex /login when baseDomain is configured', () => {
      mockConfig.baseDomain = 'orkyo.com';
      stubLocation('demo.orkyo.com');

      redirectToLogin();

      expect(window.location.href).toBe('https://orkyo.com/login');
    });

    it('redirects to / when baseDomain is not configured (local dev)', () => {
      mockConfig.baseDomain = '';
      stubLocation('localhost');

      redirectToLogin();

      expect(window.location.href).toBe('/');
    });
  });

  // ── getApexHostname ──────────────────────────────────────────────────

  describe('getApexHostname', () => {
    it('returns null when baseDomain is not configured', () => {
      mockConfig.baseDomain = '';
      expect(getApexHostname()).toBeNull();
    });

    it('returns baseDomain when no prefix is set', () => {
      mockConfig.baseDomain = 'orkyo.com';
      expect(getApexHostname()).toBe('orkyo.com');
    });

    it('returns staging apex when subdomainPrefix is set', () => {
      mockConfig.baseDomain = 'orkyo.com';
      mockConfig.subdomainPrefix = 'staging-';
      expect(getApexHostname()).toBe('staging.orkyo.com');
    });
  });

  // ── getTenantHostname ────────────────────────────────────────────────

  describe('getTenantHostname', () => {
    it('returns null when baseDomain is not configured', () => {
      mockConfig.baseDomain = '';
      expect(getTenantHostname('demo')).toBeNull();
    });

    it('returns slug.baseDomain in production', () => {
      mockConfig.baseDomain = 'orkyo.com';
      expect(getTenantHostname('demo')).toBe('demo.orkyo.com');
    });

    it('returns prefix+slug.baseDomain on staging', () => {
      mockConfig.baseDomain = 'orkyo.com';
      mockConfig.subdomainPrefix = 'staging-';
      expect(getTenantHostname('demo')).toBe('staging-demo.orkyo.com');
    });
  });

  // ── extractSlugFromHostname ──────────────────────────────────────────

  describe('extractSlugFromHostname', () => {
    it('returns null when baseDomain is not configured', () => {
      mockConfig.baseDomain = '';
      expect(extractSlugFromHostname('demo.orkyo.com')).toBeNull();
    });

    it('returns the slug from a production hostname', () => {
      mockConfig.baseDomain = 'orkyo.com';
      expect(extractSlugFromHostname('demo.orkyo.com')).toBe('demo');
    });

    it('strips prefix on staging', () => {
      mockConfig.baseDomain = 'orkyo.com';
      mockConfig.subdomainPrefix = 'staging-';
      expect(extractSlugFromHostname('staging-demo.orkyo.com')).toBe('demo');
    });

    it('returns null for the apex hostname', () => {
      mockConfig.baseDomain = 'orkyo.com';
      expect(extractSlugFromHostname('orkyo.com')).toBeNull();
    });

    it('returns null for nested subdomains', () => {
      mockConfig.baseDomain = 'orkyo.com';
      expect(extractSlugFromHostname('a.b.orkyo.com')).toBeNull();
    });

    it('returns null when prefix does not match', () => {
      mockConfig.baseDomain = 'orkyo.com';
      mockConfig.subdomainPrefix = 'staging-';
      expect(extractSlugFromHostname('demo.orkyo.com')).toBeNull();
    });
  });

  // ── getApexOrigin ────────────────────────────────────────────────────

  describe('getApexOrigin', () => {
    it('returns current origin when baseDomain is not configured', () => {
      mockConfig.baseDomain = '';
      stubLocation('localhost', 'http:');
      // stubLocation sets hostname but window.location.origin isn't set,
      // so we verify it falls back gracefully
      expect(getApexOrigin()).toBe(window.location.origin);
    });

    it('returns production apex origin', () => {
      mockConfig.baseDomain = 'orkyo.com';
      stubLocation('demo.orkyo.com');
      expect(getApexOrigin()).toBe('https://orkyo.com');
    });

    it('returns staging apex origin', () => {
      mockConfig.baseDomain = 'orkyo.com';
      mockConfig.subdomainPrefix = 'staging-';
      stubLocation('staging-demo.orkyo.com');
      expect(getApexOrigin()).toBe('https://staging.orkyo.com');
    });
  });

  // ── Break-glass cookie ───────────────────────────────────────────────

  describe('setBreakGlassCookie', () => {
    it('does nothing when baseDomain is not configured', () => {
      mockConfig.baseDomain = '';
      const spy = vi.spyOn(document, 'cookie', 'set');

      setBreakGlassCookie('sess-123', 'tenant-uuid-abc');

      expect(spy).not.toHaveBeenCalled();
    });

    it('sets a cookie with sessionId and tenantId separated by pipe', () => {
      mockConfig.baseDomain = 'orkyo.com';
      const spy = vi.spyOn(document, 'cookie', 'set');

      setBreakGlassCookie('sess-123', 'tenant-uuid-abc');

      expect(spy).toHaveBeenCalledOnce();
      const cookieStr = spy.mock.calls[0][0];
      expect(cookieStr).toContain('orkyo-break-glass=sess-123|tenant-uuid-abc');
      expect(cookieStr).toContain('domain=.orkyo.com');
      expect(cookieStr).toContain('secure');
      expect(cookieStr).toContain('samesite=lax');
    });
  });

  describe('consumeBreakGlassCookie', () => {
    it('returns null when cookie is not present', () => {
      vi.spyOn(document, 'cookie', 'get').mockReturnValue('');
      expect(consumeBreakGlassCookie()).toBeNull();
    });

    it('returns sessionId and tenantId and clears the cookie', () => {
      mockConfig.baseDomain = 'orkyo.com';

      const getCookieSpy = vi.spyOn(document, 'cookie', 'get').mockReturnValue(
        'other=val; orkyo-break-glass=sess-abc|tenant-uuid-xyz; another=x'
      );
      const setCookieSpy = vi.spyOn(document, 'cookie', 'set');

      const result = consumeBreakGlassCookie();

      expect(result).toEqual({ sessionId: 'sess-abc', tenantId: 'tenant-uuid-xyz' });
      expect(setCookieSpy).toHaveBeenCalledOnce();
      const clearStr = setCookieSpy.mock.calls[0][0];
      expect(clearStr).toContain('orkyo-break-glass=');
      expect(clearStr).toContain('expires=Thu, 01 Jan 1970');

      getCookieSpy.mockRestore();
      setCookieSpy.mockRestore();
    });

    it('returns empty tenantId when cookie has only sessionId (legacy format)', () => {
      mockConfig.baseDomain = 'orkyo.com';

      const getCookieSpy = vi.spyOn(document, 'cookie', 'get').mockReturnValue(
        'orkyo-break-glass=sess-abc'
      );
      vi.spyOn(document, 'cookie', 'set');

      const result = consumeBreakGlassCookie();

      expect(result).toEqual({ sessionId: 'sess-abc', tenantId: '' });

      getCookieSpy.mockRestore();
    });

    it('returns null when baseDomain is set but cookie is absent', () => {
      mockConfig.baseDomain = 'orkyo.com';
      vi.spyOn(document, 'cookie', 'get').mockReturnValue('some=other');
      expect(consumeBreakGlassCookie()).toBeNull();
    });
  });
});
