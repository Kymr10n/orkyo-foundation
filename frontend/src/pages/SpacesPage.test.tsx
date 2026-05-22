import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Routes, Route, Navigate } from 'react-router-dom';
import { SpacesPage } from './SpacesPage';

function Stub({ id }: { id: string }) {
  return <div data-testid={id} />;
}

function renderAt(initialPath: string) {
  return render(
    <MemoryRouter initialEntries={[initialPath]}>
      <Routes>
        <Route path="/spaces" element={<SpacesPage />}>
          <Route index element={<Navigate to="floorplan" replace />} />
          <Route path="list" element={<Navigate to="/spaces/floorplan" replace />} />
          <Route path="floorplan" element={<Stub id="floorplan" />} />
          <Route path="groups" element={<Stub id="groups" />} />
        </Route>
      </Routes>
    </MemoryRouter>,
  );
}

describe('SpacesPage', () => {
  it('renders all tab triggers', () => {
    renderAt('/spaces/floorplan');
    for (const label of ['Floorplan', 'Groups']) {
      expect(screen.getByRole('tab', { name: label })).toBeInTheDocument();
    }
  });

  it('does not expose a standalone Spaces list tab (Floorplan owns space management)', () => {
    renderAt('/spaces/floorplan');
    expect(screen.queryByRole('tab', { name: 'Spaces' })).not.toBeInTheDocument();
  });

  it('index route redirects to /spaces/floorplan', () => {
    renderAt('/spaces');
    expect(screen.getByTestId('floorplan')).toBeInTheDocument();
  });

  it('legacy /spaces/list redirects to /spaces/floorplan', () => {
    renderAt('/spaces/list');
    expect(screen.getByTestId('floorplan')).toBeInTheDocument();
  });

  it.each([
    ['/spaces/floorplan', 'floorplan'],
    ['/spaces/groups', 'groups'],
  ])('deep-links %s renders the right child', (path, id) => {
    renderAt(path);
    expect(screen.getByTestId(id)).toBeInTheDocument();
  });

  it('clicking a tab navigates to its sub-route', async () => {
    renderAt('/spaces/floorplan');
    await userEvent.click(screen.getByRole('tab', { name: 'Groups' }));
    expect(screen.getByTestId('groups')).toBeInTheDocument();
  });
});

// SpaceListView/FloorplanView/SpaceCapabilitiesTab own the "no site selected"
// fallback now — their dedicated component tests cover that branch.
vi.mock('@foundation/src/components/spaces/SpaceManagementPanel', () => ({
  SpaceManagementPanel: () => <div />,
}));
