import type { CreateRequestRequest } from '@foundation/src/types/requests';
import type { CreateResourceRequest } from '@foundation/src/lib/api/resources-api';
import { RESOURCE_TYPE_KEY } from '@foundation/src/constants/resource-type-key';

// Parses the published Orkyo capacity-planning workbook (orkyo.com/guides/
// capacity-planning-excel-template/) into create payloads. Kept free of any
// spreadsheet library: the exceljs touchpoint lives in spreadsheet-file.ts and
// hands over neutral SheetData, so everything here is synchronous and testable.
//
// Template layout (both sheets): headers on row 5, data from row 6. Columns
// beyond the input range are derived formulas in the workbook and are ignored.

export type SheetCell = string | number | boolean | Date | null | undefined;

export interface SheetData {
  name: string;
  /** rows[0] is spreadsheet row 1; cells[0] is column A. */
  rows: SheetCell[][];
}

export interface WorkstationRow {
  /** Spreadsheet row number, for error messages. */
  row: number;
  code: string;
  name: string;
  standsThere: string;
  notes: string;
}

export interface JobRow {
  row: number;
  job: string;
  description: string;
  workstationCode: string;
  /** ISO date (yyyy-mm-dd) or null when the cell was empty/unreadable. */
  start: string | null;
  end: string | null;
  hoursPerDay: number | null;
}

export interface ImportRowError {
  sheet: string;
  row: number;
  message: string;
}

export interface ParsedWorkbook {
  workstations: WorkstationRow[];
  jobs: JobRow[];
  errors: ImportRowError[];
}

const WORKSTATIONS_SHEET = 'Workstations';
const JOBS_SHEET = 'Jobs';
const DATA_FIRST_ROW = 6; // 1-based; headers sit on row 5

function text(cell: SheetCell): string {
  if (cell === null || cell === undefined) return '';
  if (cell instanceof Date) return cell.toISOString();
  return String(cell).trim();
}

function number(cell: SheetCell): number | null {
  if (typeof cell === 'number' && Number.isFinite(cell)) return cell;
  const parsed = parseFloat(text(cell));
  return Number.isFinite(parsed) ? parsed : null;
}

/** Excel serial dates count days from 1899-12-30 (the off-by-two lotus legacy). */
const EXCEL_EPOCH_MS = Date.UTC(1899, 11, 30);
const DAY_MS = 24 * 60 * 60 * 1000;

export function cellToIsoDate(cell: SheetCell): string | null {
  if (cell === null || cell === undefined || cell === '') return null;
  if (cell instanceof Date) {
    return Number.isNaN(cell.getTime()) ? null : cell.toISOString().slice(0, 10);
  }
  if (typeof cell === 'number' && Number.isFinite(cell) && cell > 0) {
    return new Date(EXCEL_EPOCH_MS + Math.round(cell) * DAY_MS).toISOString().slice(0, 10);
  }
  const parsed = new Date(text(cell));
  return Number.isNaN(parsed.getTime()) ? null : parsed.toISOString().slice(0, 10);
}

function isBlank(cells: SheetCell[]): boolean {
  return cells.every(c => text(c) === '');
}

export function parseTemplateWorkbook(sheets: SheetData[]): ParsedWorkbook {
  const errors: ImportRowError[] = [];
  const workstations: WorkstationRow[] = [];
  const jobs: JobRow[] = [];

  const wsSheet = sheets.find(s => s.name === WORKSTATIONS_SHEET);
  const jobSheet = sheets.find(s => s.name === JOBS_SHEET);
  if (!wsSheet) {
    errors.push({ sheet: WORKSTATIONS_SHEET, row: 0, message: `Sheet "${WORKSTATIONS_SHEET}" not found — is this the capacity-planning template?` });
  }
  if (!jobSheet) {
    errors.push({ sheet: JOBS_SHEET, row: 0, message: `Sheet "${JOBS_SHEET}" not found — is this the capacity-planning template?` });
  }
  if (!wsSheet || !jobSheet) return { workstations, jobs, errors };

  const seenCodes = new Set<string>();
  for (let i = DATA_FIRST_ROW - 1; i < wsSheet.rows.length; i++) {
    const cells = wsSheet.rows[i] ?? [];
    const input = cells.slice(0, 5); // A–E; F+ are derived formulas
    if (isBlank(input)) continue;
    const row = i + 1;
    const code = text(input[0]);
    const name = text(input[1]);
    if (!code || !name) {
      errors.push({ sheet: WORKSTATIONS_SHEET, row, message: 'Code and Workstation name are both required.' });
      continue;
    }
    if (seenCodes.has(code)) {
      errors.push({ sheet: WORKSTATIONS_SHEET, row, message: `Duplicate code "${code}" — codes must be unique.` });
      continue;
    }
    seenCodes.add(code);
    workstations.push({ row, code, name, standsThere: text(input[2]), notes: text(input[4]) });
  }

  for (let i = DATA_FIRST_ROW - 1; i < jobSheet.rows.length; i++) {
    const cells = jobSheet.rows[i] ?? [];
    const input = cells.slice(0, 6); // A–F; G+ are derived formulas
    if (isBlank(input)) continue;
    const row = i + 1;
    const job = text(input[0]);
    const workstationCode = text(input[2]);
    if (!job) {
      errors.push({ sheet: JOBS_SHEET, row, message: 'Job name is required.' });
      continue;
    }
    if (!workstationCode) {
      errors.push({ sheet: JOBS_SHEET, row, message: `Job "${job}" has no workstation code.` });
      continue;
    }
    if (!seenCodes.has(workstationCode)) {
      errors.push({ sheet: JOBS_SHEET, row, message: `Job "${job}" references unknown workstation "${workstationCode}".` });
      continue;
    }
    const start = cellToIsoDate(input[3]);
    const end = cellToIsoDate(input[4]);
    if (start && end && end < start) {
      errors.push({ sheet: JOBS_SHEET, row, message: `Job "${job}" ends before it starts.` });
      continue;
    }
    jobs.push({ row, job, description: text(input[1]), workstationCode, start, end, hoursPerDay: number(input[5]) });
  }

  return { workstations, jobs, errors };
}

// The template's "Capacity (h/day)" is hours a workstation can absorb; a space's
// `capacity` is how many things fit in it at once. Different quantities — the
// hours column is deliberately not mapped, and the wizard says so.
export function workstationToCreateSpace(row: WorkstationRow, siteId: string): CreateResourceRequest {
  const description = [row.standsThere, row.notes].filter(Boolean).join(' — ');
  return {
    // A workstation is a space. That mapping is the template's meaning, not a behaviour the
    // system infers, so it stays keyed — see the identity/behaviour split in the review notes.
    resourceTypeKey: RESOURCE_TYPE_KEY.SPACE,
    name: row.name,
    code: row.code,
    description: description || undefined,
    allocationMode: 'Exclusive',
    homeSiteId: siteId,
    crossSiteAllowed: false,
    isPhysical: true,
  };
}

function weekdayCount(startIso: string, endIso: string): number {
  const start = new Date(`${startIso}T00:00:00Z`);
  const end = new Date(`${endIso}T00:00:00Z`);
  let count = 0;
  for (let t = start.getTime(); t <= end.getTime(); t += DAY_MS) {
    const day = new Date(t).getUTCDay();
    if (day !== 0 && day !== 6) count++;
  }
  return count;
}

export function jobToCreateRequest(
  job: JobRow,
  codeToResourceId: Map<string, string>,
  siteId: string,
): CreateRequestRequest {
  const resourceId = codeToResourceId.get(job.workstationCode);
  // The template's End column is inclusive (a one-day job has Start = End);
  // request windows are exclusive at the end, so the window runs to the
  // following midnight.
  const startTs = job.start ? `${job.start}T00:00:00Z` : undefined;
  const endTs = job.end
    ? `${new Date(new Date(`${job.end}T00:00:00Z`).getTime() + DAY_MS).toISOString().slice(0, 10)}T00:00:00Z`
    : undefined;
  const workingDays = job.start && job.end ? weekdayCount(job.start, job.end) : 0;
  const totalHours = job.hoursPerDay && workingDays > 0 ? job.hoursPerDay * workingDays : 0;

  return {
    name: job.job,
    description: job.description || undefined,
    siteId,
    resourceIds: resourceId ? [resourceId] : undefined,
    startTs,
    endTs,
    minimalDurationValue: totalHours > 0 ? totalHours : 1,
    minimalDurationUnit: 'hours',
    status: 'new',
  };
}
