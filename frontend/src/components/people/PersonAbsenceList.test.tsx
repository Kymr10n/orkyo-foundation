import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClientProvider } from '@tanstack/react-query';
import { PersonAbsenceList } from './PersonAbsenceList';
import type { ResourceAbsenceInfo } from '@foundation/src/lib/api/resource-absences-api';

vi.mock('@foundation/src/lib/api/resource-absences-api', () => ({
  getResourceAbsences: vi.fn(),
  deleteResourceAbsence: vi.fn(),
  createResourceAbsence: vi.fn(),
}));

// PersonAbsenceEditDialog is heavyweight; stub it so this suite stays focused on the list.
vi.mock('./PersonAbsenceEditDialog', () => ({
  PersonAbsenceEditDialog: ({ isOpen, onClose, onSaved }: {
    isOpen: boolean; onClose: () => void; onSaved: () => void;
  }) => isOpen ? (
    <div data-testid="absence-edit-dialog">
      <button data-testid="dialog-close" onClick={onClose}>Close</button>
      <button data-testid="dialog-saved" onClick={onSaved}>Saved</button>
    </div>
  ) : null,
}));

import { getResourceAbsences, deleteResourceAbsence } from '@foundation/src/lib/api/resource-absences-api';
import { createFeedbackTestQueryClientWithSpy } from '@foundation/src/test-utils';

const mockAbsences: ResourceAbsenceInfo[] = [
  {
    id: 'abs-1',
    resourceId: 'person-alice',
    absenceType: 'vacation',
    title: 'Summer break',
    startTs: '2026-07-01T00:00:00Z',
    endTs: '2026-07-14T00:00:00Z',
    isRecurring: false,
    enabled: true,
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
  },
];

function renderList(props: Partial<React.ComponentProps<typeof PersonAbsenceList>> = {}) {
  // Production-identical feedback MutationCache (dialog-feedback.md).
  const { queryClient } = createFeedbackTestQueryClientWithSpy();
  return render(
    <QueryClientProvider client={queryClient}>
      <PersonAbsenceList
        open={true}
        onOpenChange={() => {}}
        personId="person-alice"
        personName="Alice"
        {...props}
      />
    </QueryClientProvider>,
  );
}

describe('PersonAbsenceList', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(getResourceAbsences).mockResolvedValue(mockAbsences);
    vi.mocked(deleteResourceAbsence).mockResolvedValue(undefined);
  });

  it('shows the person name in the dialog title', async () => {
    renderList();
    await waitFor(() => expect(screen.getByText(/Absences — Alice/i)).toBeInTheDocument());
  });

  it('does not render a site picker', async () => {
    renderList();
    await waitFor(() => expect(getResourceAbsences).toHaveBeenCalled());
    expect(screen.queryByText(/Select site/i)).not.toBeInTheDocument();
  });

  it('fetches absences for the given personId without a siteId', async () => {
    renderList({ personId: 'person-bob' });
    await waitFor(() =>
      expect(getResourceAbsences).toHaveBeenCalledWith('person-bob'),
    );
  });

  it('renders absence rows with type, dates, and reason', async () => {
    renderList();
    await waitFor(() => expect(screen.getByText('Vacation')).toBeInTheDocument());
    expect(screen.getByText('Summer break')).toBeInTheDocument();
  });

  it('shows empty state when no absences exist', async () => {
    vi.mocked(getResourceAbsences).mockResolvedValue([]);
    renderList();
    await waitFor(() =>
      expect(screen.getByText(/No absences recorded/i)).toBeInTheDocument(),
    );
  });

  it('calls deleteResourceAbsence with the correct personId and absenceId', async () => {
    renderList();
    await waitFor(() => screen.getByText('Vacation'));
    const deleteBtn = screen.getByRole('button', { name: /Delete absence/i });
    await userEvent.click(deleteBtn);
    await waitFor(() =>
      expect(deleteResourceAbsence).toHaveBeenCalledWith('person-alice', 'abs-1'),
    );
  });

  it('opens the add absence dialog when Add Absence is clicked', async () => {
    const user = userEvent.setup();
    renderList();
    await waitFor(() => expect(getResourceAbsences).toHaveBeenCalled());
    await user.click(screen.getByRole('button', { name: /Add Absence/i }));
    expect(screen.getByTestId('absence-edit-dialog')).toBeInTheDocument();
  });
});
