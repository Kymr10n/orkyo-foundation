import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactNode } from 'react';
import { SpaceCapabilitiesTab } from './SpaceCapabilitiesTab';

vi.mock('@foundation/src/store/app-store', () => ({
  useAppStore: vi.fn(),
}));

vi.mock('@foundation/src/hooks/useSpaces', () => ({
  useSpaces: vi.fn(),
}));

// Editor is opened via state; mock so we only verify it is mounted.
vi.mock('./SpaceCapabilitiesEditor', () => ({
  SpaceCapabilitiesEditor: ({ spaceName }: { spaceName: string }) => (
    <div data-testid="editor">editor:{spaceName}</div>
  ),
}));

import { useAppStore } from '@foundation/src/store/app-store';
import { useSpaces } from '@foundation/src/hooks/useSpaces';

function createWrapper() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
  );
}

describe('SpaceCapabilitiesTab', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(useAppStore).mockImplementation((selector: unknown) =>
      (selector as (s: { selectedSiteId: string }) => unknown)({ selectedSiteId: 'site-1' }) as never,
    );
  });

  it('prompts for site selection when none is selected', () => {
    vi.mocked(useAppStore).mockImplementationOnce((selector: unknown) =>
      (selector as (s: { selectedSiteId: string | null }) => unknown)({ selectedSiteId: null }) as never,
    );
    vi.mocked(useSpaces).mockReturnValue({ data: [], isLoading: false } as unknown as ReturnType<typeof useSpaces>);
    render(<SpaceCapabilitiesTab />, { wrapper: createWrapper() });
    expect(screen.getByText(/Please select a site/)).toBeInTheDocument();
  });

  it('shows the spec empty-state copy when no spaces exist', () => {
    vi.mocked(useSpaces).mockReturnValue({ data: [], isLoading: false } as unknown as ReturnType<typeof useSpaces>);
    render(<SpaceCapabilitiesTab />, { wrapper: createWrapper() });
    expect(screen.getByText('No space capabilities defined yet.')).toBeInTheDocument();
  });

  it('lists every space with a Manage capabilities button and opens the editor', async () => {
    vi.mocked(useSpaces).mockReturnValue({
      data: [
        { id: 's-1', name: 'Lobby', code: 'L1' },
        { id: 's-2', name: 'Lab 1' },
      ],
      isLoading: false,
    } as unknown as ReturnType<typeof useSpaces>);

    render(<SpaceCapabilitiesTab />, { wrapper: createWrapper() });
    expect(screen.getByText('Lobby')).toBeInTheDocument();
    expect(screen.getByText('L1')).toBeInTheDocument();
    expect(screen.getByText('Lab 1')).toBeInTheDocument();

    const buttons = screen.getAllByRole('button', { name: /Manage capabilities/i });
    expect(buttons).toHaveLength(2);

    await userEvent.click(buttons[0]);
    expect(screen.getByTestId('editor').textContent).toBe('editor:Lobby');
  });
});
