import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Routes, Route, Navigate } from 'react-router';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { FloorplanPage } from './FloorplanPage';

function Stub({ id }: { id: string }) {
  return <div data-testid={id} />;
}

// The page registers import/export per placeable type, which reads the resource-type and
// placeable-resource queries — so it needs a client, exactly like it does in the app.
function renderAt(initialPath: string) {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return render(
    <QueryClientProvider client={queryClient}>
    <MemoryRouter initialEntries={[initialPath]}>
      <Routes>
        <Route path="/floorplan" element={<FloorplanPage />}>
          <Route index element={<Navigate to="floorplan" replace />} />
          <Route path="floorplan" element={<Stub id="floorplan" />} />
          <Route path="stations" element={<Stub id="stations" />} />
        </Route>
      </Routes>
    </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('FloorplanPage', () => {
  it('renders the tab triggers in order: Floorplan, Stations', () => {
    renderAt('/floorplan/floorplan');
    const tabs = screen.getAllByRole('tab').map((t) => t.textContent);
    // Groups moved to the generic per-type pages — a type owns its own groups.
    expect(tabs).toEqual(['Floorplan', 'Stations']);
  });

  it('index route redirects to the plan', () => {
    renderAt('/floorplan');
    expect(screen.getByTestId('floorplan')).toBeInTheDocument();
  });

  it.each([
    ['/floorplan/floorplan', 'floorplan'],
    ['/floorplan/stations', 'stations'],
  ])('deep-links %s renders the right child', (path, id) => {
    renderAt(path);
    expect(screen.getByTestId(id)).toBeInTheDocument();
  });

  it('clicking a tab navigates to its sub-route', async () => {
    renderAt('/floorplan/floorplan');
    await userEvent.click(screen.getByRole('tab', { name: 'Stations' }));
    expect(screen.getByTestId('stations')).toBeInTheDocument();
  });
});

// SpaceListView/FloorplanView/SpaceCapabilitiesTab own the "no site selected"
// fallback now — their dedicated component tests cover that branch.
vi.mock('@foundation/src/components/spaces/SpaceManagementPanel', () => ({
  SpaceManagementPanel: () => <div />,
}));
