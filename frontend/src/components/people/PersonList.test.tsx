import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor, fireEvent } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { PersonList } from './PersonList';
import type { ResourceInfo, ResourcesResponse } from '@foundation/src/lib/api/resources-api';

vi.mock('@foundation/src/lib/api/resources-api', () => ({
  getResources: vi.fn(),
  deleteResource: vi.fn(),
}));

vi.mock('@foundation/src/lib/api/person-profiles-api', () => ({
  getPersonProfile: vi.fn().mockResolvedValue(null),
}));

// PersonEditDialog is a heavyweight dialog; stub it out so the list tests
// focus on the list itself rather than the dialog's dependencies.
vi.mock('./PersonEditDialog', () => ({
  PersonEditDialog: ({
    isOpen,
    person,
    onClose,
    onSaved,
  }: {
    isOpen: boolean;
    person: ResourceInfo | null;
    onClose: () => void;
    onSaved: () => void;
  }) =>
    isOpen ? (
      <div data-testid="person-edit-dialog" data-person-id={person?.id ?? ''}>
        <button data-testid="dialog-close" onClick={onClose}>
          Close
        </button>
        <button data-testid="dialog-saved" onClick={onSaved}>
          Saved
        </button>
      </div>
    ) : null,
}));

import { getResources, deleteResource } from '@foundation/src/lib/api/resources-api';

const mockPerson = (id: string, name: string): ResourceInfo => ({
  id,
  resourceTypeId: 'rt-person',
  resourceTypeKey: 'person',
  name,
  allocationMode: 'Exclusive',
  baseAvailabilityPercent: 100,
  isActive: true,
  createdAt: '2026-01-01T00:00:00Z',
  updatedAt: '2026-01-01T00:00:00Z',
});

const emptyResponse: ResourcesResponse = { data: [], total: 0, page: 1, pageSize: 50 };

const populatedResponse: ResourcesResponse = {
  data: [mockPerson('person-1', 'Alice'), mockPerson('person-2', 'Bob')],
  total: 2,
  page: 1,
  pageSize: 50,
};

function renderList() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return render(
    <QueryClientProvider client={queryClient}>
      <PersonList />
    </QueryClientProvider>,
  );
}

describe('PersonList', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(getResources).mockResolvedValue(populatedResponse);
    vi.mocked(deleteResource).mockResolvedValue(undefined);
  });

  it('calls getResources with person resource type key', async () => {
    renderList();
    await waitFor(() =>
      expect(getResources).toHaveBeenCalledWith({ resourceTypeKey: 'person' }),
    );
  });

  it('renders a row for each person returned by the API', async () => {
    renderList();
    await waitFor(() => {
      expect(screen.getByText('Alice')).toBeInTheDocument();
      expect(screen.getByText('Bob')).toBeInTheDocument();
    });
  });

  it('shows empty state when API returns an empty list', async () => {
    vi.mocked(getResources).mockResolvedValue(emptyResponse);
    renderList();
    await waitFor(() =>
      expect(screen.getByText(/No people found/i)).toBeInTheDocument(),
    );
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
  });

  it('does NOT render any people when getResources returns a plain array instead of the envelope', async () => {
    // Simulates the pre-fix bug: endpoint returned ResourceInfo[] directly.
    // people?.data is undefined on a plain array, so personRows = [] and no
    // rows are rendered. The component shows the table shell but empty — which
    // is how the regression manifested (people tab appeared blank).
    vi.mocked(getResources).mockResolvedValue(
      populatedResponse.data as unknown as ResourcesResponse,
    );
    renderList();
    // Wait for the loading state to clear before asserting absence.
    await waitFor(() =>
      expect(screen.queryByText('Loading people...')).not.toBeInTheDocument(),
    );
    expect(screen.queryByText('Alice')).not.toBeInTheDocument();
    expect(screen.queryByText('Bob')).not.toBeInTheDocument();
  });

  it('renders the Add Person button', async () => {
    renderList();
    await waitFor(() =>
      expect(screen.getByRole('button', { name: /Add Person/i })).toBeInTheDocument(),
    );
  });

  it('opens the edit dialog when Add Person is clicked', async () => {
    renderList();
    await waitFor(() => screen.getByRole('button', { name: /Add Person/i }));
    fireEvent.click(screen.getByRole('button', { name: /Add Person/i }));
    expect(screen.getByTestId('person-edit-dialog')).toBeInTheDocument();
  });

  it('renders edit and delete action buttons for each person', async () => {
    renderList();
    await waitFor(() => screen.getByText('Alice'));
    // Two rows × two buttons each = 4 ghost buttons
    const editButtons = screen.getAllByRole('button', { name: '' });
    expect(editButtons.length).toBeGreaterThanOrEqual(4);
  });

  it('opens the dialog with the correct person when edit is clicked', async () => {
    renderList();
    await waitFor(() => screen.getByText('Alice'));
    const editButtons = screen.getAllByRole('button', { name: '' });
    // First ghost button in the first row is the Pencil button
    fireEvent.click(editButtons[0]);
    const dialog = screen.getByTestId('person-edit-dialog');
    expect(dialog).toBeInTheDocument();
    expect(dialog).toHaveAttribute('data-person-id', 'person-1');
  });

  it('closes the dialog when onClose is called', async () => {
    renderList();
    await waitFor(() => screen.getByRole('button', { name: /Add Person/i }));
    fireEvent.click(screen.getByRole('button', { name: /Add Person/i }));
    expect(screen.getByTestId('person-edit-dialog')).toBeInTheDocument();
    fireEvent.click(screen.getByTestId('dialog-close'));
    expect(screen.queryByTestId('person-edit-dialog')).not.toBeInTheDocument();
  });

  it('closes the dialog and refreshes when onSaved is called', async () => {
    renderList();
    await waitFor(() => screen.getByRole('button', { name: /Add Person/i }));
    fireEvent.click(screen.getByRole('button', { name: /Add Person/i }));
    expect(screen.getByTestId('person-edit-dialog')).toBeInTheDocument();
    fireEvent.click(screen.getByTestId('dialog-saved'));
    expect(screen.queryByTestId('person-edit-dialog')).not.toBeInTheDocument();
  });

  it('calls deleteResource when delete is confirmed', async () => {
    vi.stubGlobal('confirm', vi.fn().mockReturnValue(true));
    renderList();
    await waitFor(() => screen.getByText('Alice'));
    const allButtons = screen.getAllByRole('button', { name: '' });
    // Second ghost button in the first row is the Trash button (index 1)
    fireEvent.click(allButtons[1]);
    await waitFor(() =>
      expect(deleteResource).toHaveBeenCalledWith('person-1'),
    );
    vi.unstubAllGlobals();
  });

  it('does not call deleteResource when delete is cancelled', async () => {
    vi.stubGlobal('confirm', vi.fn().mockReturnValue(false));
    renderList();
    await waitFor(() => screen.getByText('Alice'));
    const allButtons = screen.getAllByRole('button', { name: '' });
    fireEvent.click(allButtons[1]);
    expect(deleteResource).not.toHaveBeenCalled();
    vi.unstubAllGlobals();
  });
});
