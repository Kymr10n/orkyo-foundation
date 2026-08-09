import type { SheetCell, SheetData } from './spreadsheet-import';
import { readZipEntries, ZipFormatError } from './zip-reader';

// Reads the sheets of an .xlsx into the neutral SheetData the parser consumes.
// Deliberately dependency-free — see zip-reader.ts for why.
//
// Scope is reading cell values out of workbooks like our published capacity
// template: shared/inline strings, numbers, booleans, and formula cells (whose
// cached value is the data). Styles, number formats, merged cells and charts are
// ignored. Dates arrive as Excel serial numbers; `cellToIsoDate` in
// spreadsheet-import.ts already converts those.

const WORKBOOK_PATH = 'xl/workbook.xml';
const WORKBOOK_RELS_PATH = 'xl/_rels/workbook.xml.rels';
const SHARED_STRINGS_PATH = 'xl/sharedStrings.xml';

function parseXml(xml: string, what: string): Document {
  const doc = new DOMParser().parseFromString(xml, 'application/xml');
  if (doc.getElementsByTagName('parsererror').length > 0) {
    throw new ZipFormatError(`Could not parse ${what} — the file looks damaged.`);
  }
  return doc;
}

/** "C7" → 2 (zero-based column). Letters are base-26 with no zero digit. */
export function columnIndexFromRef(ref: string): number {
  let index = 0;
  for (const char of ref) {
    const code = char.charCodeAt(0);
    if (code < 65 || code > 90) break; // stop at the row digits
    index = index * 26 + (code - 64);
  }
  return index - 1;
}

/** Concatenated text of a shared-string or inline-string element (`<t>` runs). */
function stringElementText(element: Element): string {
  let text = '';
  for (const run of Array.from(element.getElementsByTagName('t'))) {
    text += run.textContent ?? '';
  }
  return text;
}

function readSharedStrings(entries: Map<string, string>): string[] {
  const xml = entries.get(SHARED_STRINGS_PATH);
  if (!xml) return [];
  const items = Array.from(parseXml(xml, 'shared strings').getElementsByTagName('si'));
  return items.map(stringElementText);
}

/** Sheet name → part path, in the workbook's own order. */
function readSheetIndex(entries: Map<string, string>): { name: string; path: string }[] {
  const workbookXml = entries.get(WORKBOOK_PATH);
  if (!workbookXml) throw new ZipFormatError('No workbook part found — is this really an .xlsx?');

  const relsXml = entries.get(WORKBOOK_RELS_PATH);
  const relTargets = new Map<string, string>();
  if (relsXml) {
    const rels = parseXml(relsXml, 'workbook relationships').getElementsByTagName('Relationship');
    for (const rel of Array.from(rels)) {
      const id = rel.getAttribute('Id');
      const target = rel.getAttribute('Target');
      if (id && target) relTargets.set(id, target.replace(/^\/?xl\//, '').replace(/^\//, ''));
    }
  }

  const sheets = parseXml(workbookXml, 'workbook').getElementsByTagName('sheet');
  const index: { name: string; path: string }[] = [];
  for (let i = 0; i < sheets.length; i++) {
    const name = sheets[i].getAttribute('name');
    if (!name) continue;
    // r:id is namespaced; getAttribute with the prefix works in both DOM impls.
    const rid = sheets[i].getAttribute('r:id') ?? sheets[i].getAttribute('id');
    const target = rid ? relTargets.get(rid) : undefined;
    index.push({ name, path: `xl/${target ?? `worksheets/sheet${i + 1}.xml`}` });
  }
  return index;
}

function cellValue(cell: Element, sharedStrings: string[]): SheetCell {
  const type = cell.getAttribute('t');
  if (type === 'inlineStr') {
    const inline = cell.getElementsByTagName('is')[0];
    return inline ? stringElementText(inline) : '';
  }

  const valueNode = cell.getElementsByTagName('v')[0];
  const raw = valueNode?.textContent ?? '';
  if (raw === '') return null;

  switch (type) {
    case 's': {
      const index = Number(raw);
      return sharedStrings[index] ?? '';
    }
    case 'str': // formula returning text — `raw` is the cached result
      return raw;
    case 'b':
      return raw === '1';
    case 'e': // formula error (#REF!, #N/A); surface it as text rather than a number
      return raw;
    default: {
      const numeric = Number(raw);
      return Number.isNaN(numeric) ? raw : numeric;
    }
  }
}

function readSheet(xml: string, name: string, sharedStrings: string[]): SheetData {
  const rows: SheetCell[][] = [];
  const rowNodes = parseXml(xml, `sheet "${name}"`).getElementsByTagName('row');

  for (let i = 0; i < rowNodes.length; i++) {
    const rowNode = rowNodes[i];
    // `r` is the 1-based spreadsheet row; absent on some writers, so fall back to
    // document order. Rows are placed by number, leaving gaps as holes, because
    // the template addresses data by absolute row (headers on 5, data from 6).
    const rowNumber = Number(rowNode.getAttribute('r')) || i + 1;
    const cells = rowNode.getElementsByTagName('c');
    const values: SheetCell[] = [];
    for (let j = 0; j < cells.length; j++) {
      const ref = cells[j].getAttribute('r');
      const column = ref ? columnIndexFromRef(ref) : j;
      values[column] = cellValue(cells[j], sharedStrings);
    }
    rows[rowNumber - 1] = values;
  }
  return { name, rows };
}

export async function readWorkbook(file: File): Promise<SheetData[]> {
  const entries = await readZipEntries(await file.arrayBuffer());
  const sharedStrings = readSharedStrings(entries);

  return readSheetIndex(entries).map(({ name, path }) => {
    const xml = entries.get(path);
    if (!xml) throw new ZipFormatError(`Sheet "${name}" is missing from the file.`);
    return readSheet(xml, name, sharedStrings);
  });
}
