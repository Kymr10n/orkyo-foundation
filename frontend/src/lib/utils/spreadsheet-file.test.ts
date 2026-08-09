import { describe, it, expect } from 'vitest';
import ExcelJS from 'exceljs';
import { readWorkbook } from './spreadsheet-file';
import { parseTemplateWorkbook } from './spreadsheet-import';

/**
 * Builds a real .xlsx in memory and wraps it as a File, so the reader is tested
 * against actual exceljs output rather than a hand-made stub of it. Nothing here
 * depends on a checked-in fixture.
 */
async function workbookFile(build: (wb: ExcelJS.Workbook) => void): Promise<File> {
  const wb = new ExcelJS.Workbook();
  build(wb);
  const buffer = await wb.xlsx.writeBuffer();
  return {
    arrayBuffer: () => Promise.resolve(buffer as ArrayBuffer),
  } as unknown as File;
}

describe('readWorkbook', () => {
  it('returns every sheet with 0-based columns and 0-based rows', async () => {
    const file = await workbookFile((wb) => {
      const a = wb.addWorksheet('First');
      a.getCell('A1').value = 'top-left';
      a.getCell('C2').value = 'c-two';
      wb.addWorksheet('Second');
    });

    const sheets = await readWorkbook(file);

    expect(sheets.map((s) => s.name)).toEqual(['First', 'Second']);
    // rows[0] is spreadsheet row 1, cells[0] is column A.
    expect(sheets[0].rows[0][0]).toBe('top-left');
    expect(sheets[0].rows[1][2]).toBe('c-two');
  });

  it('unwraps the cached result of a formula cell rather than the formula itself', async () => {
    const file = await workbookFile((wb) => {
      const ws = wb.addWorksheet('Calc');
      ws.getCell('A1').value = 2;
      ws.getCell('B1').value = { formula: 'A1*3', result: 6 };
    });

    const [sheet] = await readWorkbook(file);

    // The template's derived columns are formula cells; the cached result is the data.
    expect(sheet.rows[0][1]).toBe(6);
  });

  it('flattens rich text to a plain string', async () => {
    const file = await workbookFile((wb) => {
      const ws = wb.addWorksheet('Rich');
      ws.getCell('A1').value = {
        richText: [{ text: 'Mill ' }, { text: '1' }],
      } as unknown as ExcelJS.CellValue;
    });

    const [sheet] = await readWorkbook(file);

    expect(sheet.rows[0][0]).toBe('Mill 1');
  });

  it('passes dates through as Date, so serial conversion is never needed', async () => {
    const when = new Date(Date.UTC(2026, 7, 10));
    const file = await workbookFile((wb) => {
      wb.addWorksheet('Dates').getCell('A1').value = when;
    });

    const [sheet] = await readWorkbook(file);

    expect(sheet.rows[0][0]).toBeInstanceOf(Date);
    expect((sheet.rows[0][0] as Date).toISOString().slice(0, 10)).toBe('2026-08-10');
  });

  it('reads a template-shaped workbook end to end', async () => {
    const file = await workbookFile((wb) => {
      const ws = wb.addWorksheet('Workstations');
      ws.getRow(5).values = ['Code', 'Workstation', 'What stands there', 'Capacity (h/day)', 'Notes'];
      ws.getRow(6).values = ['WS-01', 'Mill 1', 'Haas VF-2', 8, 'Service Fri'];
      const jobs = wb.addWorksheet('Jobs');
      jobs.getRow(5).values = ['Job', 'Description', 'Workstation', 'Start', 'End', 'Hours/day'];
      jobs.getRow(6).values = [
        'J-1041',
        'Brackets',
        'WS-01',
        new Date(Date.UTC(2026, 7, 10)),
        new Date(Date.UTC(2026, 7, 14)),
        6,
      ];
    });

    const parsed = parseTemplateWorkbook(await readWorkbook(file));

    expect(parsed.errors).toEqual([]);
    expect(parsed.workstations[0]).toMatchObject({ code: 'WS-01', name: 'Mill 1', row: 6 });
    expect(parsed.jobs[0]).toMatchObject({
      job: 'J-1041',
      workstationCode: 'WS-01',
      start: '2026-08-10',
      end: '2026-08-14',
      hoursPerDay: 6,
    });
  });
});
