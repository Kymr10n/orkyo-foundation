import type { SheetCell, SheetData } from './spreadsheet-import';

// The only exceljs touchpoint. Dynamically imported (the jspdf precedent) so the
// library stays out of the initial bundle — it is only paid for when someone
// actually imports a spreadsheet.

type ExcelCellValue = SheetCell | { result?: SheetCell; richText?: { text: string }[]; text?: string };

function plainValue(value: ExcelCellValue): SheetCell {
  if (value && typeof value === 'object' && !(value instanceof Date)) {
    // Formula cells carry { formula, result }; the cached result is the data.
    if ('result' in value) return (value.result ?? null) as SheetCell;
    if ('richText' in value && Array.isArray(value.richText)) {
      return value.richText.map(t => t.text).join('');
    }
    if ('text' in value) return value.text as SheetCell;
    return null;
  }
  return value as SheetCell;
}

export async function readWorkbook(file: File): Promise<SheetData[]> {
  const { default: ExcelJS } = await import('exceljs');
  const workbook = new ExcelJS.Workbook();
  await workbook.xlsx.load(await file.arrayBuffer());

  return workbook.worksheets.map(worksheet => {
    const rows: SheetCell[][] = [];
    worksheet.eachRow({ includeEmpty: true }, (row, rowNumber) => {
      // row.values is 1-based with an empty slot 0; normalize to 0-based columns.
      const values = (row.values ?? []) as ExcelCellValue[];
      rows[rowNumber - 1] = values.slice(1).map(plainValue);
    });
    return { name: worksheet.name, rows };
  });
}
