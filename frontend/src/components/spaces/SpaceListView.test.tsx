/** @jsxImportSource react */
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { SpaceListView } from './SpaceListView';

vi.mock('@foundation/src/store/app-store', () => ({ useAppStore: vi.fn() }));
vi.mock('@foundation/src/hooks/useSpaces', () => ({
  useSpaces: vi.fn(),
  useDeleteSpace: vi.fn(),
}));
vi.mock('./EditSpaceDialog', () => ({
  EditSpaceDialog: ({ space }: { space: { name: string } }) => (
    <div data-testid="edit-dialog">edit:{space.name}</div>
  ),
}));
vi.mock('./SpaceCapabilitiesEditor', () => ({
  SpaceCapabilitiesEditor: ({ spaceName }: { spaceName: string }) => (
    <div data-testid="capabilities-editor">caps:{spaceName}</div>
  ),
}));

import { useAppStore } from '@foundation/src/store/app-store';
import { useSpaces, useDeleteSpace } from '@foundation/src/hooks/useSpaces';
import { useCanEdit } from '@foundation/src/hooks/usePermissions';
import { createFeedbackTestQueryWrapper } from '@foundation/src/test-utils';

function createWrapper() {
  // Production-identical feedback MutationCache (dialog-feedback.md).
  return createFeedbackTestQueryWrapper();
}

function setSite(siteId: string | null) {
  vi.mocked(useAppStore).mockImplementation((selector: unknown) =>
    (selector as (s: { selectedSiteId: string | null }) => unknown)({ selectedSiteId: siteId }) as never,
  );
}

const spaces = [
  { id: 's-1', name: 'Lobby' },
  { id: 's-2', name: 'Lab 1' },
];

// Row actions live behind a labelled kebab menu (RowActions).
async function openRowMenu(user: ReturnType<typeof userEvent.setup>, name: string) {
  await user.click(screen.getByRole('button', { name: `Actions for ${name}` }));
  await screen.findByRole('menuitem', { name: /Edit Space/ });
}

describe('SpaceListView', () => {
  let mutateAsync: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    vi.clearAllMocks();
    setSite('site-1');
    mutateAsync = vi.fn().mockResolvedValue(undefined);
    vi.mocked(useDeleteSpace).mockReturnValue({
      mutateAsync,
      isPending: false,
    } as unknown as ReturnType<typeof useDeleteSpace>);
    vi.mocked(useSpaces).mockReturnValue({
      data: spaces,
      isLoading: false,
    } as unknown as ReturnType<typeof useSpaces>);
    // useCanEdit is globally mocked to true (src/test/setup.ts); reset each test.
    vi.mocked(useCanEdit).mockReturnValue(true);
  });

  it('disables the row edit/delete/capabilities actions for a viewer', async () => {
    const user = userEvent.setup();
    vi.mocked(useCanEdit).mockReturnValue(false);
    render(<SpaceListView />, { wrapper: createWrapper() });
    await openRowMenu(user, 'Lobby');
    expect(screen.getByRole('menuitem', { name: /Edit Space/ })).toHaveAttribute('aria-disabled', 'true');
    expect(screen.getByRole('menuitem', { name: /Delete Space/ })).toHaveAttribute('aria-disabled', 'true');
    expect(screen.getByRole('menuitem', { name: /Edit Capabilities/ })).toHaveAttribute('aria-disabled', 'true');
  });

  it('prompts for site when none selected', () => {
    setSite(null);
    render(<SpaceListView />, { wrapper: createWrapper() });
    expect(screen.getByText(/Please select a site/)).toBeInTheDocument();
  });

  it('renders the spaces list', () => {
    render(<SpaceListView />, { wrapper: createWrapper() });
    expect(screen.getByText('Lobby')).toBeInTheDocument();
    expect(screen.getByText('Lab 1')).toBeInTheDocument();
  });

  it('opens EditSpaceDialog when the row Edit action fires', async () => {
    const user = userEvent.setup();
    render(<SpaceListView />, { wrapper: createWrapper() });
    await openRowMenu(user, 'Lobby');
    await user.click(screen.getByRole('menuitem', { name: /Edit Space/ }));
    await waitFor(() => expect(screen.getByTestId('edit-dialog').textContent).toBe('edit:Lobby'));
  });

  it('opens SpaceCapabilitiesEditor when the row Settings action fires', async () => {
    const user = userEvent.setup();
    render(<SpaceListView />, { wrapper: createWrapper() });
    await openRowMenu(user, 'Lobby');
    await user.click(screen.getByRole('menuitem', { name: /Edit Capabilities/ }));
    await waitFor(() => expect(screen.getByTestId('capabilities-editor').textContent).toBe('caps:Lobby'));
  });

  it('confirms then deletes when the row Delete action fires', async () => {
    const user = userEvent.setup();
    render(<SpaceListView />, { wrapper: createWrapper() });
    await openRowMenu(user, 'Lobby');
    await user.click(screen.getByRole('menuitem', { name: /Delete Space/ }));
    // The shared ConfirmDialog replaces the native confirm() prompt.
    await user.click(await screen.findByRole('button', { name: 'Delete' }));
    await waitFor(() => expect(mutateAsync).toHaveBeenCalledWith('s-1'));
  });

  it('does not call delete when the confirm prompt is cancelled', async () => {
    const user = userEvent.setup();
    render(<SpaceListView />, { wrapper: createWrapper() });
    await openRowMenu(user, 'Lobby');
    await user.click(screen.getByRole('menuitem', { name: /Delete Space/ }));
    await user.click(await screen.findByRole('button', { name: 'Cancel' }));
    expect(mutateAsync).not.toHaveBeenCalled();
  });
});
