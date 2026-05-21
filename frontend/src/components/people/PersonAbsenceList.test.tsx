import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { PersonAbsenceList } from './PersonAbsenceList';
import type { ResourceAbsenceInfo } from '@foundation/src/lib/api/resource-absences-api';

vi.mock('@foundation/src/lib/api/site-api', () => ({
  getSites: vi.fn(),
}));

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

import { getSites } from '@foundation/src/lib/api/site-api';
import { getResourceAbsences, deleteResourceAbsence } from '@foundation/src/lib/api/resource-absences-api';

const mockSites = [
  { id: 'site-1', name: 'Main Office', code: 'MAIN' },
];

const mockAbsences: ResourceAbsenceInfo[] = [
  {
    id: 'abs-1',
    siteId: 'site-1',
    title: 'Summer break',
    type: 'vacation',
    appliesToAllResources: false,
    resourceIds: ['person-alice'],
    startTs: '2026-07-01T00:00:00Z',
    endTs: '2026-07-14T00:00:00Z',
    isRecurring: false,
    enabled: true,
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
  },
];

function renderList(props: Partial<React.ComponentProps<typeof PersonAbsenceList>> = {}) {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
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
    vi.mocked(getSites).mockResolvedValue(mockSites as never);
    vi.mocked(getResourceAbsences).mockResolvedValue(mockAbsences);
    vi.mocked(deleteResourceAbsence).mockResolvedValue(undefined);
  });

  it('shows the person name in the dialog title', async () => {
    renderList();
    await waitFor(() => expect(screen.getByText(/Absences — Alice/i)).toBeInTheDocument());
  });

  it('shows site selector and disabled Add Absence button before a site is chosen', async () => {
    renderList();
    await waitFor(() => expect(getSites).toHaveBeenCalled());
    expect(screen.getByRole('button', { name: /Add Absence/i })).toBeDisabled();
  });

  it('does not render a person selector', async () => {
    renderList();
    await waitFor(() => expect(getSites).toHaveBeenCalled());
    expect(screen.queryByText(/Select a person/i)).not.toBeInTheDocument();
  });

  it('fetches absences for the given personId across all sites', async () => {
    renderList({ personId: 'person-bob' });
    await waitFor(() =>
      expect(getResourceAbsences).toHaveBeenCalledWith('person-bob', 'site-1'),
    );
  });

  it('renders absence rows with type, dates, reason, and site', async () => {
    renderList();
    await waitFor(() => expect(screen.getByText('Vacation')).toBeInTheDocument());
    expect(screen.getByText('Summer break')).toBeInTheDocument();
    expect(screen.getByText('Main Office')).toBeInTheDocument();
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

  it('enables Add Absence button and opens add dialog after selecting a site', async () => {
    const user = userEvent.setup();
    renderList();
    await waitFor(() => expect(getSites).toHaveBeenCalled());

    const trigger = screen.getByRole('combobox');
    await user.click(trigger);
    const option = await screen.findByRole('option', { name: /Main Office/i });
    await user.click(option);

    expect(screen.getByRole('button', { name: /Add Absence/i })).not.toBeDisabled();
    await user.click(screen.getByRole('button', { name: /Add Absence/i }));
    expect(screen.getByTestId('absence-edit-dialog')).toBeInTheDocument();
  });
});
