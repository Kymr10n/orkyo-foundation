import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor, fireEvent } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter } from 'react-router-dom';
import { PersonList } from './PersonList';
import { useCanEdit } from '@foundation/src/hooks/usePermissions';
import type { ResourceInfo, ResourcesResponse } from '@foundation/src/lib/api/resources-api';

vi.mock('@foundation/src/lib/api/resources-api', () => ({
  getResources: vi.fn(),
  deleteResource: vi.fn(),
}));

vi.mock('@foundation/src/lib/api/person-profiles-api', () => ({
  getPersonProfiles: vi.fn().mockResolvedValue([]),
}));

vi.mock('sonner', () => ({
  toast: { success: vi.fn(), error: vi.fn() },
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

// PersonSkillsEditor pulls in CreateCriterionDialog and several queries;
// stub it so this suite stays focused on the list-row plumbing.
vi.mock('./PersonSkillsEditor', () => ({
  PersonSkillsEditor: ({
    open,
    resourceId,
    personName,
    onOpenChange,
  }: {
    open: boolean;
    resourceId: string;
    personName: string;
    onOpenChange: (open: boolean) => void;
  }) =>
    open ? (
      <div
        data-testid="person-skills-editor"
        data-resource-id={resourceId}
        data-person-name={personName}
      >
        <button data-testid="skills-close" onClick={() => onOpenChange(false)}>
          Close
        </button>
      </div>
    ) : null,
}));

vi.mock('./PersonAbsenceList', () => ({
  PersonAbsenceList: ({
    open,
    personId,
    personName,
    onOpenChange,
  }: {
    open: boolean;
    personId: string;
    personName: string;
    onOpenChange: (open: boolean) => void;
  }) =>
    open ? (
      <div
        data-testid="person-absence-list"
        data-person-id={personId}
        data-person-name={personName}
      >
        <button data-testid="absence-close" onClick={() => onOpenChange(false)}>
          Close
        </button>
      </div>
    ) : null,
}));

import { getResources, deleteResource } from '@foundation/src/lib/api/resources-api';
import { createFeedbackMutationCache } from '@foundation/src/lib/core/query-client';
import { toast } from 'sonner';

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

function renderList(initialEntries: string[] = ['/']) {
  // Delete feedback now flows through the meta-driven MutationCache (matching
  // prod), so wire the same cache here for the success/error toast assertions.
  const queryClient: QueryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
    mutationCache: createFeedbackMutationCache(() => queryClient, toast),
  });
  return render(
    <MemoryRouter initialEntries={initialEntries}>
      <QueryClientProvider client={queryClient}>
        <PersonList />
      </QueryClientProvider>
    </MemoryRouter>,
  );
}

// Actions live behind a labelled kebab menu (RowActions). Radix's dropdown needs
// real pointer events, so use userEvent to open the row's menu and click items.
// A single user instance is shared per test (a second setup() dispatches a
// pointerdown Radix reads as an outside-click, closing the menu).
let user: ReturnType<typeof userEvent.setup>;

async function openRowMenu(name: string) {
  // The profile/job-title queries resolve after first paint and re-render the
  // table, which would dismiss a freshly-opened menu. Retry the open until a
  // menu item sticks so the test isn't racing that settle.
  await waitFor(async () => {
    if (screen.queryAllByRole('menuitem').length === 0) {
      await user.click(screen.getByRole('button', { name: `Actions for ${name}` }));
    }
    expect(screen.getAllByRole('menuitem').length).toBeGreaterThan(0);
  });
}

async function clickMenuItem(name: RegExp) {
  await user.click(await screen.findByRole('menuitem', { name }));
}

// Confirm a destructive ConfirmDialog by clicking its confirm button.
async function confirmDelete(label = 'Deactivate') {
  fireEvent.click(await screen.findByRole('button', { name: label }));
}

describe('PersonList', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    user = userEvent.setup();
    vi.mocked(getResources).mockResolvedValue(populatedResponse);
    vi.mocked(deleteResource).mockResolvedValue(undefined);
    // useCanEdit is globally mocked to true (src/test/setup.ts); reset each test.
    vi.mocked(useCanEdit).mockReturnValue(true);
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

  it('renders a labelled actions menu for each person', async () => {
    renderList();
    await waitFor(() => screen.getByText('Alice'));
    expect(screen.getByRole('button', { name: 'Actions for Alice' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Actions for Bob' })).toBeInTheDocument();
    await openRowMenu('Alice');
    expect(await screen.findByRole('menuitem', { name: /Edit Person/ })).toBeInTheDocument();
    expect(screen.getByRole('menuitem', { name: /Manage Skills/ })).toBeInTheDocument();
    expect(screen.getByRole('menuitem', { name: /Manage Absences/ })).toBeInTheDocument();
    expect(screen.getByRole('menuitem', { name: /Deactivate Person/ })).toBeInTheDocument();
  });

  it('opens the edit dialog with the correct person when edit is clicked', async () => {
    renderList();
    await waitFor(() => screen.getByText('Alice'));
    await openRowMenu('Alice');
    await clickMenuItem(/Edit Person/);
    const dialog = screen.getByTestId('person-edit-dialog');
    expect(dialog).toHaveAttribute('data-person-id', 'person-1');
  });

  it('opens the edit dialog with the correct person when the row is clicked', async () => {
    renderList();
    await waitFor(() => screen.getByText('Bob'));
    // Clicking anywhere in the row (here the name cell) opens the edit dialog.
    fireEvent.click(screen.getByText('Bob'));
    const dialog = screen.getByTestId('person-edit-dialog');
    expect(dialog).toHaveAttribute('data-person-id', 'person-2');
  });

  it('does not open the edit dialog when a row action is clicked', async () => {
    // The actions menu wrapper stops propagation so its items never trigger the
    // row-level click that would open the edit dialog.
    renderList();
    await waitFor(() => screen.getByText('Alice'));
    await openRowMenu('Alice');
    await clickMenuItem(/Manage Skills/);
    expect(screen.getByTestId('person-skills-editor')).toBeInTheDocument();
    expect(screen.queryByTestId('person-edit-dialog')).not.toBeInTheDocument();
  });

  it('opens the skills editor with the correct person when manage-skills is clicked', async () => {
    renderList();
    await waitFor(() => screen.getByText('Alice'));
    await openRowMenu('Bob');
    await clickMenuItem(/Manage Skills/);
    const editor = screen.getByTestId('person-skills-editor');
    expect(editor).toHaveAttribute('data-resource-id', 'person-2');
    expect(editor).toHaveAttribute('data-person-name', 'Bob');
  });

  it('closes the skills editor when its onOpenChange fires', async () => {
    renderList();
    await waitFor(() => screen.getByText('Alice'));
    await openRowMenu('Alice');
    await clickMenuItem(/Manage Skills/);
    expect(screen.getByTestId('person-skills-editor')).toBeInTheDocument();
    fireEvent.click(screen.getByTestId('skills-close'));
    expect(screen.queryByTestId('person-skills-editor')).not.toBeInTheDocument();
  });

  it('closes the edit dialog when onClose is called', async () => {
    renderList();
    await waitFor(() => screen.getByRole('button', { name: /Add Person/i }));
    fireEvent.click(screen.getByRole('button', { name: /Add Person/i }));
    expect(screen.getByTestId('person-edit-dialog')).toBeInTheDocument();
    fireEvent.click(screen.getByTestId('dialog-close'));
    expect(screen.queryByTestId('person-edit-dialog')).not.toBeInTheDocument();
  });

  it('closes the edit dialog and refreshes when onSaved is called', async () => {
    renderList();
    await waitFor(() => screen.getByRole('button', { name: /Add Person/i }));
    fireEvent.click(screen.getByRole('button', { name: /Add Person/i }));
    expect(screen.getByTestId('person-edit-dialog')).toBeInTheDocument();
    fireEvent.click(screen.getByTestId('dialog-saved'));
    expect(screen.queryByTestId('person-edit-dialog')).not.toBeInTheDocument();
  });

  it('calls deleteResource when delete is confirmed', async () => {
    renderList();
    await waitFor(() => screen.getByText('Alice'));
    await openRowMenu('Alice');
    await clickMenuItem(/Deactivate Person/);
    await confirmDelete();
    await waitFor(() =>
      expect(deleteResource).toHaveBeenCalledWith('person-1'),
    );
  });

  it('does not call deleteResource when delete is cancelled', async () => {
    renderList();
    await waitFor(() => screen.getByText('Alice'));
    await openRowMenu('Alice');
    await clickMenuItem(/Deactivate Person/);
    // Dismiss the confirm dialog via its Cancel action.
    fireEvent.click(await screen.findByRole('button', { name: 'Cancel' }));
    expect(deleteResource).not.toHaveBeenCalled();
  });

  it('shows a success toast after deactivating a person', async () => {
    renderList();
    await waitFor(() => screen.getByText('Alice'));
    await openRowMenu('Alice');
    await clickMenuItem(/Deactivate Person/);
    await confirmDelete();
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith('Person deactivated'));
  });

  it('shows an error toast when deactivation fails', async () => {
    vi.mocked(deleteResource).mockRejectedValue(new Error('Network error'));
    renderList();
    await waitFor(() => screen.getByText('Alice'));
    await openRowMenu('Alice');
    await clickMenuItem(/Deactivate Person/);
    await confirmDelete();
    await waitFor(() =>
      expect(toast.error).toHaveBeenCalledWith(
        'Failed to deactivate person',
        expect.objectContaining({ description: 'Network error' }),
      ),
    );
  });

  it('renders manage-absences action for each person', async () => {
    renderList();
    await waitFor(() => screen.getByText('Alice'));
    await openRowMenu('Alice');
    expect(await screen.findByRole('menuitem', { name: /Manage Absences/ })).toBeInTheDocument();
  });

  it('opens the absence list with the correct person when the absence action is clicked', async () => {
    renderList();
    await waitFor(() => screen.getByText('Alice'));
    await openRowMenu('Alice');
    await clickMenuItem(/Manage Absences/);
    const panel = screen.getByTestId('person-absence-list');
    expect(panel).toHaveAttribute('data-person-id', 'person-1');
    expect(panel).toHaveAttribute('data-person-name', 'Alice');
  });

  it('closes the absence list when its onOpenChange fires', async () => {
    renderList();
    await waitFor(() => screen.getByText('Alice'));
    await openRowMenu('Alice');
    await clickMenuItem(/Manage Absences/);
    expect(screen.getByTestId('person-absence-list')).toBeInTheDocument();
    fireEvent.click(screen.getByTestId('absence-close'));
    expect(screen.queryByTestId('person-absence-list')).not.toBeInTheDocument();
  });

  it('disables write affordances for a viewer who cannot edit', async () => {
    vi.mocked(useCanEdit).mockReturnValue(false);
    renderList();

    await screen.findByText('Alice');
    // Assert the (always-visible) Add button before opening the menu — an open
    // Radix menu aria-hides the rest of the page.
    expect(screen.getByRole('button', { name: /Add Person/i })).toBeDisabled();

    await openRowMenu('Alice');
    expect(await screen.findByRole('menuitem', { name: /Deactivate Person/ })).toHaveAttribute(
      'aria-disabled',
      'true',
    );
    expect(screen.getByRole('menuitem', { name: /Manage Skills/ })).toHaveAttribute(
      'aria-disabled',
      'true',
    );
  });
});
