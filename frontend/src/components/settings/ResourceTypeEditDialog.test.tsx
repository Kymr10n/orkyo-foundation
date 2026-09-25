import { afterEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { createTestQueryClient } from '@foundation/src/test-utils';
import { machineResourceType } from '@foundation/src/test-utils/resource-fixtures';
import { ResourceTypeEditDialog } from './ResourceTypeEditDialog';

const api = vi.hoisted(() => ({ createResourceType: vi.fn(), updateResourceType: vi.fn() }));
vi.mock('@foundation/src/lib/api/resource-types-api', () => api);
vi.mock('sonner', () => ({ toast: { success: vi.fn(), error: vi.fn() } }));

function renderDialog(resourceType = machineResourceType as typeof machineResourceType | null) {
  const { wrapper: Wrapper } = createTestQueryClient({ feedback: true });
  return render(
    <Wrapper>
      <ResourceTypeEditDialog resourceType={resourceType} open onOpenChange={vi.fn()} />
    </Wrapper>,
  );
}

describe('ResourceTypeEditDialog — QR codes flag', () => {
  afterEach(() => vi.clearAllMocks());

  it('saves the flag on an existing type', async () => {
    const user = userEvent.setup();
    api.updateResourceType.mockResolvedValue({ ...machineResourceType, scanCodesEnabled: true });
    renderDialog({ ...machineResourceType, scanCodesEnabled: false });

    const flag = screen.getByLabelText('Can have QR codes');
    expect(flag).not.toBeChecked();
    await user.click(flag);
    await user.click(screen.getByRole('button', { name: 'Save' }));

    await waitFor(() =>
      expect(api.updateResourceType).toHaveBeenCalledWith(
        machineResourceType.id,
        expect.objectContaining({ scanCodesEnabled: true }),
      ),
    );
  });

  it('starts a new type with QR codes on', async () => {
    const user = userEvent.setup();
    api.createResourceType.mockResolvedValue(machineResourceType);
    renderDialog(null);

    expect(screen.getByLabelText('Can have QR codes')).toBeChecked();
    await user.type(screen.getByLabelText('Name'), 'Forklift');
    await user.type(screen.getByLabelText('Plural name'), 'Forklifts');
    await user.clear(screen.getByLabelText('Key'));
    await user.type(screen.getByLabelText('Key'), 'forklift');
    await user.click(screen.getByRole('button', { name: 'Save' }));

    await waitFor(() =>
      expect(api.createResourceType).toHaveBeenCalledWith(expect.objectContaining({ scanCodesEnabled: true })),
    );
  });
});
