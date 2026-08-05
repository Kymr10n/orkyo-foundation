/**
 * Import/Export utilities for Space Utilization System
 * Implements requirements from requirements_import_export_v1.md
 */

import { formatDateForInput } from './utils';

export type ExportFormat = 'csv' | 'json' | 'pdf';
export type ImportFormat = 'csv' | 'json';

/**
 * Identifies what a page can import/export. Open by design: tenant-defined
 * resource types produce their own contexts (`resources:tool`,
 * `resources:forklift`, …), which a closed union could never express. The
 * authoritative list of live contexts is the registry in the ui-actions store —
 * whatever is mounted right now, nothing more.
 */
export type ExportContext = string;

/** The context a resource type's page registers under, e.g. `resources:tool`. */
export function resourceContext(typeKey: string): ExportContext {
  return `resources:${typeKey}`;
}

export interface ExportMetadata {
  exportTimestamp: string;
  tenantId?: string;
  siteId?: string;
  schemaVersion: '1.0.0';
  context: ExportContext;
}

/**
 * Convert array of objects to CSV string
 */
export function arrayToCSV(
  data: Record<string, unknown>[],
  headers?: string[]
): string {
  if (data.length === 0) return '';

  // Use provided headers or extract from first object
  const csvHeaders = headers || Object.keys(data[0]);
  const headerRow = csvHeaders.join(',');

  const rows = data.map(obj => {
    return csvHeaders.map(header => {
      const value = obj[header];

      // Handle null/undefined
      if (value === null || value === undefined) return '';

      // Handle arrays and objects
      if (typeof value === 'object') {
        return `"${JSON.stringify(value).replace(/"/g, '""')}"`;
      }

      // Handle strings with commas, quotes, or newlines
      const stringValue = typeof value === 'string' ? value
        : typeof value === 'number' || typeof value === 'boolean' ? String(value)
        : JSON.stringify(value);
      if (stringValue.includes(',') || stringValue.includes('"') || stringValue.includes('\n')) {
        return `"${stringValue.replace(/"/g, '""')}"`;
      }

      return stringValue;
    }).join(',');
  });

  return [headerRow, ...rows].join('\n');
}

/**
 * Parse CSV string to array of objects
 */
export function csvToArray<T = Record<string, string>>(
  csv: string,
  headers?: string[]
): T[] {
  const lines = csv.split('\n').filter(line => line.trim());
  if (lines.length === 0) return [];

  // Parse headers from first line or use provided
  const csvHeaders = headers || parseCSVLine(lines[0]);
  const dataLines = headers ? lines : lines.slice(1);

  return dataLines.map(line => {
    const values = parseCSVLine(line);
    const obj: Record<string, string> = {};

    csvHeaders.forEach((header, index) => {
      obj[header] = values[index] || '';
    });

    return obj as T;
  });
}

/**
 * Parse a single CSV line handling quoted values
 */
function parseCSVLine(line: string): string[] {
  const result: string[] = [];
  let current = '';
  let inQuotes = false;

  for (let i = 0; i < line.length; i++) {
    const char = line[i];
    const nextChar = line[i + 1];

    if (char === '"') {
      if (inQuotes && nextChar === '"') {
        // Escaped quote
        current += '"';
        i++; // Skip next quote
      } else {
        // Toggle quote state
        inQuotes = !inQuotes;
      }
    } else if (char === ',' && !inQuotes) {
      // End of field
      result.push(current);
      current = '';
    } else {
      current += char;
    }
  }

  // Add final field
  result.push(current);

  return result;
}

/**
 * Trigger browser download of a file
 */
export function downloadFile(content: string | Blob, filename: string, mimeType: string) {
  const blob = content instanceof Blob ? content : new Blob([content], { type: mimeType });
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = filename;
  document.body.appendChild(link);
  link.click();
  document.body.removeChild(link);
  URL.revokeObjectURL(url);
}

/**
 * Get appropriate filename for export
 */
export function getExportFilename(context: ExportContext, format: ExportFormat, siteId?: string): string {
  const timestamp = formatDateForInput(new Date());
  const sitePrefix = siteId ? `${siteId}-` : '';
  // `resources:tool` would make an awkward filename segment.
  const safeContext = context.replace(/:/g, '-');
  return `${sitePrefix}${safeContext}-${timestamp}.${format}`;
}
