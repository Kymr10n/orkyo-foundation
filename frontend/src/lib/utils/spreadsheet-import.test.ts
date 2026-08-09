import { describe, it, expect } from 'vitest';
import {
  cellToIsoDate,
  jobToCreateRequest,
  parseTemplateWorkbook,
  workstationToCreateSpace,
  type SheetData,
} from './spreadsheet-import';

/** Builds a sheet with the template's layout: rows 1–4 chrome, headers on 5, data from 6. */
function sheet(name: string, dataRows: (string | number | Date | null)[][]): SheetData {
  return {
    name,
    rows: [[], [], [], [], ['headers'], ...dataRows],
  };
}

const WORKSTATIONS = sheet('Workstations', [
  ['WS-01', 'Mill 1', 'Haas VF-2', 8, 'Service Fri'],
  ['WS-02', 'Assembly bay', 'Floor area, 40 m²', 24, ''],
]);

function jobsSheet(rows: (string | number | Date | null)[][]) {
  return sheet('Jobs', rows);
}

describe('parseTemplateWorkbook', () => {
  it('reads workstations and jobs from row 6, ignoring derived columns', () => {
    const parsed = parseTemplateWorkbook([
      {
        name: 'Workstations',
        rows: [[], [], [], [], ['h'], ['WS-01', 'Mill 1', 'Haas VF-2', 8, 'notes', 'DERIVED-F', 'DERIVED-G']],
      },
      jobsSheet([['J-1', 'Brackets', 'WS-01', '2026-08-10', '2026-08-14', 6, 'DERIVED-G']]),
    ]);

    expect(parsed.errors).toEqual([]);
    expect(parsed.workstations).toHaveLength(1);
    expect(parsed.workstations[0]).toMatchObject({ row: 6, code: 'WS-01', name: 'Mill 1', standsThere: 'Haas VF-2', notes: 'notes' });
    expect(parsed.jobs[0]).toMatchObject({ job: 'J-1', workstationCode: 'WS-01', start: '2026-08-10', end: '2026-08-14', hoursPerDay: 6 });
  });

  it('skips blank rows and reports missing sheets', () => {
    const parsed = parseTemplateWorkbook([sheet('Workstations', [[null, '', null, null, '']])]);
    expect(parsed.workstations).toHaveLength(0);
    expect(parsed.errors).toHaveLength(1);
    expect(parsed.errors[0].message).toContain('"Jobs" not found');
  });

  it('rejects jobs referencing unknown workstation codes, mirroring the template check', () => {
    const parsed = parseTemplateWorkbook([WORKSTATIONS, jobsSheet([['J-1', '', 'WS-99', '2026-08-10', '2026-08-11', 4]])]);
    expect(parsed.jobs).toHaveLength(0);
    expect(parsed.errors[0]).toMatchObject({ sheet: 'Jobs', row: 6 });
    expect(parsed.errors[0].message).toContain('WS-99');
  });

  it('reports a job row that is missing its name or its workstation code', () => {
    const parsed = parseTemplateWorkbook([
      WORKSTATIONS,
      jobsSheet([
        [null, 'no job name', 'WS-01', '2026-08-10', '2026-08-11', 4],
        ['J-2', 'no workstation', '', '2026-08-10', '2026-08-11', 4],
      ]),
    ]);

    expect(parsed.jobs).toHaveLength(0);
    expect(parsed.errors).toHaveLength(2);
    expect(parsed.errors[0]).toMatchObject({ sheet: 'Jobs', row: 6, message: 'Job name is required.' });
    expect(parsed.errors[1].message).toContain('has no workstation code');
  });

  it('reports a workstation row missing its code or name', () => {
    const parsed = parseTemplateWorkbook([
      sheet('Workstations', [
        ['', 'Nameless code', '', 8, ''],
        ['WS-09', '', '', 8, ''],
      ]),
      jobsSheet([]),
    ]);

    expect(parsed.workstations).toHaveLength(0);
    expect(parsed.errors).toHaveLength(2);
    for (const error of parsed.errors) {
      expect(error.message).toContain('both required');
    }
  });

  it('rejects duplicate codes and end-before-start windows', () => {
    const parsed = parseTemplateWorkbook([
      sheet('Workstations', [
        ['WS-01', 'Mill 1', '', 8, ''],
        ['WS-01', 'Mill 1 again', '', 8, ''],
      ]),
      jobsSheet([['J-1', '', 'WS-01', '2026-08-14', '2026-08-10', 4]]),
    ]);
    expect(parsed.workstations).toHaveLength(1);
    expect(parsed.errors.map(e => e.message).join(' ')).toContain('Duplicate code');
    expect(parsed.errors.map(e => e.message).join(' ')).toContain('ends before it starts');
  });
});

describe('cellToIsoDate', () => {
  it('converts Excel serials from the 1899-12-30 epoch', () => {
    // 2026-08-10 is serial 46244 (verified against the shipped template).
    expect(cellToIsoDate(46244)).toBe('2026-08-10');
  });

  it('handles Date cells and ISO strings, and returns null for junk', () => {
    expect(cellToIsoDate(new Date(Date.UTC(2026, 7, 10)))).toBe('2026-08-10');
    expect(cellToIsoDate('2026-08-10')).toBe('2026-08-10');
    expect(cellToIsoDate('not a date')).toBeNull();
    expect(cellToIsoDate('')).toBeNull();
    expect(cellToIsoDate(null)).toBeNull();
  });
});

describe('workstationToCreateSpace', () => {
  it('maps name/code/description and never the hours-per-day capacity', () => {
    const space = workstationToCreateSpace({ row: 6, code: 'WS-01', name: 'Mill 1', standsThere: 'Haas VF-2', notes: 'Service Fri' });
    expect(space).toEqual({
      name: 'Mill 1',
      code: 'WS-01',
      description: 'Haas VF-2 — Service Fri',
      isPhysical: true,
    });
    expect('capacity' in space).toBe(false);
  });
});

describe('jobToCreateRequest', () => {
  const codeMap = new Map([['WS-01', 'resource-abc']]);

  it('builds a complete create payload with weekday-hours duration and exclusive end', () => {
    const req = jobToCreateRequest(
      { row: 6, job: 'J-1', description: 'Brackets', workstationCode: 'WS-01', start: '2026-08-10', end: '2026-08-14', hoursPerDay: 6 },
      codeMap,
      'site-1',
    );
    // Mon 10th – Fri 14th = 5 weekdays × 6 h.
    expect(req.minimalDurationValue).toBe(30);
    expect(req.minimalDurationUnit).toBe('hours');
    expect(req.startTs).toBe('2026-08-10T00:00:00Z');
    // Template End is inclusive; the request window ends the following midnight.
    expect(req.endTs).toBe('2026-08-15T00:00:00Z');
    expect(req.resourceIds).toEqual(['resource-abc']);
    expect(req.siteId).toBe('site-1');
    expect(req.status).toBe('new');
  });

  it('spans weekends correctly and falls back to 1 hour without dates', () => {
    const spanning = jobToCreateRequest(
      { row: 7, job: 'J-2', description: '', workstationCode: 'WS-01', start: '2026-08-14', end: '2026-08-17', hoursPerDay: 8 },
      codeMap,
      'site-1',
    );
    // Fri 14th + Mon 17th = 2 weekdays × 8 h; Sat/Sun excluded.
    expect(spanning.minimalDurationValue).toBe(16);

    const dateless = jobToCreateRequest(
      { row: 8, job: 'J-3', description: '', workstationCode: 'WS-01', start: null, end: null, hoursPerDay: 8 },
      codeMap,
      'site-1',
    );
    expect(dateless.minimalDurationValue).toBe(1);
    expect(dateless.startTs).toBeUndefined();
  });
});
