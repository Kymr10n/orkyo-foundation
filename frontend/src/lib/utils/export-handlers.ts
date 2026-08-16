import type { CreateRequestRequest, DurationUnit, Request, RequestStatus } from '@foundation/src/types/requests';
import type { GeometryType } from '@foundation/src/types/geometry';
import type { Criterion, CriterionDataType } from '@foundation/src/types/criterion';
import type { Site } from '@foundation/src/types/site';
import type { Conflict } from '@foundation/src/types/requests';
import type { Template } from '@foundation/src/types/templates';
import type { User } from '@foundation/src/types/auth';
import { getPlacementResourceId } from '@foundation/src/domain/scheduling/request-assignments';
import { getResources, type CreateResourceRequest, type ResourceInfo } from '@foundation/src/lib/api/resources-api';
import type { ResourceTypeInfo } from '@foundation/src/lib/api/resource-types-api';
import type { CustomFieldDataType, CustomFieldValue } from '@foundation/src/lib/api/resource-custom-fields-api';
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
  'resourceTypeId', 'currentSiteId', 'createdAt', 'updatedAt', 'customFields',
]);

/** Objects that need their own serialization — a CSV cell cannot hold one. */
const RESOURCE_STRUCTURED_FIELDS = new Set(['geometry', 'properties']);

/** camelCase → snake_case, matching the column style of the other exporters. */
function toColumnName(field: string): string {
  return field.replace(/[A-Z]/g, (c) => `_${c.toLowerCase()}`);
}

function resourceToRow(resource: ResourceExportRow): Record<string, unknown> {
  const row: Record<string, unknown> = {};
  for (const [key, value] of Object.entries(resource)) {
    if (RESOURCE_INTERNAL_FIELDS.has(key)) continue;
    if (RESOURCE_STRUCTURED_FIELDS.has(key)) continue;
    row[toColumnName(key)] = value;
  }
  // Placement. A shape is two columns rather than one blob so a spreadsheet stays legible, and
  // they keep the names the retired spaces export used, so files people already have still read.
  // Emitted only when the resource actually carries geometry — a person's export is unchanged.
  if (resource.geometry) {
    row.geometry_type = resource.geometry.type;
    row.coordinates = JSON.stringify(resource.geometry.coordinates);
  }
  if (resource.properties && Object.keys(resource.properties).length > 0) {
    row.properties = JSON.stringify(resource.properties);
  }
  // A type's own fields round-trip under a `meta.` prefix, so a custom field can
  // never collide with a core column.
  for (const [key, value] of Object.entries(resource.customFields ?? {})) {
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

/**
 * Reads a CSV cell back as the type its field declares. Guessing from the text cannot work in
 * either direction — an all-digit serial number is text, and "1200" in a number field is a
 * number — so the declared type decides. A value that does not parse is passed through
 * unchanged for the server to reject by name, rather than being silently dropped.
 */
function coerceToDeclaredType(value: string, dataType: CustomFieldDataType | undefined): CustomFieldValue {
  if (dataType === 'number') {
    const parsed = Number(value);
    return value.trim() !== '' && Number.isFinite(parsed) ? parsed : value;
  }
  if (dataType === 'boolean') {
    if (value === 'true') return true;
    if (value === 'false') return false;
  }
  return value;
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
 * are folded back into the custom-field document.
 *
 * @param customFieldTypes the type's custom fields keyed by field key, so CSV strings can be
 *   read back as numbers and booleans. Omit when the caller has no definitions to hand: values
 *   then arrive as text and the server rejects any that its field disagrees with.
 */
export async function importResources(
  file: File,
  format: ImportFormat,
  typeKey: string,
  customFieldTypes: Record<string, CustomFieldDataType> = {},
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
      // `meta.<key>` columns carry custom-field values. A JSON export keeps its types; every
      // CSV cell is a string, so each is read back as whatever its field declares — without
      // that, a file this module exported could not be re-imported once the server started
      // type-checking values. Anything non-scalar is not a value.
      const customFields: Record<string, CustomFieldValue> = {};
      for (const [key, value] of Object.entries(row)) {
        if (!key.startsWith('meta.') || value === '' || value == null) continue;
        const fieldKey = key.slice('meta.'.length);
        if (typeof value === 'number' || typeof value === 'boolean') {
          customFields[fieldKey] = value;
        } else if (typeof value === 'string') {
          customFields[fieldKey] = coerceToDeclaredType(value, customFieldTypes[fieldKey]);
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

      // Placement. Absent on a non-placeable type's file, and the server rejects any that turn
      // up on one — so they are read whenever present rather than gated on the type here.
      if (cell(row.code)) request.code = cell(row.code);
      if (cell(row.is_physical)) request.isPhysical = cell(row.is_physical) === 'true';
      if (cell(row.capacity)) request.capacity = Number(cell(row.capacity));
      if (cell(row.coordinates)) {
        // A malformed shape is left out rather than guessed at: the server names the field it
        // rejected, which is more use than a resource placed at coordinates nobody drew.
        try {
          request.geometry = {
            type: cell(row.geometry_type) as GeometryType,
            coordinates: JSON.parse(cell(row.coordinates)),
          };
        } catch {
          // Falls through with no geometry; a physical row without one is rejected by name.
        }
      }
      if (cell(row.properties)) {
        try {
          request.properties = JSON.parse(cell(row.properties)) as Record<string, unknown>;
        } catch {
          // Same reasoning as geometry.
        }
      }
      if (Object.keys(customFields).length > 0) request.customFields = customFields;
      return { request, source: row };
    });
}

// ============================================================================
// REQUESTS EXPORT/IMPORT
// ============================================================================

export async function exportRequests(
  requests: Request[],
  format: ExportFormat,
  placeableKeys: ReadonlySet<string>,
) {
  const filename = getExportFilename('requests', format);

  if (format === 'csv') {
    const data = requests.map(request => ({
      id: request.id,
      name: request.name,
      description: request.description || '',
      status: request.status,
      start_ts: request.startTs || '',
      end_ts: request.endTs || '',
      resource_id: getPlacementResourceId(request, placeableKeys) || '',
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

const REQUEST_STATUSES: readonly RequestStatus[] = ['new', 'in_progress', 'done', 'cancelled', 'deferred'];
const DURATION_UNITS: readonly DurationUnit[] = ['years', 'months', 'weeks', 'days', 'hours', 'minutes'];

export async function importRequests(file: File, format: ImportFormat): Promise<CreateRequestRequest[]> {
  const content = await file.text();

  if (format === 'csv') {
    const rows = csvToArray(content);
    // Maps the export columns (snake_case, see exportRequests above) onto the camelCase
    // create payload. `id` is dropped — an import creates, it does not overwrite. The
    // export carries no tree structure, site, or requirements, so the round-trip is
    // lossy by design: rows come back as root-level requests.
    return rows
      .filter(row => row.name)
      .map(row => {
        const durationValue = parseInt(row.min_duration_value);
        const durationUnit = DURATION_UNITS.find(u => u === row.min_duration_unit);
        const actualValue = parseInt(row.actual_duration_value);
        const actualUnit = DURATION_UNITS.find(u => u === row.actual_duration_unit);
        return {
          name: row.name,
          description: row.description || undefined,
          status: REQUEST_STATUSES.find(s => s === row.status),
          startTs: row.start_ts || undefined,
          endTs: row.end_ts || undefined,
          earliestStartTs: row.earliest_start_ts || undefined,
          latestEndTs: row.latest_end_ts || undefined,
          // The create payload requires a minimal duration; fall back to the request
          // form's own default when the column is missing or unparseable.
          minimalDurationValue: Number.isFinite(durationValue) && durationValue > 0 ? durationValue : 1,
          minimalDurationUnit: durationUnit ?? 'hours',
          actualDurationValue: Number.isFinite(actualValue) && actualValue > 0 && actualUnit ? actualValue : undefined,
          actualDurationUnit: Number.isFinite(actualValue) && actualValue > 0 && actualUnit ? actualUnit : undefined,
          resourceIds: row.resource_id ? [row.resource_id] : undefined,
        };
      });
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
