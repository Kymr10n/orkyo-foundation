import { describe, it, expect, vi, beforeEach } from 'vitest';
import {
  validatePreset,
  applyPreset,
  exportPreset,
  getPresetApplications,
  parsePresetFile,
  downloadPreset,
} from './preset-api';
import * as apiClient from '../core/api-client';
import { API_PATHS } from '../core/api-paths';
import type { Preset } from './preset-api';

vi.mock('../core/api-client');

const mockPreset: Preset = {
  presetId: 'preset-1',
  name: 'Office Standard',
  description: 'Standard office setup',
  vendor: 'Orkyo',
  industry: 'General',
  version: '1.0.0',
  createdAt: '2024-01-01T00:00:00Z',
  contents: {
    criteria: [],
    spaceGroups: [],
    templates: { space: [], group: [], request: [] },
  },
};

describe('preset-api', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('validatePreset posts to the validate endpoint', async () => {
    vi.mocked(apiClient.apiPost).mockResolvedValue({ isValid: true, errors: [] });
    const result = await validatePreset(mockPreset);
    expect(apiClient.apiPost).toHaveBeenCalledWith(API_PATHS.ADMIN.PRESETS_VALIDATE, mockPreset);
    expect(result.isValid).toBe(true);
  });

  it('applyPreset posts to the apply endpoint', async () => {
    const mockResult = { success: true, stats: { criteriaCreated: 2, criteriaUpdated: 0, spaceGroupsCreated: 1, spaceGroupsUpdated: 0, templatesCreated: 3, templatesUpdated: 0 } };
    vi.mocked(apiClient.apiPost).mockResolvedValue(mockResult);
    const result = await applyPreset(mockPreset);
    expect(apiClient.apiPost).toHaveBeenCalledWith(API_PATHS.ADMIN.PRESETS_APPLY, mockPreset);
    expect(result.success).toBe(true);
  });

  it('exportPreset calls GET with query params', async () => {
    vi.mocked(apiClient.apiGet).mockResolvedValue(mockPreset);
    await exportPreset('preset-1', 'My Preset', 'A description');
    expect(apiClient.apiGet).toHaveBeenCalledWith(
      expect.stringContaining(API_PATHS.ADMIN.PRESETS_EXPORT),
    );
  });

  it('getPresetApplications calls GET on applications endpoint', async () => {
    vi.mocked(apiClient.apiGet).mockResolvedValue([]);
    const result = await getPresetApplications();
    expect(apiClient.apiGet).toHaveBeenCalledWith(API_PATHS.ADMIN.PRESETS_APPLICATIONS);
    expect(result).toEqual([]);
  });

  it('parsePresetFile parses valid JSON', () => {
    const result = parsePresetFile(JSON.stringify(mockPreset));
    expect(result.presetId).toBe('preset-1');
  });

  it('parsePresetFile throws on invalid JSON', () => {
    expect(() => parsePresetFile('not json')).toThrow('Invalid JSON format');
  });

  it('downloadPreset creates and clicks a download link', () => {
    const createSpy = vi.spyOn(document, 'createElement');
    const appendSpy = vi.spyOn(document.body, 'appendChild').mockImplementation((el) => el);
    const removeSpy = vi.spyOn(document.body, 'removeChild').mockImplementation((el) => el);
    const revokeUrl = vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => {});

    downloadPreset(mockPreset);

    expect(createSpy).toHaveBeenCalledWith('a');
    expect(appendSpy).toHaveBeenCalled();
    expect(removeSpy).toHaveBeenCalled();
    expect(revokeUrl).toHaveBeenCalled();

    createSpy.mockRestore();
    appendSpy.mockRestore();
    removeSpy.mockRestore();
    revokeUrl.mockRestore();
  });
});
