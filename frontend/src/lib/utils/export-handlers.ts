import type { Request, RequestStatus } from '@foundation/src/types/requests';
import type { Space, GeometryType } from '@foundation/src/types/space';
import type { Criterion, CriterionDataType } from '@foundation/src/types/criterion';
import type { Site } from '@foundation/src/types/site';
import type { Conflict } from '@foundation/src/types/requests';
import type { Template } from '@foundation/src/types/templates';
import type { User } from '@foundation/src/types/auth';
import { getSpaceResourceId } from '@foundation/src/domain/scheduling/request-assignments';
import { getResources, type CreateResourceRequest, type ResourceInfo } from '@foundation/src/lib/api/resources-api';
import type { ResourceTypeInfo } from '@foundation/src/lib/api/resource-types-api';
import { resourceContext } from './import-export';
import {
  arrayToCSV,
  csvToArray,
  downloadFile,
  getExportFilename,
  type ExportContext,
  type ExportFormat,
  type ImportFormat,
  type ExportMetadata,
} from './import-export';

// ============================================================================
// SHARED HELPERS
// ============================================================================

function buildJsonExport(context: ExportContext, data: unknown): string {
  const metadata: ExportMetadata = {
    exportTimestamp: new Date().toISOString(),
    schemaVersion: '1.0.0',
    context,
  };
  return JSON.stringify({ ...metadata, data }, null, 2);
}

// ============================================================================
// UTILIZATION EXPORT (PDF) - Lazy loaded
// ============================================================================

export async function exportUtilization(
  requests: Request[],
  startDate: Date,
  endDate: Date,
  /** Types to render, in display order — one section per type. Scoped by the caller's tab. */
  resourceTypes: ResourceTypeInfo[]
) {
  // Row labels for every resource type, fetched here rather than threaded
  // through the page: an export is a deliberate user action, and the page holds
  // only the resources of the tab being looked at. Paged: the server caps
  // pageSize at 100, and a single default-page call left every resource beyond
  // it labelled "Unknown resource".
  const resources: ResourceInfo[] = [];
  for (let page = 1; ; page++) {
    const res = await getResources({ isActive: true, page, pageSize: 100 });
    resources.push(...res.data);
    if (resources.length >= res.total || res.data.length === 0) break;
  }
  // The type comes along: the chart sections rows by it, so people and spaces
  // never interleave in one alphabetical list.
  const resourceMap = new Map(
    resources.map((r) => [r.id, { name: r.name, typeKey: r.resourceTypeKey }]),
  );

  // Dynamically import PDF export to reduce initial bundle size
  const { exportGanttChartToPDF } = await import('./gantt-pdf-export');
  exportGanttChartToPDF({
    requests,
    resources: resourceMap,
    resourceTypes,
    startDate,
    endDate,
  });
}

// ============================================================================
// RESOURCES EXPORT/IMPORT (every resource type — people, tools, tenant-defined)
// ============================================================================

/**
 * A resource row as exported: the fields every resource carries, plus whatever
 * the calling page adds (a person's profile fields, say). One exporter serves
 * every type — including types that did not exist when this code was written,
 * which is why the columns are derived from the data rather than listed here.
 */
export type ResourceExportRow = ResourceInfo & Record<string, unknown>;

/** Fields that describe the row's identity in THIS system, not its content. */
const RESOURCE_INTERNAL_FIELDS = new Set([
  'resourceTypeId', 'currentSiteId', 'createdAt', 'updatedAt', 'metadata',
]);

/** camelCase → snake_case, matching the column style of the other exporters. */
function toColumnName(field: string): string {
  return field.replace(/[A-Z]/g, (c) => `_${c.toLowerCase()}`);
}

function resourceToRow(resource: ResourceExportRow): Record<string, unknown> {
  const row: Record<string, unknown> = {};
  for (const [key, value] of Object.entries(resource)) {
    if (RESOURCE_INTERNAL_FIELDS.has(key)) continue;
    row[toColumnName(key)] = value;
  }
  // A type's own fields round-trip under a `meta.` prefix, so a custom field can
  // never collide with a core column.
  for (const [key, value] of Object.entries(resource.metadata ?? {})) {
    row[`meta.${key}`] = value;
  }
  return row;
}

export async function exportResources(
  resources: ResourceExportRow[],
  format: ExportFormat,
  typeKey: string,
) {
  const context = resourceContext(typeKey);
  const filename = getExportFilename(context, format);
  const rows = resources.map(resourceToRow);

  if (format === 'json') {
    downloadFile(buildJsonExport(context, rows), filename, 'application/json');
    return;
  }
  // Union of all keys: a row missing a custom field must not truncate the header.
  const headers = [...new Set(rows.flatMap((r) => Object.keys(r)))];
  downloadFile(arrayToCSV(rows, headers), filename, 'text/csv');
}

/** A parsed import row: the create-request plus the raw columns behind it. */
export interface ResourceImportRow {
  request: CreateResourceRequest;
  /** Every column as parsed, so a type's own page can use its extra ones. */
  source: Record<string, unknown>;
}

/**
 * Parses an export back into create-requests. Unknown columns are kept on
 * `source` (a page may own side tables the core request cannot express), `id`
 * is dropped (an import creates, it does not overwrite), and `meta.*` columns
 * are folded back into the metadata document.
 */
export async function importResources(
  file: File,
  format: ImportFormat,
  typeKey: string,
): Promise<ResourceImportRow[]> {
  const text = await file.text();
  const rows: Record<string, unknown>[] =
    format === 'json' ? (JSON.parse(text).data ?? JSON.parse(text)) : csvToArray(text);

  // Cells arrive as scalars from CSV and can be anything from JSON; anything
  // that is not a scalar is not a field value and is treated as absent.
  const cell = (value: unknown): string => {
    if (typeof value === 'string') return value;
    if (typeof value === 'number' || typeof value === 'boolean') return String(value);
    return '';
  };

  return rows
    .filter((row) => cell(row.name).trim().length > 0)
    .map((row) => {
      const metadata: Record<string, unknown> = {};
      for (const [key, value] of Object.entries(row)) {
        if (key.startsWith('meta.') && value !== '' && value !== null) {
          metadata[key.slice('meta.'.length)] = value;
        }
      }
      const request: CreateResourceRequest = {
        resourceTypeKey: typeKey,
        name: cell(row.name).trim(),
        allocationMode: cell(row.allocation_mode) || 'exclusive',
      };
      if (cell(row.description)) request.description = cell(row.description);
      if (cell(row.external_reference)) request.externalReference = cell(row.external_reference);
      if (cell(row.base_availability_percent)) {
        request.baseAvailabilityPercent = Number(cell(row.base_availability_percent));
      }
      if (cell(row.home_site_id)) request.homeSiteId = cell(row.home_site_id);
      if (cell(row.cross_site_allowed)) {
        request.crossSiteAllowed = cell(row.cross_site_allowed) === 'true';
      }
      if (Object.keys(metadata).length > 0) request.metadata = metadata;
      return { request, source: row };
    });
}

// ============================================================================
// SPACES EXPORT/IMPORT
// ============================================================================

export async function exportSpaces(spaces: Space[], format: ExportFormat, _siteId?: string) {
  const filename = getExportFilename('spaces', format);

  if (format === 'csv') {
    const data = spaces.map(space => ({
      id: space.id,
      name: space.name,
      code: space.code || '',
      description: space.description || '',
      is_physical: space.isPhysical,
      geometry_type: space.geometry?.type || '',
      coordinates: space.geometry?.coordinates ? JSON.stringify(space.geometry.coordinates) : '',
      site_id: space.siteId || '',
      group_id: space.groupId || '',
      created_at: space.createdAt,
      updated_at: space.updatedAt,
    }));

    const csv = arrayToCSV(data);
    downloadFile(csv, filename, 'text/csv');
  }
}

export async function importSpaces(file: File, format: ImportFormat): Promise<Space[]> {
  const content = await file.text();

  if (format === 'csv') {
    const rows = csvToArray(content);
    return rows.map(row => ({
      id: row.id,
      siteId: row.site_id,
      name: row.name,
      code: row.code || undefined,
      description: row.description || undefined,
      isPhysical: row.is_physical === 'true',
      geometry: row.coordinates ? {
        type: row.geometry_type as GeometryType,
        coordinates: JSON.parse(row.coordinates),
      } : undefined,
      groupId: row.group_id || undefined,
      createdAt: row.created_at || new Date().toISOString(),
      updatedAt: row.updated_at || new Date().toISOString(),
    })) as Space[];
  }

  return [];
}

// ============================================================================
// REQUESTS EXPORT/IMPORT
// ============================================================================

export async function exportRequests(requests: Request[], format: ExportFormat) {
  const filename = getExportFilename('requests', format);

  if (format === 'csv') {
    const data = requests.map(request => ({
      id: request.id,
      name: request.name,
      description: request.description || '',
      status: request.status,
      start_ts: request.startTs || '',
      end_ts: request.endTs || '',
      resource_id: getSpaceResourceId(request) || '',
      resource_name: '', // Resource name needs to be fetched separately
      earliest_start_ts: request.earliestStartTs || '',
      latest_end_ts: request.latestEndTs || '',
      min_duration_value: request.minimalDurationValue || '',
      min_duration_unit: request.minimalDurationUnit || '',
      actual_duration_value: request.actualDurationValue || '',
      actual_duration_unit: request.actualDurationUnit || '',
      requirements_count: request.requirements?.length || 0,
    }));

    const csv = arrayToCSV(data);
    downloadFile(csv, filename, 'text/csv');
  }
}

export async function importRequests(file: File, format: ImportFormat): Promise<Partial<Request>[]> {
  const content = await file.text();

  if (format === 'csv') {
    const rows = csvToArray(content);
    return rows.map(row => ({
      id: row.id,
      name: row.name,
      description: row.description || null,
      status: row.status as RequestStatus,
      start_ts: row.start_ts || null,
      end_ts: row.end_ts || null,
      resource_id: row.resource_id || null,
      earliest_start_ts: row.earliest_start_ts || null,
      latest_end_ts: row.latest_end_ts || null,
      min_duration_value: row.min_duration_value ? parseInt(row.min_duration_value) : null,
      min_duration_unit: row.min_duration_unit || null,
      actual_duration_value: row.actual_duration_value ? parseInt(row.actual_duration_value) : null,
      actual_duration_unit: row.actual_duration_unit || null,
    }));
  }

  return [];
}

// ============================================================================
// CONFLICTS EXPORT
// ============================================================================

export async function exportConflicts(conflicts: Conflict[], format: ExportFormat) {
  const filename = getExportFilename('conflicts', format);

  if (format === 'csv') {
    const data = conflicts.map(conflict => ({
      id: conflict.id,
      kind: conflict.kind,
      severity: conflict.severity,
      message: conflict.message,
    }));

    const csv = arrayToCSV(data);
    downloadFile(csv, filename, 'text/csv');
  }
}

// ============================================================================
// CRITERIA EXPORT/IMPORT
// ============================================================================

export async function exportCriteria(criteria: Criterion[], format: ExportFormat) {
  const filename = getExportFilename('criteria', format);

  if (format === 'csv') {
    const data = criteria.map(criterion => ({
      id: criterion.id,
      name: criterion.name,
      description: criterion.description || '',
      data_type: criterion.dataType,
      unit: criterion.unit || '',
      enum_values: criterion.enumValues ? JSON.stringify(criterion.enumValues) : '',
      created_at: criterion.createdAt,
      updated_at: criterion.updatedAt,
    }));

    const csv = arrayToCSV(data);
    downloadFile(csv, filename, 'text/csv');
  } else if (format === 'json') {
    downloadFile(buildJsonExport('criteria', criteria), filename, 'application/json');
  }
}

export async function importCriteria(file: File, format: ImportFormat): Promise<Partial<Criterion>[]> {
  const content = await file.text();

  if (format === 'csv') {
    const rows = csvToArray(content);
    return rows.map(row => ({
      id: row.id,
      name: row.name,
      description: row.description || undefined,
      dataType: row.data_type as CriterionDataType,
      unit: row.unit || undefined,
      enumValues: row.enum_values ? JSON.parse(row.enum_values) : undefined,
      createdAt: row.created_at || new Date().toISOString(),
      updatedAt: row.updated_at || new Date().toISOString(),
    }));
  } else if (format === 'json') {
    const parsed = JSON.parse(content);
    return parsed.data || parsed; // Handle both wrapped and unwrapped formats
  }

  return [];
}

// ============================================================================
// SITES EXPORT/IMPORT
// ============================================================================

export async function exportSites(sites: Site[], format: ExportFormat) {
  const filename = getExportFilename('sites', format);

  if (format === 'csv') {
    const data = sites.map(site => ({
      id: site.id,
      code: site.code,
      name: site.name,
      description: site.description || '',
      address: site.address || '',
      created_at: site.createdAt,
      updated_at: site.updatedAt,
    }));

    const csv = arrayToCSV(data);
    downloadFile(csv, filename, 'text/csv');
  } else if (format === 'json') {
    downloadFile(buildJsonExport('sites', sites), filename, 'application/json');
  }
}

export async function importSites(file: File, format: ImportFormat): Promise<Partial<Site>[]> {
  const content = await file.text();

  if (format === 'csv') {
    const rows = csvToArray(content);
    return rows.map(row => ({
      id: row.id,
      name: row.name,
      location: row.location || null,
      timezone: row.timezone || null,
    }));
  } else if (format === 'json') {
    const parsed = JSON.parse(content);
    return parsed.data || parsed;
  }

  return [];
}

// ============================================================================
// TEMPLATES EXPORT/IMPORT
// ============================================================================

export async function exportTemplates(templates: Template[], format: ExportFormat) {
  const filename = getExportFilename('templates', format);

  if (format === 'csv') {
    const data = templates.map(template => ({
      id: template.id,
      name: template.name,
      description: template.description || '',
      requirements: JSON.stringify(template.items || []),
    }));

    const csv = arrayToCSV(data);
    downloadFile(csv, filename, 'text/csv');
  } else if (format === 'json') {
    downloadFile(buildJsonExport('templates', templates), filename, 'application/json');
  }
}

export async function importTemplates(file: File, format: ImportFormat): Promise<Partial<Template>[]> {
  const content = await file.text();

  if (format === 'csv') {
    const rows = csvToArray(content);
    return rows.map(row => ({
      id: row.id,
      name: row.name,
      description: row.description || undefined,
      items: row.requirements ? JSON.parse(row.requirements) : [],
    }));
  } else if (format === 'json') {
    const parsed = JSON.parse(content);
    return parsed.data || parsed;
  }

  return [];
}

// ============================================================================
// USERS EXPORT/IMPORT
// ============================================================================

export async function exportUsers(users: User[], format: ExportFormat) {
  const filename = getExportFilename('users', format);

  if (format === 'csv') {
    const data = users.map(user => ({
      id: user.id,
      email: user.email,
      display_name: user.displayName || '',
      role: user.role,
      is_active: user.status === 'active',
    }));

    const csv = arrayToCSV(data);
    downloadFile(csv, filename, 'text/csv');
  } else if (format === 'json') {
    downloadFile(buildJsonExport('users', users), filename, 'application/json');
  }
}

export async function importUsers(file: File, format: ImportFormat): Promise<Partial<User>[]> {
  const content = await file.text();

  if (format === 'csv') {
    const rows = csvToArray(content);
    return rows.map(row => ({
      id: row.id,
      email: row.email,
      displayName: row.display_name || undefined,
      role: row.role as User['role'],
      status: row.is_active !== 'false' ? 'active' as const : 'suspended' as const,
    }));
  } else if (format === 'json') {
    const parsed = JSON.parse(content);
    return parsed.data || parsed;
  }

  return [];
}
