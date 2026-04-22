import { describe, it, expect, beforeEach } from 'vitest';
import { getCsrfToken, isMutatingMethod, CSRF_HEADER_NAME } from './csrf';

describe('csrf', () => {
  beforeEach(() => {
    // Clear cookies before each test
    document.cookie = 'orkyo-csrf=; expires=Thu, 01 Jan 1970 00:00:00 GMT; path=/';
  });

  describe('CSRF_HEADER_NAME', () => {
    it('equals X-CSRF-Token', () => {
      expect(CSRF_HEADER_NAME).toBe('X-CSRF-Token');
    });
  });

  describe('getCsrfToken', () => {
    it('returns null when no cookie is set', () => {
      expect(getCsrfToken()).toBeNull();
    });

    it('returns the token value when cookie is set', () => {
      document.cookie = 'orkyo-csrf=abc123; path=/';
      expect(getCsrfToken()).toBe('abc123');
    });

    it('returns the token when other cookies are present', () => {
      document.cookie = 'other=value; path=/';
      document.cookie = 'orkyo-csrf=my-token; path=/';
      document.cookie = 'another=thing; path=/';
      expect(getCsrfToken()).toBe('my-token');
    });

    it('handles hex token values (real CSRF tokens)', () => {
      const hexToken = 'a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2';
      document.cookie = `orkyo-csrf=${hexToken}; path=/`;
      expect(getCsrfToken()).toBe(hexToken);
    });

    it('does not match a cookie with a similar prefix', () => {
      document.cookie = 'orkyo-csrf-old=stale; path=/';
      // orkyo-csrf is not set, so should return null
      // (the regex matches 'orkyo-csrf=' exactly before capturing)
      expect(getCsrfToken()).toBeNull();
    });
  });

  describe('isMutatingMethod', () => {
    it.each(['POST', 'PUT', 'PATCH', 'DELETE'])(
      'returns true for %s',
      (method) => {
        expect(isMutatingMethod(method)).toBe(true);
      }
    );

    it.each(['GET', 'HEAD', 'OPTIONS'])(
      'returns false for %s',
      (method) => {
        expect(isMutatingMethod(method)).toBe(false);
      }
    );

    it('handles lowercase input', () => {
      expect(isMutatingMethod('post')).toBe(true);
      expect(isMutatingMethod('get')).toBe(false);
    });

    it('handles mixed case input', () => {
      expect(isMutatingMethod('Post')).toBe(true);
      expect(isMutatingMethod('Delete')).toBe(true);
    });
  });
});
