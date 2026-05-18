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
          <Route index element={<Navigate to="list" replace />} />
          <Route path="list" element={<Stub id="list" />} />
          <Route path="floorplan" element={<Stub id="floorplan" />} />
          <Route path="groups" element={<Stub id="groups" />} />
          <Route path="capabilities" element={<Stub id="capabilities" />} />
        </Route>
      </Routes>
    </MemoryRouter>,
  );
}

describe('SpacesPage', () => {
  it('renders all tab triggers', () => {
    renderAt('/spaces/list');
    for (const label of ['Spaces', 'Floorplan', 'Groups', 'Capabilities']) {
      expect(screen.getByRole('tab', { name: label })).toBeInTheDocument();
    }
  });

  it('index route redirects to /spaces/list', () => {
    renderAt('/spaces');
    expect(screen.getByTestId('list')).toBeInTheDocument();
  });

  it.each([
    ['/spaces/list', 'list'],
    ['/spaces/floorplan', 'floorplan'],
    ['/spaces/groups', 'groups'],
    ['/spaces/capabilities', 'capabilities'],
  ])('deep-links %s renders the right child', (path, id) => {
    renderAt(path);
    expect(screen.getByTestId(id)).toBeInTheDocument();
  });

  it('clicking a tab navigates to its sub-route', async () => {
    renderAt('/spaces/list');
    await userEvent.click(screen.getByRole('tab', { name: 'Capabilities' }));
    expect(screen.getByTestId('capabilities')).toBeInTheDocument();
  });
});

// SpaceListView/FloorplanView/SpaceCapabilitiesTab own the "no site selected"
// fallback now — their dedicated component tests cover that branch.
vi.mock('@foundation/src/components/spaces/SpaceManagementPanel', () => ({
  SpaceManagementPanel: () => <div />,
}));
