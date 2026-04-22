import type { Request, RequestStatus } from '@/types/requests';
import type { Space, GeometryType } from '@/types/space';
import type { Criterion, CriterionDataType } from '@/types/criterion';
import type { Site } from '@/types/site';
import type { Conflict } from '@/types/requests';
import type { Template } from '@/types/templates';
import type { User } from '@/types/auth';
import {
  arrayToCSV,
  csvToArray,
  downloadFile,
  type ExportContext,
  type ExportFormat,
  type ImportFormat,
  type ExportMetadata,
} from './import-export';

// ============================================================================
// SHARED HELPERS
// ============================================================================

function generateFilename(entity: string, format: ExportFormat): string {
  return `${entity}-${new Date().toISOString().split('T')[0]}.${format}`;
}

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
  spaces: Space[],
  startDate: Date,
  endDate: Date
) {
  // Dynamically import PDF export to reduce initial bundle size
  const { exportGanttChartToPDF } = await import('./gantt-pdf-export');
  exportGanttChartToPDF({
    requests,
    spaces,
    startDate,
    endDate,
  });
}

// ============================================================================
// SPACES EXPORT/IMPORT
// ============================================================================

export async function exportSpaces(spaces: Space[], format: ExportFormat, _siteId?: string) {
  const filename = generateFilename('spaces', format);

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
  const filename = generateFilename('requests', format);

  if (format === 'csv') {
    const data = requests.map(request => ({
      id: request.id,
      name: request.name,
      description: request.description || '',
      status: request.status,
      start_ts: request.startTs || '',
      end_ts: request.endTs || '',
      space_id: request.spaceId || '',
      space_name: '', // Space name needs to be fetched separately
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
      space_id: row.space_id || null,
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
  const filename = generateFilename('conflicts', format);

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
  const filename = generateFilename('criteria', format);

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
  const filename = generateFilename('sites', format);

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
  const filename = generateFilename('templates', format);

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
  const filename = generateFilename('users', format);

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
