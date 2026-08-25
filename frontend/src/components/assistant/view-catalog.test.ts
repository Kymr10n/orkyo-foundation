import { describe, it, expect } from 'vitest';
import { resolveView } from './view-catalog';

describe('resolveView', () => {
  it('resolves a page to its final route', () => {
    expect(resolveView('insights_conflicts')).toEqual({
      label: 'Insights → Conflicts',
      path: '/insights/conflicts',
    });
  });

  it('never points at a route that redirects on arrival', () => {
    // /insights, /settings and /tenant-admin all bounce to a child. Sending the router
    // somewhere that immediately redirects is the loop the tour had to be rescued from.
    const redirectOnly = ['/insights', '/settings', '/tenant-admin', '/configuration', '/conflicts'];
    const pages = [
      'scheduling', 'requests', 'insights_overview', 'insights_utilization',
      'insights_conflicts', 'organization', 'stations', 'assets', 'floorplan',
      'settings_criteria', 'settings_templates', 'settings_scheduling',
      'admin_sites', 'admin_users', 'admin_ai_assistant', 'configuration_resource_types',
    ];

    for (const id of pages) {
      const target = resolveView(id);
      expect(target, id).not.toBeNull();
      expect(redirectOnly, id).not.toContain(target!.path);
    }
  });

  it('builds a record view on the app\'s existing ?edit= convention', () => {
    expect(resolveView('request', 'abc-123')?.path).toBe('/requests?edit=abc-123');
  });

  it('escapes the record id', () => {
    // The id reaches the URL, so it is encoded rather than trusted.
    expect(resolveView('request', 'a b&c')?.path).toBe('/requests?edit=a%20b%26c');
  });

  it('refuses a record view with no record', () => {
    expect(resolveView('request')).toBeNull();
    expect(resolveView('request', null)).toBeNull();
  });

  it('refuses a view it does not know', () => {
    expect(resolveView('../../etc/passwd')).toBeNull();
    expect(resolveView('')).toBeNull();
  });

  it('ignores an entity id on a page view', () => {
    // A stray id must not turn a page into something else.
    expect(resolveView('requests', 'abc')?.path).toBe('/requests');
  });
});
