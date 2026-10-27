import { describe, it, expect } from 'vitest';
import writeXlsxFile, { type Sheet } from 'write-excel-file/universal';
import { columnIndexFromRef, readWorkbook } from './spreadsheet-file';
import { ZipFormatError } from './zip-reader';
import { cellToIsoDate, parseTemplateWorkbook } from './spreadsheet-import';

/**
 * Builds a real .xlsx in memory with write-excel-file and wraps it as a File, so
 * the reader is tested against a real writer's output (deflated entries, shared
 * strings, styles) rather than a hand-made stub of it. Nothing here depends on a
 * checked-in fixture.
 */
async function workbookFile(sheets: Sheet<never>[]): Promise<File> {
  const bytes = await (await writeXlsxFile(sheets).toBlob()).arrayBuffer();
  return { arrayBuffer: () => Promise.resolve(bytes) } as unknown as File;
}

/**
 * Builds a ZIP with every entry STORED (method 0). write-excel-file always
 * deflates, so without this the uncompressed branch of the reader would never run
 * in tests. CRC is left zero: the reader does not verify it.
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

/**
 * One-sheet workbook from hand-written sheet XML, for cell shapes write-excel-file
 * cannot produce: a formula with its cached result, and rich text.
 */
function storedWorkbook(sheetName: string, sheetDataXml: string): File {
  return buildStoredZip(
    new Map([
      ['xl/workbook.xml', `<workbook><sheets><sheet name="${sheetName}" r:id="rId1"/></sheets></workbook>`],
      [
        'xl/_rels/workbook.xml.rels',
        '<Relationships><Relationship Id="rId1" Target="worksheets/sheet1.xml"/></Relationships>',
      ],
      ['xl/worksheets/sheet1.xml', `<worksheet><sheetData>${sheetDataXml}</sheetData></worksheet>`],
    ]),
  );
}

describe('readWorkbook', () => {
  it('returns every sheet with 0-based columns and 0-based rows', async () => {
    const file = await workbookFile([
      { sheet: 'First', data: [['top-left'], [null, null, 'c-two']] },
      { sheet: 'Second', data: [] },
    ]);

    const sheets = await readWorkbook(file);

    expect(sheets.map((s) => s.name)).toEqual(['First', 'Second']);
    // rows[0] is spreadsheet row 1, cells[0] is column A.
    expect(sheets[0].rows[0][0]).toBe('top-left');
    expect(sheets[0].rows[1][2]).toBe('c-two');
  });

  it('unwraps the cached result of a formula cell rather than the formula itself', async () => {
    // Excel stores the last computed value next to the formula; that is what Excel
    // and LibreOffice write, so the cell is hand-built here.
    const file = storedWorkbook('Calc', '<row r="1"><c r="A1"><v>2</v></c><c r="B1"><f>A1*3</f><v>6</v></c></row>');

    const [sheet] = await readWorkbook(file);

    // The template's derived columns are formula cells; the cached result is the data.
    expect(sheet.rows[0][1]).toBe(6);
  });

  it('flattens rich text to a plain string', async () => {
    // Two formatted runs in one cell. The reader joins <t> runs the same way for
    // inline and shared strings.
    const file = storedWorkbook(
      'Rich',
      '<row r="1"><c r="A1" t="inlineStr"><is><r><t xml:space="preserve">Mill </t></r><r><t>1</t></r></is></c></row>',
    );

    const [sheet] = await readWorkbook(file);

    expect(sheet.rows[0][0]).toBe('Mill 1');
  });

  it('yields dates as Excel serials, which the parser converts', async () => {
    const file = await workbookFile([
      { sheet: 'Dates', data: [[{ value: new Date(Date.UTC(2026, 7, 10)), format: 'yyyy-mm-dd' }]] },
    ]);

    const [sheet] = await readWorkbook(file);

    // Reading the XML directly gives the stored serial rather than a Date —
    // cellToIsoDate has always handled both, and the template's own dates
    // arrive this way.
    expect(sheet.rows[0][0]).toBe(46244);
    expect(cellToIsoDate(sheet.rows[0][0])).toBe('2026-08-10');
  });

  it('reads a template-shaped workbook end to end', async () => {
    // The template's headers sit on row 5, so rows 1-4 are empty.
    const date = (value: Date) => ({ value, format: 'yyyy-mm-dd' });
    const file = await workbookFile([
      {
        sheet: 'Workstations',
        data: [
          [], [], [], [],
          ['Code', 'Workstation', 'What stands there', 'Capacity (h/day)', 'Notes'],
          ['WS-01', 'Mill 1', 'Haas VF-2', 8, 'Service Fri'],
        ],
      },
      {
        sheet: 'Jobs',
        data: [
          [], [], [], [],
          ['Job', 'Description', 'Workstation', 'Start', 'End', 'Hours/day'],
          [
            'J-1041',
            'Brackets',
            'WS-01',
            date(new Date(Date.UTC(2026, 7, 10))),
            date(new Date(Date.UTC(2026, 7, 14))),
            6,
          ],
        ],
      },
    ]);

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
    const file = await workbookFile([
      {
        sheet: 'Sparse',
        // B1, C1 and all of row 2 are never written.
        data: [['first', null, null, 'fourth'], [], ['third row']],
      },
    ]);

    const [sheet] = await readWorkbook(file);

    expect(sheet.rows[0][0]).toBe('first');
    expect(sheet.rows[0][3]).toBe('fourth');
    expect(sheet.rows[0][1]).toBeUndefined();
    expect(sheet.rows[2][0]).toBe('third row');
  });

  it('reads stored (uncompressed) entries as well as deflated ones', async () => {
    // write-excel-file always deflates; build a stored-entry workbook by hand so
    // the method-0 branch is covered by something other than inspection.
    const stored = buildStoredZip(
      new Map([
        // Some writers add folder entries; the reader must skip them, not parse them.
        ['xl/', ''],
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
