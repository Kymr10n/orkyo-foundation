/**
 * Import/Export utilities for Space Utilization System
 * Implements requirements from requirements_import_export_v1.md
 */

export type ExportFormat = 'csv' | 'json' | 'pdf';
export type ImportFormat = 'csv' | 'json';

export type ExportContext = 
  | 'utilization'      // PDF only - Gantt chart
  | 'spaces'           // CSV - List of spaces
  | 'requests'         // CSV - List of requests
  | 'conflicts'        // CSV - List of conflicts
  | 'criteria'         // CSV/JSON - Criteria definitions
  | 'sites'            // CSV/JSON - Sites
  | 'templates'        // CSV/JSON - Request templates
  | 'users';           // CSV/JSON - User management

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
  const timestamp = new Date().toISOString().split('T')[0];
  const sitePrefix = siteId ? `${siteId}-` : '';
  return `${sitePrefix}${context}-${timestamp}.${format}`;
}

/**
 * Get supported formats for a context
 */
export function getSupportedFormats(context: ExportContext): {
  export: ExportFormat[];
  import: ImportFormat[];
} {
  switch (context) {
    case 'utilization':
      return { export: ['pdf'], import: [] };
    case 'spaces':
    case 'requests':
    case 'conflicts':
      return { export: ['csv'], import: ['csv'] };
    case 'criteria':
    case 'sites':
    case 'templates':
    case 'users':
      return { export: ['csv', 'json'], import: ['csv', 'json'] };
    default:
      return { export: [], import: [] };
  }
}

/**
 * Check if import is supported for a context
 */
export function isImportSupported(context: ExportContext): boolean {
  const formats = getSupportedFormats(context);
  return formats.import.length > 0;
}
