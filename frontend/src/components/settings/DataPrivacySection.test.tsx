import { describe, it, expect, vi, beforeEach } from 'vitest';
import { screen, fireEvent, waitFor } from '@testing-library/react';
import { DataPrivacySection } from './DataPrivacySection';
import { deleteOwnAccount, exportPersonalData } from '@foundation/src/lib/api/tenant-account-api';
import { downloadFile } from '@foundation/src/lib/utils/import-export';
import { goToApex } from '@foundation/src/lib/utils/tenant-navigation';
import { renderWithQuery } from '@foundation/src/test-utils';

vi.mock('@foundation/src/lib/api/tenant-account-api', () => ({
  exportPersonalData: vi.fn(),
  deleteOwnAccount: vi.fn(),
}));
vi.mock('@foundation/src/lib/utils/import-export', () => ({ downloadFile: vi.fn() }));
vi.mock('@foundation/src/lib/utils/tenant-navigation', () => ({ goToApex: vi.fn() }));

const EMAIL = 'alex@example.com';

function renderSection(props: { locked?: boolean } = {}) {
  return renderWithQuery(<DataPrivacySection email={EMAIL} {...props} />, { feedback: true });
}

async function openDeleteDialog() {
  fireEvent.click(screen.getByRole('button', { name: /^delete$/i }));
  const dialog = await screen.findByRole('alertdialog');
  return dialog;
}

describe('DataPrivacySection', () => {
  beforeEach(() => {
    vi.mocked(exportPersonalData).mockResolvedValue({ schemaVersion: '1.0' });
    vi.mocked(deleteOwnAccount).mockResolvedValue(undefined);
  });

  it('downloads the export as a dated JSON file', async () => {
    renderSection();

    fireEvent.click(screen.getByRole('button', { name: /download/i }));

    await waitFor(() => expect(downloadFile).toHaveBeenCalledTimes(1));
    const [content, filename, mime] = vi.mocked(downloadFile).mock.calls[0];
    expect(JSON.parse(content as string)).toEqual({ schemaVersion: '1.0' });
    expect(filename).toMatch(/^orkyo-personal-data-\d{4}-\d{2}-\d{2}\.json$/);
    expect(mime).toBe('application/json');
  });

  it('hides the delete affordance for a locked shared account', () => {
    renderSection({ locked: true });

    expect(screen.getByRole('button', { name: /download/i })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /^delete$/i })).not.toBeInTheDocument();
  });

  it('keeps the confirm disabled until the email is typed, then deletes and leaves for the apex', async () => {
    renderSection();
    await openDeleteDialog();

    const confirm = screen.getByRole('button', { name: /delete my account/i });
    expect(confirm).toBeDisabled();

    fireEvent.change(screen.getByRole('textbox'), { target: { value: EMAIL } });
    expect(confirm).toBeEnabled();
    fireEvent.click(confirm);

    await waitFor(() => expect(deleteOwnAccount).toHaveBeenCalledWith(EMAIL));
    await waitFor(() => expect(goToApex).toHaveBeenCalledWith('/'));
  });

  it("shows the server's refusal inside the dialog and stays open", async () => {
    vi.mocked(deleteOwnAccount).mockRejectedValue(
      new Error('You own ACME. Delete the organization or transfer ownership first.'),
    );
    renderSection();
    await openDeleteDialog();

    fireEvent.change(screen.getByRole('textbox'), { target: { value: EMAIL } });
    fireEvent.click(screen.getByRole('button', { name: /delete my account/i }));

    expect(await screen.findByText(/You own ACME/)).toBeInTheDocument();
    expect(screen.getByRole('alertdialog')).toBeInTheDocument();
    expect(goToApex).not.toHaveBeenCalled();
  });
});
