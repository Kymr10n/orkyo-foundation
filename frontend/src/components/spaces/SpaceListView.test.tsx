/** @jsxImportSource react */
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactNode } from 'react';
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

function createWrapper() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
  );
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

describe('SpaceListView', () => {
  let mutateAsync: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    vi.clearAllMocks();
    setSite('site-1');
    mutateAsync = vi.fn().mockResolvedValue(undefined);
    vi.mocked(useDeleteSpace).mockReturnValue({
      mutateAsync,
    } as unknown as ReturnType<typeof useDeleteSpace>);
    vi.mocked(useSpaces).mockReturnValue({
      data: spaces,
      isLoading: false,
    } as unknown as ReturnType<typeof useSpaces>);
    // useCanEdit is globally mocked to true (src/test/setup.ts); reset each test.
    vi.mocked(useCanEdit).mockReturnValue(true);
  });

  it('disables the row edit/delete/capabilities actions for a viewer', () => {
    vi.mocked(useCanEdit).mockReturnValue(false);
    render(<SpaceListView />, { wrapper: createWrapper() });
    expect(screen.getByRole('button', { name: 'Edit space Lobby' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Delete space Lobby' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Edit capabilities for Lobby' })).toBeDisabled();
  });

  it('prompts for site when none selected', () => {
    setSite(null);
    render(<SpaceListView />, { wrapper: createWrapper() });
    expect(screen.getByText(/Please select a site/)).toBeInTheDocument();
  });

  it('renders header with space count and the list', () => {
    render(<SpaceListView />, { wrapper: createWrapper() });
    expect(screen.getByText('Spaces (2)')).toBeInTheDocument();
    expect(screen.getByText('Lobby')).toBeInTheDocument();
    expect(screen.getByText('Lab 1')).toBeInTheDocument();
  });

  it('opens EditSpaceDialog when the row Edit action fires', async () => {
    const user = userEvent.setup();
    render(<SpaceListView />, { wrapper: createWrapper() });
    // SpaceList renders Edit icon buttons per row — pick the first
    const editButtons = screen
      .getAllByRole('button')
      .filter((b) => b.querySelector('.lucide-edit, .lucide-pencil, .lucide-square-pen'));
    expect(editButtons.length).toBeGreaterThan(0);
    await user.click(editButtons[0]);
    await waitFor(() => expect(screen.getByTestId('edit-dialog').textContent).toBe('edit:Lobby'));
  });

  it('opens SpaceCapabilitiesEditor when the row Settings action fires', async () => {
    const user = userEvent.setup();
    render(<SpaceListView />, { wrapper: createWrapper() });
    const capsButtons = screen
      .getAllByRole('button')
      .filter((b) => b.querySelector('.lucide-settings'));
    expect(capsButtons.length).toBeGreaterThan(0);
    await user.click(capsButtons[0]);
    await waitFor(() => expect(screen.getByTestId('capabilities-editor').textContent).toBe('caps:Lobby'));
  });

  it('confirms then deletes when the row Delete action fires', async () => {
    const user = userEvent.setup();
    vi.stubGlobal('confirm', vi.fn(() => true));
    render(<SpaceListView />, { wrapper: createWrapper() });
    const trashButtons = screen
      .getAllByRole('button')
      .filter((b) => b.querySelector('.lucide-trash-2'));
    expect(trashButtons.length).toBeGreaterThan(0);
    await user.click(trashButtons[0]);
    await waitFor(() => expect(mutateAsync).toHaveBeenCalledWith('s-1'));
    vi.unstubAllGlobals();
  });

  it('does not call delete when the confirm prompt is cancelled', async () => {
    const user = userEvent.setup();
    vi.stubGlobal('confirm', vi.fn(() => false));
    render(<SpaceListView />, { wrapper: createWrapper() });
    const trashButtons = screen
      .getAllByRole('button')
      .filter((b) => b.querySelector('.lucide-trash-2'));
    await user.click(trashButtons[0]);
    expect(mutateAsync).not.toHaveBeenCalled();
    vi.unstubAllGlobals();
  });
});
