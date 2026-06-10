import { describe, it, expect } from 'vitest';
import { quotaSeverity } from './quota-display';

/** Build a quota fixture; percentUsed defaults to used/limit unless overridden. */
function quota(used: number, limit: number, opts?: { unlimited?: boolean; percentUsed?: number }) {
  return {
    unlimited: opts?.unlimited ?? false,
    used,
    limit,
    percentUsed: opts?.percentUsed ?? (limit === 0 ? 0 : (used / limit) * 100),
  };
}

describe('quotaSeverity', () => {
  it('is neutral when unlimited regardless of usage', () => {
    expect(quotaSeverity(quota(9999, 1, { unlimited: true }))).toBe('neutral');
  });

  it('is neutral below 80%', () => {
    expect(quotaSeverity(quota(0, 5))).toBe('neutral');   // 0%
    expect(quotaSeverity(quota(1, 5))).toBe('neutral');   // 20%
    expect(quotaSeverity(quota(3, 4))).toBe('neutral');   // 75%
  });

  it('is warning from 80% up to and including exactly full', () => {
    expect(quotaSeverity(quota(4, 5))).toBe('warning');   // 80%
    expect(quotaSeverity(quota(99, 100))).toBe('warning'); // 99%
    // The reported bug: exactly at the limit (1/1, 100%) must NOT be a violation.
    expect(quotaSeverity(quota(1, 1))).toBe('warning');   // 100%, full
    expect(quotaSeverity(quota(5, 5))).toBe('warning');   // 100%, full
  });

  it('is exceeded only when strictly over the limit', () => {
    expect(quotaSeverity(quota(2, 1))).toBe('exceeded');  // 200%, over (e.g. after downgrade)
    expect(quotaSeverity(quota(6, 5))).toBe('exceeded');
  });
});
