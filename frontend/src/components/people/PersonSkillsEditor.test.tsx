/** @jsxImportSource react */
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { PersonSkillsEditor } from './PersonSkillsEditor';
import { createFeedbackTestQueryWrapper } from '@foundation/src/test-utils';
import type { Criterion } from '@foundation/src/types/criterion';
import type { ResourceCapability } from '@foundation/src/lib/api/resource-capabilities-api';

vi.mock('@foundation/src/lib/api/criteria-api', () => ({ getCriteria: vi.fn() }));
vi.mock('@foundation/src/lib/api/resource-capabilities-api', () => ({
  getResourceCapabilities: vi.fn(),
  upsertResourceCapability: vi.fn(),
  deleteResourceCapability: vi.fn(),
}));
vi.mock('@foundation/src/lib/core/logger', () => ({
  logger: { debug: vi.fn(), info: vi.fn(), warn: vi.fn(), error: vi.fn() },
}));

const toastSuccess = vi.fn();
const toastError = vi.fn();
vi.mock('sonner', () => ({ toast: { success: (...a: unknown[]) => toastSuccess(...a), error: (...a: unknown[]) => toastError(...a) } }));

// Stub CriterionEditDialog. The real dialog mounts react-query mutations and
// is exercised by its own test suite; here we only care that the editor wires
// it up correctly and reacts to onSaved.
const mockCreateCriterionDialogProps = vi.fn();
vi.mock('../settings/CriterionEditDialog', () => ({
  CriterionEditDialog: (props: {
    criterion: Criterion | null;
    open: boolean;
    onOpenChange: (open: boolean) => void;
    onSaved?: (criterion: Criterion) => void;
    defaultResourceType?: string;
  }) => {
    mockCreateCriterionDialogProps(props);
    return props.open ? (
      <div data-testid="create-criterion-dialog" data-default-resource-type={props.defaultResourceType}>
        <button
          data-testid="create-criterion-success"
          onClick={() =>
            props.onSaved?.({
              id: 'new-criterion-id',
              name: 'Newly created',
              dataType: 'Boolean',
              resourceTypeKeys: ['person'],
              createdAt: '2026-01-01T00:00:00Z',
              updatedAt: '2026-01-01T00:00:00Z',
            })
          }
        >
          Pretend create succeeded
        </button>
      </div>
    ) : null;
  },
}));

import { getCriteria } from '@foundation/src/lib/api/criteria-api';
import {
  getResourceCapabilities,
  upsertResourceCapability,
  deleteResourceCapability,
} from '@foundation/src/lib/api/resource-capabilities-api';

const PERSON_SKILL: Criterion = {
  id: 'c-firstaid',
  name: 'First-aid trained',
  dataType: 'Boolean',
  resourceTypeKeys: ['person'],
  createdAt: '2026-01-01T00:00:00Z',
  updatedAt: '2026-01-01T00:00:00Z',
};
const EXISTING_ASSIGNMENT: ResourceCapability = {
  id: 'cap-1',
  resourceId: 'p-1',
  criterionId: 'c-firstaid',
  value: true,
  createdAt: '2026-01-01T00:00:00Z',
  updatedAt: '2026-01-01T00:00:00Z',
  criterion: { id: 'c-firstaid', name: 'First-aid trained', dataType: 'Boolean' },
};

const defaultProps = {
  open: true,
  onOpenChange: vi.fn(),
  resourceId: 'p-1',
  personName: 'Alice',
};

// Save now flows through useMutation; the feedback wrapper's MutationCache mirrors
// production so meta-driven toasts/invalidation fire in tests.
const makeWrapper = createFeedbackTestQueryWrapper;

describe('PersonSkillsEditor', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(getCriteria).mockResolvedValue([PERSON_SKILL]);
    vi.mocked(getResourceCapabilities).mockResolvedValue([]);
    vi.mocked(upsertResourceCapability).mockResolvedValue(EXISTING_ASSIGNMENT);
    vi.mocked(deleteResourceCapability).mockResolvedValue(undefined);
  });

  it('fetches only person-applicable criteria (cross-type protection)', async () => {
    render(<PersonSkillsEditor {...defaultProps} />, { wrapper: makeWrapper() });
    await waitFor(() => {
      expect(getCriteria).toHaveBeenCalledWith({ resourceType: 'person' });
    });
  });

  it('loads existing capabilities for the resource', async () => {
    render(<PersonSkillsEditor {...defaultProps} />, { wrapper: makeWrapper() });
    await waitFor(() => {
      expect(getResourceCapabilities).toHaveBeenCalledWith('p-1');
    });
  });

  it('renders dialog title with person name', async () => {
    render(<PersonSkillsEditor {...defaultProps} />, { wrapper: makeWrapper() });
    await waitFor(() => expect(screen.getByText('Skills for Alice')).toBeInTheDocument());
  });

  it('upserts preloaded assignments when Save is clicked unchanged', async () => {
    vi.mocked(getResourceCapabilities).mockResolvedValue([EXISTING_ASSIGNMENT]);
    const user = userEvent.setup({ pointerEventsCheck: 0 });
    render(<PersonSkillsEditor {...defaultProps} />, { wrapper: makeWrapper() });

    await waitFor(() => expect(screen.getByText('1 active')).toBeInTheDocument());

    await user.click(screen.getByRole('button', { name: /save changes/i }));

    await waitFor(() => {
      expect(upsertResourceCapability).toHaveBeenCalledWith('p-1', {
        criterionId: 'c-firstaid',
        value: true,
      });
    });
  });

  it('deletes an assignment that was removed in the editor', async () => {
    vi.mocked(getResourceCapabilities).mockResolvedValue([EXISTING_ASSIGNMENT]);
    const user = userEvent.setup({ pointerEventsCheck: 0 });
    render(<PersonSkillsEditor {...defaultProps} />, { wrapper: makeWrapper() });

    await waitFor(() => expect(screen.getByText('1 active')).toBeInTheDocument());

    // The trash button is the only destructive-icon button rendered now.
    const trashButtons = screen.getAllByRole('button').filter((b) => b.querySelector('.text-destructive'));
    await user.click(trashButtons[0]);

    await user.click(screen.getByRole('button', { name: /save changes/i }));

    await waitFor(() => {
      expect(deleteResourceCapability).toHaveBeenCalledWith('p-1', 'cap-1');
    });
  });

  it('opens the create-criterion dialog with person applicability defaulted', async () => {
    const user = userEvent.setup({ pointerEventsCheck: 0 });
    render(<PersonSkillsEditor {...defaultProps} />, { wrapper: makeWrapper() });

    await waitFor(() => screen.getByText('Skills for Alice'));
    await user.click(screen.getByRole('button', { name: /add skill criterion/i }));

    const dialog = await screen.findByTestId('create-criterion-dialog');
    expect(dialog).toHaveAttribute('data-default-resource-type', 'person');
  });

  it('refreshes the criteria list when a quick-created criterion succeeds', async () => {
    // After successful create, the editor must invalidate the person-criteria
    // query so the new criterion appears in the select without a page reload.
    vi.mocked(getCriteria)
      .mockResolvedValueOnce([PERSON_SKILL])
      .mockResolvedValueOnce([
        PERSON_SKILL,
        {
          id: 'new-criterion-id',
          name: 'Newly created',
          dataType: 'Boolean',
          resourceTypeKeys: ['person'],
          createdAt: '2026-01-01T00:00:00Z',
          updatedAt: '2026-01-01T00:00:00Z',
        },
      ]);

    const user = userEvent.setup({ pointerEventsCheck: 0 });
    render(<PersonSkillsEditor {...defaultProps} />, { wrapper: makeWrapper() });

    await waitFor(() => screen.getByText('Skills for Alice'));
    await user.click(screen.getByRole('button', { name: /add skill criterion/i }));
    await user.click(screen.getByTestId('create-criterion-success'));

    await waitFor(() => {
      // Initial render fetches once; invalidation triggers a refetch.
      expect(getCriteria).toHaveBeenCalledTimes(2);
    });
  });

  it('displays capsError message when loading capabilities fails', async () => {
    vi.mocked(getResourceCapabilities).mockRejectedValueOnce(new Error('DB unavailable'));
    render(<PersonSkillsEditor {...defaultProps} />, { wrapper: makeWrapper() });
    await waitFor(() =>
      expect(screen.getByText('DB unavailable')).toBeInTheDocument(),
    );
  });

  it('shows success toast and closes dialog after a successful save', async () => {
    vi.mocked(getResourceCapabilities).mockResolvedValue([EXISTING_ASSIGNMENT]);
    const onOpenChange = vi.fn();
    const user = userEvent.setup({ pointerEventsCheck: 0 });
    render(<PersonSkillsEditor {...defaultProps} onOpenChange={onOpenChange} />, { wrapper: makeWrapper() });

    await waitFor(() => expect(screen.getByText('1 active')).toBeInTheDocument());
    await user.click(screen.getByRole('button', { name: /save changes/i }));

    await waitFor(() => {
      expect(toastSuccess).toHaveBeenCalledWith('Skills saved');
      expect(onOpenChange).toHaveBeenCalledWith(false);
    });
  });

  it('displays saveError and toast when upsert fails during save', async () => {
    vi.mocked(getResourceCapabilities).mockResolvedValue([EXISTING_ASSIGNMENT]);
    vi.mocked(upsertResourceCapability).mockRejectedValueOnce(new Error('Save failed'));
    const user = userEvent.setup({ pointerEventsCheck: 0 });
    render(<PersonSkillsEditor {...defaultProps} />, { wrapper: makeWrapper() });
    await waitFor(() => expect(screen.getByText('1 active')).toBeInTheDocument());
    await user.click(screen.getByRole('button', { name: /save changes/i }));
    await waitFor(() => {
      expect(screen.getByText('Save failed')).toBeInTheDocument();
      expect(toastError).toHaveBeenCalledWith('Failed to save skills', expect.objectContaining({ description: 'Save failed' }));
    });
  });

  it('shows empty message when no criteria are available', async () => {
    vi.mocked(getCriteria).mockResolvedValue([]);
    render(<PersonSkillsEditor {...defaultProps} />, { wrapper: makeWrapper() });
    await waitFor(() =>
      expect(
        screen.getByText(/all person-applicable criteria are already assigned, or none exist yet/i),
      ).toBeInTheDocument(),
    );
  });
});
