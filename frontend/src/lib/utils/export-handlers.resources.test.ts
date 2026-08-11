import { describe, it, expect, vi, beforeEach } from 'vitest';
import { exportResources, importResources, type ResourceExportRow } from './export-handlers';

const downloaded: { content: string; filename: string }[] = [];
vi.mock('./import-export', async (importOriginal) => {
  const actual = await importOriginal<Record<string, unknown>>();
  return {
    ...actual,
    downloadFile: (content: string, filename: string) => downloaded.push({ content, filename }),
  };
});

function makeResource(overrides: Partial<ResourceExportRow> = {}): ResourceExportRow {
  return {
    id: 'res-1',
    resourceTypeId: 'type-1',
    resourceTypeKey: 'tool',
    name: 'Forklift 3',
    description: 'Yard unit',
    allocationMode: 'exclusive',
    baseAvailabilityPercent: 100,
    isActive: true,
    homeSiteId: 'site-1',
    crossSiteAllowed: true,
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
    ...overrides,
  } as ResourceExportRow;
}

/** CSV text → array of row objects, so assertions read the file the way a user's spreadsheet would. */
function parseCsv(text: string): { headers: string[]; rows: string[][] } {
  const [headerLine, ...lines] = text.trim().split('\n');
  return {
    headers: headerLine.split(','),
    rows: lines.map((l) => l.split(',')),
  };
}

function fileOf(content: string) {
  return { text: () => Promise.resolve(content) } as unknown as File;
}

beforeEach(() => {
  downloaded.length = 0;
});

describe('exportResources', () => {
  it('emits core columns in snake_case and omits bookkeeping fields', async () => {
    await exportResources([makeResource()], 'csv', 'tool');

    const { headers } = parseCsv(downloaded[0].content);
    expect(headers).toContain('name');
    expect(headers).toContain('base_availability_percent');
    expect(headers).toContain('home_site_id');
    // Internal plumbing has no business in a user-facing spreadsheet.
    expect(headers).not.toContain('resource_type_id');
    expect(headers).not.toContain('created_at');
  });

  it('flattens a type’s own fields under meta.', async () => {
    await exportResources(
      [makeResource({ customFields: { capacityKg: 1200, fuel: 'diesel' } })],
      'csv',
      'tool',
    );

    const { headers } = parseCsv(downloaded[0].content);
    expect(headers).toContain('meta.capacityKg');
    expect(headers).toContain('meta.fuel');
  });

  it('unions headers so a row missing a custom field cannot truncate the file', async () => {
    await exportResources(
      [
        makeResource({ id: 'a', customFields: { fuel: 'diesel' } }),
        makeResource({ id: 'b', customFields: { capacityKg: 900 } }),
      ],
      'csv',
      'tool',
    );

    const { headers } = parseCsv(downloaded[0].content);
    expect(headers).toContain('meta.fuel');
    expect(headers).toContain('meta.capacityKg');
  });

  it('carries caller-supplied extra columns (a person’s profile fields)', async () => {
    await exportResources(
      [makeResource({ resourceTypeKey: 'person', email: 'ada@example.com', job_title: 'Operator' })],
      'csv',
      'person',
    );

    const { headers, rows } = parseCsv(downloaded[0].content);
    expect(headers).toContain('email');
    expect(rows[0][headers.indexOf('email')]).toBe('ada@example.com');
  });

  it('names the file after the type without the context separator', async () => {
    await exportResources([makeResource()], 'json', 'tool');
    expect(downloaded[0].filename).toMatch(/^resources-tool-.*\.json$/);
  });
});

describe('importResources', () => {
  it('maps columns back to a create-request for the given type', async () => {
    const csv = [
      'name,description,allocation_mode,base_availability_percent,home_site_id,cross_site_allowed',
      'Forklift 3,Yard unit,exclusive,80,site-1,false',
    ].join('\n');

    const rows = await importResources(fileOf(csv), 'csv', 'tool');

    expect(rows).toHaveLength(1);
    expect(rows[0].request).toEqual({
      resourceTypeKey: 'tool',
      name: 'Forklift 3',
      description: 'Yard unit',
      allocationMode: 'exclusive',
      baseAvailabilityPercent: 80,
      homeSiteId: 'site-1',
      crossSiteAllowed: false,
    });
  });

  it('folds meta. columns back into the custom-field document', async () => {
    const csv = 'name,meta.fuel,meta.capacityKg\nForklift 3,diesel,1200';
    const [row] = await importResources(fileOf(csv), 'csv', 'tool');
    expect(row.request.customFields).toEqual({ fuel: 'diesel', capacityKg: '1200' });
  });

  it('reads a cell back as the type its field declares', async () => {
    // Every CSV cell is a string, so without the declared types a number field's column would
    // be rejected by the server and a file this module exported could not be re-imported.
    const csv = 'name,meta.capacity_kg,meta.certified,meta.serial\nForklift 3,1200,true,00420';
    const [row] = await importResources(fileOf(csv), 'csv', 'tool', {
      capacity_kg: 'number',
      certified: 'boolean',
      serial: 'text',
    });

    expect(row.request.customFields).toEqual({
      capacity_kg: 1200,
      certified: true,
      // An all-digit serial is text, and stays text — the declared type decides, not the shape.
      serial: '00420',
    });
  });

  it('passes an unparseable cell through for the server to reject by name', async () => {
    const csv = 'name,meta.capacity_kg\nForklift 3,heavy';
    const [row] = await importResources(fileOf(csv), 'csv', 'tool', { capacity_kg: 'number' });

    expect(row.request.customFields).toEqual({ capacity_kg: 'heavy' });
  });

  it('skips a meta. value that is not a scalar', async () => {
    const json = JSON.stringify({
      data: [{ name: 'Forklift 3', 'meta.notes': { nested: 'object' }, 'meta.tags': ['a'] }],
    });
    const [row] = await importResources(fileOf(json), 'json', 'tool');

    expect(row.request.customFields).toBeUndefined();
  });

  it('drops the id — an import creates, it never overwrites', async () => {
    const csv = 'id,name\nres-1,Forklift 3';
    const [row] = await importResources(fileOf(csv), 'csv', 'tool');
    expect(row.request).not.toHaveProperty('id');
  });

  it('keeps unmapped columns on source so a page can use its own fields', async () => {
    const csv = 'name,email\nAda Heaney,ada@example.com';
    const [row] = await importResources(fileOf(csv), 'csv', 'person');
    expect(row.source.email).toBe('ada@example.com');
  });

  it('skips rows without a name rather than creating blanks', async () => {
    const csv = 'name,description\n,Orphan row\nForklift 3,Real';
    const rows = await importResources(fileOf(csv), 'csv', 'tool');
    expect(rows.map((r) => r.request.name)).toEqual(['Forklift 3']);
  });

  it('reads the JSON export envelope as well as a bare array', async () => {
    const envelope = JSON.stringify({ context: 'resources:tool', data: [{ name: 'Forklift 3' }] });
    expect(await importResources(fileOf(envelope), 'json', 'tool')).toHaveLength(1);

    const bare = JSON.stringify([{ name: 'Forklift 4' }]);
    expect(await importResources(fileOf(bare), 'json', 'tool')).toHaveLength(1);
  });

  it('defaults allocation mode when the column is absent', async () => {
    const [row] = await importResources(fileOf('name\nForklift 3'), 'csv', 'tool');
    expect(row.request.allocationMode).toBe('exclusive');
  });
});

describe('resource export/import round trip', () => {
  it('preserves the fields an import can restore', async () => {
    const original = makeResource({ customFields: { fuel: 'diesel' } });
    await exportResources([original], 'csv', 'tool');

    const [row] = await importResources(fileOf(downloaded[0].content), 'csv', 'tool');

    expect(row.request.name).toBe(original.name);
    expect(row.request.description).toBe(original.description);
    expect(row.request.homeSiteId).toBe(original.homeSiteId);
    expect(row.request.customFields).toEqual({ fuel: 'diesel' });
  });

  it('round-trips a number field through CSV', async () => {
    const original = makeResource({ customFields: { capacity_kg: 1200 } });
    await exportResources([original], 'csv', 'tool');

    const [row] = await importResources(fileOf(downloaded[0].content), 'csv', 'tool', {
      capacity_kg: 'number',
    });

    expect(row.request.customFields).toEqual({ capacity_kg: 1200 });
  });
});
