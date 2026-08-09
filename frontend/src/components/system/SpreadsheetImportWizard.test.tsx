import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { createFeedbackTestQueryWrapper } from '@foundation/src/test-utils';
import { SpreadsheetImportWizard } from './SpreadsheetImportWizard';
import type { SheetData } from '@foundation/src/lib/utils/spreadsheet-import';

const readWorkbook = vi.fn<(file: File) => Promise<SheetData[]>>();
const getSpaces = vi.fn();
const createSpace = vi.fn();
const createRequest = vi.fn();
let dataExportAvailable = true;

vi.mock('@foundation/src/lib/utils/spreadsheet-file', () => ({
  readWorkbook: (file: File) => readWorkbook(file),
}));
vi.mock('@foundation/src/lib/api/space-api', () => ({
  getSpaces: (...args: unknown[]) => getSpaces(...args),
  createSpace: (...args: unknown[]) => createSpace(...args),
}));
vi.mock('@foundation/src/lib/api/request-api', () => ({
  createRequest: (...args: unknown[]) => createRequest(...args),
}));
vi.mock('@foundation/src/hooks/useDataExportAvailable', () => ({
  useDataExportAvailable: () => dataExportAvailable,
}));
vi.mock('@foundation/src/hooks/useSites', () => ({
  useSites: () => ({ data: [{ id: 'site-1', name: 'Main plant' }] }),
}));
vi.mock('@foundation/src/store/app-store', () => ({
  useAppStore: (selector: (s: { selectedSiteId: string | null }) => unknown) =>
    selector({ selectedSiteId: 'site-1' }),
}));

function sheet(name: string, dataRows: (string | number | null)[][]): SheetData {
  return { name, rows: [[], [], [], [], ['headers'], ...dataRows] };
}

const TEMPLATE: SheetData[] = [
  sheet('Workstations', [
    ['WS-01', 'Mill 1', 'Haas VF-2', 8, ''],
    ['WS-02', 'Lathe', 'Okuma', 8, ''],
  ]),
  sheet('Jobs', [
    ['J-1', 'Brackets', 'WS-01', '2026-08-10', '2026-08-14', 6],
    ['J-2', 'Shafts', 'WS-99', '2026-08-10', '2026-08-11', 4],
  ]),
];

function renderWizard() {
  return render(<SpreadsheetImportWizard open onOpenChange={() => {}} />, {
    wrapper: createFeedbackTestQueryWrapper(),
  });
}

async function pickFileAndReview(user: ReturnType<typeof userEvent.setup>) {
  const file = new File(['x'], 'template.xlsx');
  await user.upload(screen.getByLabelText('Template file'), file);
  await user.click(screen.getByRole('button', { name: 'Review' }));
}

beforeEach(() => {
  dataExportAvailable = true;
  readWorkbook.mockReset().mockResolvedValue(TEMPLATE);
  getSpaces.mockReset().mockResolvedValue([]);
  createSpace.mockReset().mockImplementation((_siteId, req) =>
    Promise.resolve({ id: `space-${req.code}`, code: req.code, name: req.name }),
  );
  createRequest.mockReset().mockResolvedValue({ id: 'req-1' });
});

describe('SpreadsheetImportWizard', () => {
  it('previews counts and lists skipped rows before creating anything', async () => {
    const user = userEvent.setup();
    renderWizard();
    await pickFileAndReview(user);

    expect(await screen.findByText(/2 workstations/)).toBeInTheDocument();
    // The unknown-workstation job is excluded, not imported blindly.
    expect(screen.getByText(/1 jobs/)).toBeInTheDocument();
    expect(screen.getByText(/row 7: Job "J-2" references unknown workstation "WS-99"/)).toBeInTheDocument();
    expect(screen.getByText(/Daily capacity hours are not imported/)).toBeInTheDocument();
    expect(createSpace).not.toHaveBeenCalled();
  });

  it('creates workstations before jobs and reports the result', async () => {
    const user = userEvent.setup();
    renderWizard();
    await pickFileAndReview(user);
    await user.click(await screen.findByRole('button', { name: 'Import' }));

    await waitFor(() => expect(screen.getByText(/Created 2 workstations/)).toBeInTheDocument());
    expect(createSpace).toHaveBeenCalledTimes(2);
    expect(createRequest).toHaveBeenCalledTimes(1);
    // The job's request carries the id of the space created moments earlier.
    expect(createRequest.mock.calls[0][0]).toMatchObject({
      name: 'J-1',
      siteId: 'site-1',
      resourceIds: ['space-WS-01'],
      minimalDurationUnit: 'hours',
    });
    expect(createSpace.mock.invocationCallOrder[0]).toBeLessThan(
      createRequest.mock.invocationCallOrder[0],
    );
  });

  it('reuses a workstation whose code already exists on the site', async () => {
    getSpaces.mockResolvedValue([{ id: 'existing-1', code: 'WS-01', name: 'Mill 1' }]);
    const user = userEvent.setup();
    renderWizard();
    await pickFileAndReview(user);

    expect(await screen.findByText(/1 workstation code already exists/)).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Import' }));

    await waitFor(() => expect(screen.getByText(/Created 1 workstations/)).toBeInTheDocument());
    expect(createSpace).toHaveBeenCalledTimes(1);
    expect(createRequest.mock.calls[0][0].resourceIds).toEqual(['existing-1']);
  });

  it('reports exactly what was created when a create fails mid-import', async () => {
    createSpace
      .mockImplementationOnce((_s, req) => Promise.resolve({ id: 'space-1', code: req.code }))
      .mockRejectedValueOnce(new Error('Quota exceeded'));
    const user = userEvent.setup();
    renderWizard();
    await pickFileAndReview(user);
    await user.click(await screen.findByRole('button', { name: 'Import' }));

    await waitFor(() => expect(screen.getByText(/Import stopped: Quota exceeded/)).toBeInTheDocument());
    expect(screen.getByText(/1 workstations and 0 jobs — they remain in place/)).toBeInTheDocument();
    expect(createRequest).not.toHaveBeenCalled();
  });

  it('shows the upsell instead of the form when the plan lacks data import', async () => {
    dataExportAvailable = false;
    renderWizard();

    expect(screen.getByText('Data import')).toBeInTheDocument();
    expect(screen.queryByLabelText('Template file')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Review' })).not.toBeInTheDocument();
  });
});
