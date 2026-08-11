import { describe, it, expect, vi, beforeEach } from 'vitest';
import {
  createResourceCustomField,
  customFieldDataTypeLabel,
  deleteResourceCustomField,
  getResourceCustomFields,
  updateResourceCustomField,
  CUSTOM_FIELD_DATA_TYPES,
  type ResourceCustomField,
} from './resource-custom-fields-api';
import * as apiClient from '../core/api-client';

vi.mock('../core/api-client');

const TYPE_ID = 'type-machine';
const FIELD_ID = 'field-1';
// The literal paths, not API_PATHS: this is where a rename has to be noticed, and asserting
// against the constant that produced them would only prove the constant equals itself.
const COLLECTION = `/api/resource-types/${TYPE_ID}/custom-fields`;
const ITEM = `${COLLECTION}/${FIELD_ID}`;

const field: ResourceCustomField = {
  id: FIELD_ID,
  resourceTypeId: TYPE_ID,
  key: 'serial_number',
  label: 'Serial number',
  dataType: 'text',
  isRequired: false,
  sortOrder: 0,
  isActive: true,
  createdAt: '2026-01-01T00:00:00Z',
  updatedAt: '2026-01-01T00:00:00Z',
};

describe('resource-custom-fields-api', () => {
  beforeEach(() => vi.clearAllMocks());

  it('lists a type’s fields from its nested collection', async () => {
    vi.mocked(apiClient.apiGet).mockResolvedValue([field]);

    const result = await getResourceCustomFields(TYPE_ID);

    expect(apiClient.apiGet).toHaveBeenCalledWith(COLLECTION);
    expect(result).toEqual([field]);
  });

  it('creates against the collection', async () => {
    vi.mocked(apiClient.apiPost).mockResolvedValue(field);

    const request = { key: 'serial_number', label: 'Serial number', dataType: 'text' as const };
    const result = await createResourceCustomField(TYPE_ID, request);

    expect(apiClient.apiPost).toHaveBeenCalledWith(COLLECTION, request);
    expect(result).toEqual(field);
  });

  it('updates the item, scoped to its type', async () => {
    vi.mocked(apiClient.apiPut).mockResolvedValue(field);

    await updateResourceCustomField(TYPE_ID, FIELD_ID, { label: 'Serial no.' });

    expect(apiClient.apiPut).toHaveBeenCalledWith(ITEM, { label: 'Serial no.' });
  });

  it('deletes the item, scoped to its type', async () => {
    vi.mocked(apiClient.apiDelete).mockResolvedValue(undefined);

    await deleteResourceCustomField(TYPE_ID, FIELD_ID);

    expect(apiClient.apiDelete).toHaveBeenCalledWith(ITEM);
  });

  describe('data types', () => {
    it('offers every type the server accepts, each with a label and a hint', () => {
      expect(CUSTOM_FIELD_DATA_TYPES.map((t) => t.value)).toEqual([
        'text',
        'number',
        'boolean',
        'date',
        'url',
      ]);
      expect(CUSTOM_FIELD_DATA_TYPES.every((t) => t.label && t.hint)).toBe(true);
    });

    it('names a type the way the picker spells it', () => {
      expect(customFieldDataTypeLabel('url')).toBe('Link');
      expect(customFieldDataTypeLabel('boolean')).toBe('Yes / no');
    });
  });
});
