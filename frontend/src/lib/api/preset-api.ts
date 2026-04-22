import { apiGet, apiPost } from '../core/api-client';
import { API_PATHS } from '../core/api-paths';

// ============================================================================
// Types
// ============================================================================

export interface Preset {
  presetId: string;
  name: string;
  description?: string;
  vendor?: string;
  industry?: string;
  version: string;
  minAppVersion?: string;
  createdAt: string;
  contents: PresetContents;
}

export interface PresetContents {
  criteria: PresetCriterion[];
  spaceGroups: PresetSpaceGroup[];
  templates: PresetTemplates;
}

export interface PresetTemplates {
  space: PresetTemplate[];
  group: PresetTemplate[];
  request: PresetTemplate[];
}

export interface PresetCriterion {
  key: string;
  name: string;
  description?: string;
  dataType: 'Boolean' | 'Number' | 'String' | 'Enum';
  enumValues?: string[];
  unit?: string;
}

export interface PresetSpaceGroup {
  key: string;
  name: string;
  description?: string;
  color?: string;
  displayOrder?: number;
}

export interface PresetTemplate {
  key: string;
  name: string;
  description?: string;
  durationValue?: number;
  durationUnit?: string;
  fixedStart?: boolean;
  fixedEnd?: boolean;
  fixedDuration?: boolean;
  items: PresetTemplateItem[];
}

export interface PresetTemplateItem {
  criterionKey: string;
  value: string;
}

export interface PresetValidationResult {
  isValid: boolean;
  errors: string[];
}

export interface PresetApplicationResult {
  success: boolean;
  error?: string;
  stats: PresetApplicationStats;
}

export interface PresetApplicationStats {
  criteriaCreated: number;
  criteriaUpdated: number;
  spaceGroupsCreated: number;
  spaceGroupsUpdated: number;
  templatesCreated: number;
  templatesUpdated: number;
}

export interface PresetApplication {
  id: string;
  presetId: string;
  presetVersion: string;
  appliedAt: string;
  updatedAt?: string;
  appliedByUserId?: string;
}

// ============================================================================
// API Functions
// ============================================================================

/**
 * Validate a preset without applying it
 */
export async function validatePreset(preset: Preset): Promise<PresetValidationResult> {
  return apiPost<PresetValidationResult>(API_PATHS.ADMIN.PRESETS_VALIDATE, preset);
}

/**
 * Apply a preset to the current tenant
 */
export async function applyPreset(preset: Preset): Promise<PresetApplicationResult> {
  return apiPost<PresetApplicationResult>(API_PATHS.ADMIN.PRESETS_APPLY, preset);
}

/**
 * Export current tenant configuration as a preset
 */
export async function exportPreset(presetId: string, name: string, description?: string): Promise<Preset> {
  const params = new URLSearchParams({ presetId, name });
  if (description) {
    params.append('description', description);
  }
  return apiGet<Preset>(`${API_PATHS.ADMIN.PRESETS_EXPORT}?${params}`);
}

/**
 * Get preset application history
 */
export async function getPresetApplications(): Promise<PresetApplication[]> {
  return apiGet<PresetApplication[]>(API_PATHS.ADMIN.PRESETS_APPLICATIONS);
}

/**
 * Parse a preset JSON file
 */
export function parsePresetFile(content: string): Preset {
  try {
    return JSON.parse(content) as Preset;
  } catch {
    throw new Error('Invalid JSON format');
  }
}

/**
 * Download a preset as a JSON file
 */
export function downloadPreset(preset: Preset): void {
  const json = JSON.stringify(preset, null, 2);
  const blob = new Blob([json], { type: 'application/json' });
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = `${preset.presetId}.preset.json`;
  document.body.appendChild(a);
  a.click();
  document.body.removeChild(a);
  URL.revokeObjectURL(url);
}
