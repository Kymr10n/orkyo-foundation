import { describe, it, expect } from 'vitest';
import { computeSiteScopeWarning } from './RequestFormDialog';

describe('computeSiteScopeWarning', () => {
  it('returns null for single-site tenants (no site scoping shown)', () => {
    expect(computeSiteScopeWarning(false, 'site-1', '', 'Fab')).toBeNull();
  });

  it('returns null when there is no schedule-site context', () => {
    expect(computeSiteScopeWarning(true, null, 'site-2', undefined)).toBeNull();
    expect(computeSiteScopeWarning(true, undefined, '', undefined)).toBeNull();
  });

  it('returns null when the chosen site already matches the schedule site', () => {
    expect(computeSiteScopeWarning(true, 'site-1', 'site-1', 'Fab')).toBeNull();
  });

  it('warns when the request is scoped to a different site', () => {
    const msg = computeSiteScopeWarning(true, 'site-1', 'site-2', 'Fabrication');
    expect(msg).toMatch(/scoped to another site/i);
    expect(msg).toContain("Fabrication's schedule");
  });

  it('warns when the request is site-neutral ("Any site")', () => {
    const msg = computeSiteScopeWarning(true, 'site-1', '', 'Fabrication');
    expect(msg).toMatch(/Any site/i);
    expect(msg).toContain("Fabrication's schedule");
  });

  it('falls back to "this site" when the schedule site name is unknown', () => {
    const msg = computeSiteScopeWarning(true, 'site-1', '', undefined);
    expect(msg).toContain("this site's schedule");
  });
});
