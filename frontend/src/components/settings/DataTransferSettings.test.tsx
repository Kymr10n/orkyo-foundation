import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { toast } from 'sonner';
import { renderWithQuery } from '@foundation/src/test-utils';
import { mockAuth } from '@foundation/src/test-utils/auth';
import { ApiError } from '@foundation/src/lib/core/api-utils';
import {
  exportTenantArchive,
  importTenantArchive,
  validateTenantArchive,
  type TransferValidationResult,
} from '@foundation/src/lib/api/transfer-api';
import { downloadFile } from '@foundation/src/lib/utils/import-export';
import { DataTransferSettings } from './DataTransferSettings';

vi.mock('@foundation/src/lib/api/transfer-api', () => ({
  exportTenantArchive: vi.fn(),
  validateTenantArchive: vi.fn(),
  importTenantArchive: vi.fn(),
}));
vi.mock('@foundation/src/lib/utils/import-export', () => ({ downloadFile: vi.fn() }));
vi.mock('@foundation/src/contexts/AuthContext', () => ({
  useAuth: () => mockAuth({ membership: { tenantId: 't-1', slug: 'my-org', displayName: 'My Organization' } }),
}));

const archive = new File(['zip'], 'acme.orkyoexport');

const accepted: TransferValidationResult = {
  sourceEdition: 'self-hosted',
  appVersion: '1.9.0',
  exportedAt: '2026-11-10T12:00:00Z',
  sourceTenantSlug: 'acme',
  blockers: [],
  tables: { sites: 2, requests: 40 },
  matchedUsers: 3,
  unmatchedEmails: [],
};

async function chooseArchive() {
  await userEvent.upload(screen.getByLabelText('Archive file'), archive);
}

describe('DataTransferSettings', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(validateTenantArchive).mockResolvedValue(accepted);
  });

  it('downloads the archive under the name the server gives it', async () => {
    const blob = new Blob(['zip']);
    vi.mocked(exportTenantArchive).mockResolvedValue({ blob, fileName: 'orkyo-transfer-my-org.orkyoexport' });
    renderWithQuery(<DataTransferSettings />, { feedback: true });

    await userEvent.click(screen.getByRole('button', { name: 'Download archive' }));

    await waitFor(() =>
      expect(downloadFile).toHaveBeenCalledWith(blob, 'orkyo-transfer-my-org.orkyoexport', 'application/zip'));
    expect(screen.getByText(/The archive is not encrypted/)).toBeInTheDocument();
  });

  it('checks a chosen archive and shows where it came from', async () => {
    renderWithQuery(<DataTransferSettings />, { feedback: true });

    await chooseArchive();

    expect(await screen.findByText('acme (self-hosted, version 1.9.0)')).toBeInTheDocument();
    expect(vi.mocked(validateTenantArchive).mock.calls[0][0]).toBe(archive);
    expect(screen.getByText('42')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Import and replace all data' })).toBeEnabled();
  });

  it('shows the blockers and does not offer the import', async () => {
    vi.mocked(validateTenantArchive).mockResolvedValue({
      ...accepted,
      blockers: [{ code: 'transfer_version_mismatch', message: 'The archive comes from an older schema version.' }],
    });
    renderWithQuery(<DataTransferSettings />, { feedback: true });

    await chooseArchive();

    expect(await screen.findByText('The archive comes from an older schema version.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Import and replace all data' })).toBeDisabled();
  });

  it('names the people an import would turn into placeholders', async () => {
    vi.mocked(validateTenantArchive).mockResolvedValue({ ...accepted, unmatchedEmails: ['left@acme.test'] });
    renderWithQuery(<DataTransferSettings />, { feedback: true });

    await chooseArchive();

    expect(await screen.findByText('left@acme.test')).toBeInTheDocument();
    expect(screen.getByText(/invite them before you import/)).toBeInTheDocument();
  });

  it('imports only after the organization identifier is typed, then offers a reload', async () => {
    vi.mocked(importTenantArchive).mockResolvedValue({ rows: 42, matchedUsers: 3, placeholderUsers: 1 });
    renderWithQuery(<DataTransferSettings />, { feedback: true });
    await chooseArchive();

    await userEvent.click(await screen.findByRole('button', { name: 'Import and replace all data' }));
    const dialog = await screen.findByRole('alertdialog');
    expect(dialog).toHaveTextContent('This erases all current data of My Organization');
    expect(dialog).toHaveTextContent('Other people cannot save changes while the import runs');
    const confirm = screen.getAllByRole('button', { name: 'Import and replace all data' })
      .find((button) => dialog.contains(button))!;
    expect(confirm).toBeDisabled();

    await userEvent.type(screen.getByRole('textbox'), 'my-org');
    await userEvent.click(confirm);

    expect(await screen.findByText(/The import is complete: 42 rows loaded/)).toBeInTheDocument();
    expect(vi.mocked(importTenantArchive).mock.calls[0].slice(0, 2)).toEqual([archive, 'my-org']);
    expect(screen.getByRole('button', { name: 'Reload now' })).toBeInTheDocument();
    expect(screen.queryByLabelText('Archive file')).not.toBeInTheDocument();
  });

  it('reports a refused import with the server reason and keeps the archive chosen', async () => {
    vi.mocked(importTenantArchive).mockRejectedValue(
      new ApiError('Another import or a long-running change holds this organization\'s data.', 409, 'transfer_busy'));
    renderWithQuery(<DataTransferSettings />, { feedback: true });
    await chooseArchive();
    await userEvent.click(await screen.findByRole('button', { name: 'Import and replace all data' }));
    const dialog = await screen.findByRole('alertdialog');
    await userEvent.type(screen.getByRole('textbox'), 'my-org');
    await userEvent.click(screen.getAllByRole('button', { name: 'Import and replace all data' })
      .find((button) => dialog.contains(button))!);

    await waitFor(() => expect(toast.error).toHaveBeenCalledWith('Could not import the archive', {
      description: 'Another import or a long-running change holds this organization\'s data.',
    }));
    expect(screen.getByRole('button', { name: 'Import and replace all data' })).toBeEnabled();
  });

  it('reports an archive the server cannot read', async () => {
    vi.mocked(validateTenantArchive).mockRejectedValue(new ApiError('The file is not a transfer archive.', 422));
    renderWithQuery(<DataTransferSettings />, { feedback: true });

    await chooseArchive();

    await waitFor(() => expect(toast.error).toHaveBeenCalledWith('Could not read the archive', {
      description: 'The file is not a transfer archive.',
    }));
    expect(screen.queryByRole('button', { name: 'Import and replace all data' })).not.toBeInTheDocument();
  });
});
