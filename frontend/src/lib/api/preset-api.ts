import { apiGet, apiPost } from '../core/api-client';
import { API_PATHS } from '../core/api-paths';
import { downloadFile } from '../utils/import-export';

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
  /** Schema 1.1.0; absent in a 1.0.0 file. Applied first: everything below refers to them by key. */
  resourceTypes?: PresetResourceType[];
  criteria: PresetCriterion[];
  spaceGroups: PresetSpaceGroup[];
  templates: PresetTemplates;
  /** Schema 1.1.0; absent in a 1.0.0 file and never in an export (resources are data, not configuration). */
  resources?: PresetResource[];
}

/**
 * A resource type the preset needs. A catalog key (person, tool, mill, ...) is activated from the
 * product's catalog and the names/flags here are ignored; any other key is an ad-hoc type.
 */
export interface PresetResourceType {
  key: string;
  displayName?: string;
  displayNamePlural?: string;
  description?: string;
  icon?: string;
  hasGeometry?: boolean;
  hasDirectoryProfile?: boolean;
  singleGroupMembership?: boolean;
}

/** A named sample resource: one room, person, machine or tool. */
export interface PresetResource {
  key: string;
  name: string;
  code?: string;
  description?: string;
  typeKey: string;
  allocationMode?: 'Exclusive' | 'Fractional';
  groupKeys?: string[];
  capabilities?: PresetCapability[];
}

export interface PresetCapability {
  criterionKey: string;
  value: string;
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
  /** Keys of the resource types this criterion applies to (schema 1.1.0). */
  resourceTypeKeys?: string[];
}

export interface PresetSpaceGroup {
  key: string;
  name: string;
  description?: string;
  color?: string;
  displayOrder?: number;
  /** Key of the resource type the group holds (schema 1.1.0); unset keeps the placeable-type default. */
  resourceTypeKey?: string;
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
  resourceTypesActivated: number;
  criteriaCreated: number;
  criteriaUpdated: number;
  spaceGroupsCreated: number;
  spaceGroupsUpdated: number;
  templatesCreated: number;
  templatesUpdated: number;
  resourcesCreated: number;
  resourcesUpdated: number;
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

export async function validatePreset(preset: Preset): Promise<PresetValidationResult> {
  return apiPost<PresetValidationResult>(API_PATHS.ADMIN.PRESETS_VALIDATE, preset);
}

export async function applyPreset(preset: Preset): Promise<PresetApplicationResult> {
  return apiPost<PresetApplicationResult>(API_PATHS.ADMIN.PRESETS_APPLY, preset);
}

/**
 * Export current tenant configuration as a preset
 */
export async function exportPreset(presetId: string, name: string, description?: string): Promise<Preset> {
  const params = { presetId, name, description: description || undefined };
  return apiGet<Preset>(API_PATHS.ADMIN.PRESETS_EXPORT, { params });
}

export async function getPresetApplications(): Promise<PresetApplication[]> {
  return apiGet<PresetApplication[]>(API_PATHS.ADMIN.PRESETS_APPLICATIONS);
}

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
  downloadFile(json, `${preset.presetId}.preset.json`, 'application/json');
}
