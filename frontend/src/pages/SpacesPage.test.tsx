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
          <Route path="floorplan" element={<Stub id="floorplan" />} />
          <Route path="list" element={<Stub id="list" />} />
          <Route path="groups" element={<Stub id="groups" />} />
        </Route>
      </Routes>
    </MemoryRouter>,
  );
}

describe('SpacesPage', () => {
  it('renders the tab triggers in order: Floorplan, Spaces, Groups', () => {
    renderAt('/spaces/floorplan');
    const tabs = screen.getAllByRole('tab').map((t) => t.textContent);
    expect(tabs).toEqual(['Floorplan', 'Spaces', 'Groups']);
  });

  it('index route redirects to /spaces/floorplan', () => {
    renderAt('/spaces');
    expect(screen.getByTestId('floorplan')).toBeInTheDocument();
  });

  it.each([
    ['/spaces/floorplan', 'floorplan'],
    ['/spaces/list', 'list'],
    ['/spaces/groups', 'groups'],
  ])('deep-links %s renders the right child', (path, id) => {
    renderAt(path);
    expect(screen.getByTestId(id)).toBeInTheDocument();
  });

  it('clicking a tab navigates to its sub-route', async () => {
    renderAt('/spaces/floorplan');
    await userEvent.click(screen.getByRole('tab', { name: 'Spaces' }));
    expect(screen.getByTestId('list')).toBeInTheDocument();
  });
});

// SpaceListView/FloorplanView/SpaceCapabilitiesTab own the "no site selected"
// fallback now — their dedicated component tests cover that branch.
vi.mock('@foundation/src/components/spaces/SpaceManagementPanel', () => ({
  SpaceManagementPanel: () => <div />,
}));
