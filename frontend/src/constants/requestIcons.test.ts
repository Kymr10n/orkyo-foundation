import { describe, it, expect } from 'vitest';
import {
  REQUEST_ICONS,
  REQUEST_ICON_GROUPS,
  getRequestIcon,
} from './requestIcons';

describe('REQUEST_ICONS registry', () => {
  it('has at least the documented number of curated icons', () => {
    // Spec calls for ~30; guard against accidental shrinkage.
    expect(REQUEST_ICONS.length).toBeGreaterThanOrEqual(25);
  });

  it('uses unique IDs', () => {
    const ids = REQUEST_ICONS.map((i) => i.id);
    expect(new Set(ids).size).toBe(ids.length);
  });

  it('keeps every ID within the backend length limit (64 chars)', () => {
    for (const icon of REQUEST_ICONS) {
      expect(icon.id.length).toBeLessThanOrEqual(64);
    }
  });

  it('uses kebab-case IDs (no whitespace, lowercase)', () => {
    for (const icon of REQUEST_ICONS) {
      expect(icon.id).toMatch(/^[a-z0-9-]+$/);
    }
  });

  it('binds every entry to a component', () => {
    for (const icon of REQUEST_ICONS) {
      expect(icon.component).toBeTypeOf('object');
      expect(icon.label.trim().length).toBeGreaterThan(0);
      expect(icon.group.trim().length).toBeGreaterThan(0);
    }
  });
});

describe('REQUEST_ICON_GROUPS', () => {
  it('is derived from REQUEST_ICONS and contains every group once', () => {
    const groupsFromIcons = new Set(REQUEST_ICONS.map((i) => i.group));
    expect(new Set(REQUEST_ICON_GROUPS)).toEqual(groupsFromIcons);
    expect(REQUEST_ICON_GROUPS.length).toBe(groupsFromIcons.size);
  });

  it('preserves first-occurrence order', () => {
    const expected: string[] = [];
    const seen = new Set<string>();
    for (const icon of REQUEST_ICONS) {
      if (!seen.has(icon.group)) {
        expected.push(icon.group);
        seen.add(icon.group);
      }
    }
    expect([...REQUEST_ICON_GROUPS]).toEqual(expected);
  });
});

describe('getRequestIcon', () => {
  it('returns the component for a known id', () => {
    const known = REQUEST_ICONS[0];
    expect(getRequestIcon(known.id)).toBe(known.component);
  });

  it.each([null, undefined, '', 'this-icon-does-not-exist'] as const)(
    'returns undefined for %s',
    (input) => {
      expect(getRequestIcon(input)).toBeUndefined();
    },
  );
});
