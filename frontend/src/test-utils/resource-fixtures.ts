import type { ResourceTypeInfo } from '@foundation/src/lib/api/resource-types-api';
import type { ResourceCustomField } from '@foundation/src/lib/api/resource-custom-fields-api';

/** A plain, non-system resource type: no geometry, no directory profile. */
export const machineResourceType: ResourceTypeInfo = {
  id: 'type-machine',
  key: 'machine',
  displayName: 'Machine',
  displayNamePlural: 'Machines',
  hasGeometry: false,
  hasDirectoryProfile: false,
  singleGroupMembership: false,
  scanCodesEnabled: true,
  isSystem: false,
  isActive: true,
  createdAt: '2026-01-01T00:00:00Z',
  updatedAt: '2026-01-01T00:00:00Z',
};

/**
 * A text custom field on `machineResourceType`. The `key` doubles as the label and the
 * id suffix, so a test reads as `customField({ key: 'serial' })`.
 */
export function customField(overrides: Partial<ResourceCustomField> & { key: string }): ResourceCustomField {
  return {
    id: `field-${overrides.key}`,
    resourceTypeId: machineResourceType.id,
    label: overrides.key,
    dataType: 'text',
    isRequired: false,
    sortOrder: 0,
    isActive: true,
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
    ...overrides,
  };
}
