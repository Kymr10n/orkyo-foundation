import { describe, it, expect } from 'vitest';
import ExcelJS from 'exceljs';
import { columnIndexFromRef, readWorkbook } from './spreadsheet-file';
import { ZipFormatError } from './zip-reader';
import { cellToIsoDate, parseTemplateWorkbook } from './spreadsheet-import';

/**
 * Builds a real .xlsx in memory and wraps it as a File, so the reader is tested
 * against actual exceljs output rather than a hand-made stub of it. Nothing here
 * depends on a checked-in fixture.
 */
async function workbookFile(build: (wb: ExcelJS.Workbook) => void): Promise<File> {
  const wb = new ExcelJS.Workbook();
  build(wb);
  // exceljs hands back a Node Buffer here; a real File.arrayBuffer() always
  // yields an ArrayBuffer, so slice to one rather than letting the test pass a
  // shape the browser never produces.
  const buffer = (await wb.xlsx.writeBuffer()) as unknown as Uint8Array;
  const bytes = buffer.buffer.slice(buffer.byteOffset, buffer.byteOffset + buffer.byteLength);
  return { arrayBuffer: () => Promise.resolve(bytes) } as unknown as File;
}

/**
 * Builds a ZIP with every entry STORED (method 0). exceljs always deflates, so
 * without this the uncompressed branch of the reader would never run in tests.
 * CRC is left zero: the reader does not verify it.
 */
function buildStoredZip(files: Map<string, string>): File {
  const encoder = new TextEncoder();
  const locals: Uint8Array[] = [];
  const centrals: Uint8Array[] = [];
  let offset = 0;

  for (const [name, content] of files) {
    const nameBytes = encoder.encode(name);
    const data = encoder.encode(content);

    const local = new Uint8Array(30 + nameBytes.length + data.length);
    const lv = new DataView(local.buffer);
    lv.setUint32(0, 0x04034b50, true);
    lv.setUint16(8, 0, true); // stored
    lv.setUint32(18, data.length, true); // compressed size
    lv.setUint32(22, data.length, true); // uncompressed size
    lv.setUint16(26, nameBytes.length, true);
    local.set(nameBytes, 30);
    local.set(data, 30 + nameBytes.length);
    locals.push(local);

    const central = new Uint8Array(46 + nameBytes.length);
    const cv = new DataView(central.buffer);
    cv.setUint32(0, 0x02014b50, true);
    cv.setUint16(10, 0, true); // stored
    cv.setUint32(20, data.length, true);
    cv.setUint32(24, data.length, true);
    cv.setUint16(28, nameBytes.length, true);
    cv.setUint32(42, offset, true); // local header offset
    central.set(nameBytes, 46);
    centrals.push(central);

    offset += local.length;
  }

  const centralSize = centrals.reduce((n, c) => n + c.length, 0);
  const eocd = new Uint8Array(22);
  const ev = new DataView(eocd.buffer);
  ev.setUint32(0, 0x06054b50, true);
  ev.setUint16(8, files.size, true);
  ev.setUint16(10, files.size, true);
  ev.setUint32(12, centralSize, true);
  ev.setUint32(16, offset, true);

  const total = offset + centralSize + eocd.length;
  const out = new Uint8Array(total);
  let cursor = 0;
  for (const part of [...locals, ...centrals, eocd]) {
    out.set(part, cursor);
    cursor += part.length;
  }
  return { arrayBuffer: () => Promise.resolve(out.buffer) } as unknown as File;
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

  it('yields dates as Excel serials, which the parser converts', async () => {
    const file = await workbookFile((wb) => {
      wb.addWorksheet('Dates').getCell('A1').value = new Date(Date.UTC(2026, 7, 10));
    });

    const [sheet] = await readWorkbook(file);

    // Reading the XML directly gives the stored serial rather than a Date —
    // cellToIsoDate has always handled both, and the template's own dates
    // arrive this way.
    expect(sheet.rows[0][0]).toBe(46244);
    expect(cellToIsoDate(sheet.rows[0][0])).toBe('2026-08-10');
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

describe('columnIndexFromRef', () => {
  it('decodes base-26 column letters, which have no zero digit', () => {
    expect(columnIndexFromRef('A1')).toBe(0);
    expect(columnIndexFromRef('E6')).toBe(4);
    expect(columnIndexFromRef('Z1')).toBe(25);
    expect(columnIndexFromRef('AA1')).toBe(26);
    // The Schedule sheet runs past AQ, so multi-letter refs are not academic.
    expect(columnIndexFromRef('AQ11')).toBe(42);
  });
});

describe('readWorkbook — file shapes', () => {
  it('places cells by their reference, leaving gaps as holes', async () => {
    const file = await workbookFile((wb) => {
      const ws = wb.addWorksheet('Sparse');
      ws.getCell('A1').value = 'first';
      ws.getCell('D1').value = 'fourth'; // B and C never written
      ws.getCell('A3').value = 'third row'; // row 2 never written
    });

    const [sheet] = await readWorkbook(file);

    expect(sheet.rows[0][0]).toBe('first');
    expect(sheet.rows[0][3]).toBe('fourth');
    expect(sheet.rows[0][1]).toBeUndefined();
    expect(sheet.rows[2][0]).toBe('third row');
  });

  it('reads stored (uncompressed) entries as well as deflated ones', async () => {
    // exceljs always deflates; build a stored-entry workbook by hand so the
    // method-0 branch is covered by something other than inspection.
    const stored = buildStoredZip(
      new Map([
        ['xl/workbook.xml', '<workbook><sheets><sheet name="Only" r:id="rId1"/></sheets></workbook>'],
        [
          'xl/_rels/workbook.xml.rels',
          '<Relationships><Relationship Id="rId1" Target="worksheets/sheet1.xml"/></Relationships>',
        ],
        [
          'xl/worksheets/sheet1.xml',
          '<worksheet><sheetData><row r="1"><c r="A1" t="inlineStr"><is><t>plain</t></is></c></row></sheetData></worksheet>',
        ],
      ]),
    );

    const [sheet] = await readWorkbook(stored);

    expect(sheet.name).toBe('Only');
    expect(sheet.rows[0][0]).toBe('plain');
  });

  it('rejects a file that is not a ZIP with a message a user can act on', async () => {
    const notAZip = {
      arrayBuffer: () => Promise.resolve(new TextEncoder().encode('name,code\nWS-01,Mill').buffer),
    } as unknown as File;

    await expect(readWorkbook(notAZip)).rejects.toBeInstanceOf(ZipFormatError);
    await expect(readWorkbook(notAZip)).rejects.toThrow(/\.xlsx/);
  });
});
