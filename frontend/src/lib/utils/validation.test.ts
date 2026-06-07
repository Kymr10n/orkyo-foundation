import { describe, it, expect } from 'vitest';
import { isValidEmail } from './validation';

describe('isValidEmail', () => {
  it.each([
    'user@example.com',
    'alice.bob@sub.domain.org',
    'user+tag@example.co.uk',
    'x@y.z',
    '123@456.789',
  ])('returns true for valid address: %s', (email) => {
    expect(isValidEmail(email)).toBe(true);
  });

  it.each([
    '',
    'notanemail',
    '@nodomain',
    'missing-at-sign.com',
    'user@',
    'user@ example.com',
    'user @example.com',
    'user@example',
  ])('returns false for invalid address: %s', (email) => {
    expect(isValidEmail(email)).toBe(false);
  });
});
