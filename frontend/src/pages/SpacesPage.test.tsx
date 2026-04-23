import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { SpacesPage } from './SpacesPage';

vi.mock('@foundation/src/store/app-store', () => ({
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  useAppStore: vi.fn((selector: any) =>
    selector({ selectedSiteId: null }),
  ),
}));

vi.mock('@foundation/src/components/spaces/SpaceManagementPanel', () => ({
  SpaceManagementPanel: ({ siteId }: { siteId: string }) => (
    <div data-testid="space-panel">site={siteId}</div>
  ),
}));

describe('SpacesPage', () => {
  it('shows prompt when no site is selected', () => {
    render(
      <MemoryRouter>
        <SpacesPage />
      </MemoryRouter>,
    );
    expect(screen.getByText('Please select a site to manage spaces.')).toBeInTheDocument();
  });

  it('renders SpaceManagementPanel when site is selected', async () => {
    const { useAppStore } = await import('@foundation/src/store/app-store');
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    vi.mocked(useAppStore).mockImplementation((selector: any) =>
      selector({ selectedSiteId: 'site-1' }) as never,
    );

    render(
      <MemoryRouter>
        <SpacesPage />
      </MemoryRouter>,
    );
    expect(screen.getByTestId('space-panel')).toBeInTheDocument();
    expect(screen.getByTestId('space-panel').textContent).toContain('site=site-1');
  });
});
