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
 * A spreadsheet reads a cell that starts with one of these as a formula. Exported text cells
 * that start with one get a leading `'`, which Excel and LibreOffice treat as "text, not a
 * formula"; `csvToArray` strips it again, so an export re-imports unchanged.
 */
const FORMULA_TRIGGERS = /^[=+\-@\t\r]/;
const NEUTRALISED = /^'[=+\-@\t\r]/;

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

      // Handle strings with commas, quotes, or line breaks
      const stringValue = typeof value === 'string'
        ? (FORMULA_TRIGGERS.test(value) ? `'${value}` : value)
        : typeof value === 'number' || typeof value === 'boolean' ? String(value)
        : JSON.stringify(value);
      if (/[,"\n\r]/.test(stringValue)) {
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
  const records = parseCSV(csv);
  if (records.length === 0) return [];

  // Parse headers from first record or use provided
  const csvHeaders = headers || records[0];
  const dataRecords = headers ? records : records.slice(1);

  return dataRecords.map(values => {
    const obj: Record<string, string> = {};

    csvHeaders.forEach((header, index) => {
      const value = values[index] || '';
      obj[header] = NEUTRALISED.test(value) ? value.slice(1) : value;
    });

    return obj as T;
  });
}

/**
 * Split CSV text into records of fields, one character at a time: a quoted field may hold
 * commas, doubled quotes and line breaks, and records end at LF or CRLF. Blank records are
 * dropped.
 */
function parseCSV(csv: string): string[][] {
  const records: string[][] = [];
  let record: string[] = [];
  let field = '';
  let inQuotes = false;

  const endRecord = () => {
    record.push(field);
    if (record.some(value => value.trim())) records.push(record);
    record = [];
    field = '';
  };

  for (let i = 0; i < csv.length; i++) {
    const char = csv[i];

    if (inQuotes) {
      if (char === '"' && csv[i + 1] === '"') {
        field += '"'; // Escaped quote
        i++;
      } else if (char === '"') {
        inQuotes = false;
      } else {
        field += char;
      }
    } else if (char === '"') {
      inQuotes = true;
    } else if (char === ',') {
      record.push(field);
      field = '';
    } else if (char === '\n' || char === '\r') {
      if (char === '\r' && csv[i + 1] === '\n') i++;
      endRecord();
    } else {
      field += char;
    }
  }
  endRecord();

  return records;
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
