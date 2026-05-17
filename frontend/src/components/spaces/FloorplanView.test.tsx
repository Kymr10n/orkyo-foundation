/** @jsxImportSource react */
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { FloorplanView } from './FloorplanView';

vi.mock('@foundation/src/store/app-store', () => ({ useAppStore: vi.fn() }));
vi.mock('./SpaceManagementPanel', () => ({
  SpaceManagementPanel: ({ siteId, editResourceId }: { siteId: string; editResourceId: string | null }) => (
    <div data-testid="panel">{`panel:site=${siteId}:edit=${editResourceId ?? ''}`}</div>
  ),
}));

import { useAppStore } from '@foundation/src/store/app-store';

function setSite(siteId: string | null) {
  vi.mocked(useAppStore).mockImplementation((selector: unknown) =>
    (selector as (s: { selectedSiteId: string | null }) => unknown)({ selectedSiteId: siteId }) as never,
  );
}

describe('FloorplanView', () => {
  beforeEach(() => vi.clearAllMocks());

  it('prompts for site when none selected', () => {
    setSite(null);
    render(<MemoryRouter><FloorplanView /></MemoryRouter>);
    expect(screen.getByText(/Please select a site/)).toBeInTheDocument();
  });

  it('mounts SpaceManagementPanel with the selected site and ?edit= passthrough', () => {
    setSite('site-9');
    render(
      <MemoryRouter initialEntries={['/spaces/floorplan?edit=r-42']}>
        <FloorplanView />
      </MemoryRouter>,
    );
    expect(screen.getByTestId('panel').textContent).toBe('panel:site=site-9:edit=r-42');
  });
});
