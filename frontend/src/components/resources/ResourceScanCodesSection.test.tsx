import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useCanEdit } from '@foundation/src/hooks/usePermissions';
import { createTestQueryClient } from '@foundation/src/test-utils';
import { ResourceScanCodesSection } from './ResourceScanCodesSection';

const api = vi.hoisted(() => ({
  getResourceScanCodes: vi.fn(),
  lookupScanCode: vi.fn(),
  linkResourceScanCode: vi.fn(),
  unlinkResourceScanCode: vi.fn(),
}));
vi.mock('@foundation/src/lib/api/resource-scan-codes-api', () => api);

const toast = vi.hoisted(() => ({ success: vi.fn(), error: vi.fn(), info: vi.fn() }));
vi.mock('sonner', () => ({ toast }));

// The camera is the scanner's own concern (QrScannerDialog.test); here a button stands in for a scan.
vi.mock('@foundation/src/components/scan/QrScannerDialog', () => ({
  QrScannerDialog: ({ open, onScan }: { open: boolean; onScan: (code: string) => void }) =>
    open ? <button onClick={() => onScan('STICKER-1')}>Simulate scan</button> : null,
}));

const RESOURCE_ID = 'res-1';

function renderSection() {
  const { wrapper: Wrapper } = createTestQueryClient({ feedback: true });
  return render(
    <Wrapper>
      <ResourceScanCodesSection resourceId={RESOURCE_ID} />
    </Wrapper>,
  );
}

async function scan(user: ReturnType<typeof userEvent.setup>) {
  await user.click(screen.getByRole('button', { name: 'Scan to link' }));
  await user.click(screen.getByRole('button', { name: 'Simulate scan' }));
}

describe('ResourceScanCodesSection', () => {
  beforeEach(() => {
    api.getResourceScanCodes.mockResolvedValue([]);
    api.linkResourceScanCode.mockResolvedValue({ id: 'c1', resourceId: RESOURCE_ID, code: 'STICKER-1', createdAt: '' });
    api.unlinkResourceScanCode.mockResolvedValue(undefined);
  });

  afterEach(() => {
    vi.clearAllMocks();
    vi.mocked(useCanEdit).mockReturnValue(true);
  });

  it('says when no code is linked', async () => {
    renderSection();

    expect(await screen.findByText('No QR code is linked to this resource.')).toBeInTheDocument();
  });

  it('links an unknown code straight away', async () => {
    const user = userEvent.setup();
    api.lookupScanCode.mockResolvedValue({ status: 'unknown' });
    renderSection();

    await scan(user);

    await waitFor(() => expect(api.linkResourceScanCode).toHaveBeenCalledWith(RESOURCE_ID, 'STICKER-1', false));
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith('QR code linked'));
  });

  it('says so when the code is already on this resource', async () => {
    const user = userEvent.setup();
    api.lookupScanCode.mockResolvedValue({
      status: 'linked',
      resource: { id: RESOURCE_ID, name: 'Drill', resourceTypeKey: 'machine', isActive: true },
    });
    renderSection();

    await scan(user);

    await waitFor(() => expect(toast.info).toHaveBeenCalledWith('This QR code is already linked to this resource.'));
    expect(api.linkResourceScanCode).not.toHaveBeenCalled();
  });

  it('moves a code from another resource only after confirmation', async () => {
    const user = userEvent.setup();
    api.lookupScanCode.mockResolvedValue({
      status: 'linked',
      resource: { id: 'res-2', name: 'Old mill', resourceTypeKey: 'machine', isActive: true },
    });
    renderSection();

    await scan(user);
    expect(await screen.findByText('This QR code is linked to "Old mill". Move it to this resource?')).toBeInTheDocument();
    expect(api.linkResourceScanCode).not.toHaveBeenCalled();

    await user.click(screen.getByRole('button', { name: 'Move link' }));

    await waitFor(() => expect(api.linkResourceScanCode).toHaveBeenCalledWith(RESOURCE_ID, 'STICKER-1', true));
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith('QR code moved'));
  });

  it('does not name the owner when its type has scanning off', async () => {
    const user = userEvent.setup();
    api.lookupScanCode.mockResolvedValue({ status: 'type_disabled' });
    renderSection();

    await scan(user);

    expect(await screen.findByText('This QR code is linked to another resource. Move it to this resource?')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Cancel' }));
    expect(api.linkResourceScanCode).not.toHaveBeenCalled();
  });

  it('closes the confirmation and reports a failed move', async () => {
    const user = userEvent.setup();
    api.lookupScanCode.mockResolvedValue({ status: 'type_disabled' });
    api.linkResourceScanCode.mockRejectedValue(new Error('API Error (409): taken'));
    renderSection();

    await scan(user);
    await user.click(await screen.findByRole('button', { name: 'Move link' }));

    await waitFor(() => expect(toast.error).toHaveBeenCalled());
    await waitFor(() => expect(screen.queryByText(/Move it to this resource/)).not.toBeInTheDocument());
  });

  it('reports a lookup that fails', async () => {
    const user = userEvent.setup();
    api.lookupScanCode.mockRejectedValue(new Error('offline'));
    renderSection();

    await scan(user);

    await waitFor(() =>
      expect(toast.error).toHaveBeenCalledWith('The scanned code could not be checked. Try again.', expect.anything()),
    );
    expect(api.linkResourceScanCode).not.toHaveBeenCalled();
  });

  it('lists the codes and removes one', async () => {
    const user = userEvent.setup();
    api.getResourceScanCodes.mockResolvedValue([{ id: 'c1', resourceId: RESOURCE_ID, code: 'STICKER-1', createdAt: '' }]);
    renderSection();

    await user.click(await screen.findByRole('button', { name: 'Remove QR code STICKER-1' }));

    await waitFor(() => expect(api.unlinkResourceScanCode).toHaveBeenCalledWith(RESOURCE_ID, 'c1'));
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith('QR code removed'));
  });

  it('is read-only for a Viewer', async () => {
    vi.mocked(useCanEdit).mockReturnValue(false);
    api.getResourceScanCodes.mockResolvedValue([{ id: 'c1', resourceId: RESOURCE_ID, code: 'STICKER-1', createdAt: '' }]);
    renderSection();

    expect(await screen.findByText('STICKER-1')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Scan to link' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Remove QR code/ })).not.toBeInTheDocument();
  });
});
