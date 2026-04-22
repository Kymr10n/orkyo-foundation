import { describe, it, expect } from 'vitest';
import { formatBuildTime } from './formatBuildTime';

describe('formatBuildTime', () => {
  it('formats an ISO string into a human-readable date', () => {
    const result = formatBuildTime('2026-04-15T14:30:00Z');
    // Exact output depends on locale/timezone, but should contain year and time parts
    expect(result).toContain('2026');
    expect(result).toMatch(/\d{1,2}:\d{2}/); // contains HH:MM time
  });

  it('handles date-only ISO strings', () => {
    const result = formatBuildTime('2025-01-01T00:00:00Z');
    expect(result).toContain('2025');
  });
});
