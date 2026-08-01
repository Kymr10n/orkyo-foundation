import { describe, it, expect } from 'vitest';
import {
  RESOURCE_TYPE_ICONS,
  DEFAULT_RESOURCE_TYPE_ICON,
  resourceTypeIcon,
} from './resource-type-icon';

describe('resourceTypeIcon', () => {
  it('resolves a name in the allow-list', () => {
    expect(resourceTypeIcon('Car')).toBe(RESOURCE_TYPE_ICONS.Car);
  });

  // The stored value is tenant data and the allow-list is a bundle detail, so the two can
  // drift — a type saved against a newer build must still render, not crash the nav.
  it.each([null, undefined, '', 'NotAnIcon', 'car'])(
    'falls back to the default for %p',
    (name) => {
      expect(resourceTypeIcon(name)).toBe(DEFAULT_RESOURCE_TYPE_ICON);
    },
  );

  it('never resolves an inherited Object property to a component', () => {
    expect(resourceTypeIcon('constructor')).toBe(DEFAULT_RESOURCE_TYPE_ICON);
    expect(resourceTypeIcon('toString')).toBe(DEFAULT_RESOURCE_TYPE_ICON);
  });
});
