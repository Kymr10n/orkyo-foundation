/** @jsxImportSource react */
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { PersonSkillsEditor } from './PersonSkillsEditor';
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

describe('PersonSkillsEditor', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(getCriteria).mockResolvedValue([PERSON_SKILL]);
    vi.mocked(getResourceCapabilities).mockResolvedValue([]);
    vi.mocked(upsertResourceCapability).mockResolvedValue(EXISTING_ASSIGNMENT);
    vi.mocked(deleteResourceCapability).mockResolvedValue(undefined);
  });

  it('fetches only person-applicable criteria (cross-type protection)', async () => {
    render(<PersonSkillsEditor {...defaultProps} />);
    await waitFor(() => {
      expect(getCriteria).toHaveBeenCalledWith({ resourceType: 'person' });
    });
  });

  it('loads existing capabilities for the resource', async () => {
    render(<PersonSkillsEditor {...defaultProps} />);
    await waitFor(() => {
      expect(getResourceCapabilities).toHaveBeenCalledWith('p-1');
    });
  });

  it('renders dialog title with person name', async () => {
    render(<PersonSkillsEditor {...defaultProps} />);
    await waitFor(() => expect(screen.getByText('Skills for Alice')).toBeInTheDocument());
  });

  it('upserts preloaded assignments when Save is clicked unchanged', async () => {
    vi.mocked(getResourceCapabilities).mockResolvedValue([EXISTING_ASSIGNMENT]);
    const user = userEvent.setup({ pointerEventsCheck: 0 });
    render(<PersonSkillsEditor {...defaultProps} />);

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
    render(<PersonSkillsEditor {...defaultProps} />);

    await waitFor(() => expect(screen.getByText('1 active')).toBeInTheDocument());

    // The trash button is the only destructive-icon button rendered now.
    const trashButtons = screen.getAllByRole('button').filter((b) => b.querySelector('.text-destructive'));
    await user.click(trashButtons[0]);

    await user.click(screen.getByRole('button', { name: /save changes/i }));

    await waitFor(() => {
      expect(deleteResourceCapability).toHaveBeenCalledWith('p-1', 'cap-1');
    });
  });
});
