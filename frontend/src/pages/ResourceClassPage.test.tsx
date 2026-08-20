import { describe, it, expect, vi, beforeEach } from 'vitest';
import { cleanup, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Routes, Route, Navigate, useLocation } from 'react-router';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { ResourceClassPage } from './ResourceClassPage';
import type { ResourceTypeInfo } from '@foundation/src/lib/api/resource-types-api';
import type { ResourceCustomField } from '@foundation/src/lib/api/resource-custom-fields-api';

let types: ResourceTypeInfo[] = [];
let customFields: ResourceCustomField[] = [];

vi.mock('@foundation/src/hooks/useResourceTypes', () => ({
  useResourceTypes: () => ({ data: types, isLoading: false }),
}));
vi.mock('@foundation/src/hooks/useResourceCustomFields', () => ({
  useResourceCustomFields: () => ({ data: customFields, isSuccess: true }),
}));

function Stub({ id }: { id: string }) {
  return <div data-testid={id} />;
}
function LocationProbe() {
  return <div data-testid="path">{useLocation().pathname}</div>;
}

function type(over: Partial<ResourceTypeInfo> & { key: string }): ResourceTypeInfo {
  return {
    id: `rt-${over.key}`,
    displayName: over.key,
    displayNamePlural: `${over.key}s`,
    hasGeometry: false,
    hasDirectoryProfile: false,
    singleGroupMembership: false,
    isSystem: false,
    isActive: true,
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
    ...over,
  } as ResourceTypeInfo;
}

function field(over: Partial<ResourceCustomField> = {}): ResourceCustomField {
  return {
    id: 'f-1',
    resourceTypeId: 'rt-mill',
    key: 'tooling',
    label: 'Tooling',
    dataType: 'list_lookup',
    isRequired: false,
    sortOrder: 0,
    isActive: true,
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
    ...over,
  } as ResourceCustomField;
}

/** The typeless floorplan surface: /stations/floorplan, with the canvas as the whole body. */
function renderFloorplan() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={['/stations/floorplan']}>
        <Routes>
          <Route
            path="/stations/floorplan"
            element={<ResourceClassPage resourceClass="station" surface="floorplan" />}
          >
            <Route index element={<Stub id="canvas" />} />
          </Route>
          <Route path="*" element={<LocationProbe />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

function renderAt(path: string, resourceClass: 'station' | 'asset' = 'station') {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const segment = resourceClass === 'station' ? 'stations' : 'assets';
  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={[path]}>
        <Routes>
          <Route path={`/${segment}/:typeKey`} element={<ResourceClassPage resourceClass={resourceClass} />}>
            <Route index element={<Navigate to="instances" replace />} />
            <Route path="instances" element={<Stub id="instances" />} />
            <Route path="groups" element={<Stub id="groups" />} />
            <Route path="lists" element={<Stub id="lists" />} />
          </Route>
          <Route path="*" element={<LocationProbe />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

beforeEach(() => {
  vi.clearAllMocks();
  types = [
    type({ key: 'mill', displayNamePlural: 'Mills', hasGeometry: true }),
    type({ key: 'drill', displayNamePlural: 'Drills', hasGeometry: true }),
    type({ key: 'person', displayNamePlural: 'People', hasDirectoryProfile: true }),
  ];
  customFields = [];
});

describe('ResourceClassPage', () => {
  it('offers only the types of its own class', async () => {
    renderAt('/stations/mill/instances');

    await userEvent.click(screen.getByLabelText('Type'));
    expect(await screen.findByRole('option', { name: 'Mills' })).toBeInTheDocument();
    expect(screen.getByRole('option', { name: 'Drills' })).toBeInTheDocument();
    // person is an asset; it has no business in the station selector.
    expect(screen.queryByRole('option', { name: 'People' })).not.toBeInTheDocument();
  });

  it('keeps the tab set stable across a type change', async () => {
    renderAt('/stations/mill/groups');
    const before = screen.getAllByRole('tab').map((t) => t.textContent);

    await userEvent.click(screen.getByLabelText('Type'));
    await userEvent.click(await screen.findByRole('option', { name: 'Drills' }));

    // Only the instances tab is named after the type; the rest hold their place.
    expect(screen.getAllByRole('tab').map((t) => t.textContent)).toEqual(
      before.map((l) => (l === 'Mills' ? 'Drills' : l)),
    );
  });

  it('stays on the same tab when the type changes', async () => {
    renderAt('/stations/mill/groups');

    await userEvent.click(screen.getByLabelText('Type'));
    await userEvent.click(await screen.findByRole('option', { name: 'Drills' }));

    expect(screen.getByTestId('groups')).toBeInTheDocument();
  });

  it('hides the Lists tab for a type with no lists', () => {
    renderAt('/stations/mill/instances');

    expect(screen.getAllByRole('tab').map((t) => t.textContent)).toEqual([
      'Mills', 'Groups', 'Floorplan',
    ]);
  });

  it('offers the floorplan on stations and never on assets', () => {
    renderAt('/stations/mill/instances');
    expect(screen.getAllByRole('tab').map((t) => t.textContent)).toContain('Floorplan');

    cleanup();
    renderAt('/assets/person/instances', 'asset');
    // The plan holds placeable resources; an asset has no place on it.
    expect(screen.getAllByRole('tab').map((t) => t.textContent)).not.toContain('Floorplan');
  });

  it('drops Lists when the chosen type has none', async () => {
    customFields = [field()];
    renderAt('/stations/mill/lists');
    expect(screen.getAllByRole('tab').map((t) => t.textContent)).toContain('Lists');

    // Switching to a type with no lists must not leave the strip pointing at a tab it lost.
    customFields = [];
    cleanup();
    renderAt('/stations/drill/lists');
    expect(await screen.findByTestId('instances')).toBeInTheDocument();
  });

  it('shows the Lists tab once a type binds one', () => {
    customFields = [field()];
    renderAt('/stations/mill/instances');

    expect(screen.getAllByRole('tab').map((t) => t.textContent)).toContain('Lists');
  });

  it('ignores a retired list field when deciding on the tab', () => {
    customFields = [field({ isActive: false })];
    renderAt('/stations/mill/instances');

    expect(screen.getAllByRole('tab').map((t) => t.textContent)).not.toContain('Lists');
  });

  it('redirects a type belonging to the other class', async () => {
    // person is an asset, so the station page cannot show it; it lands on the first station.
    renderAt('/stations/person/instances');

    expect(await screen.findByTestId('instances')).toBeInTheDocument();
    expect(screen.getAllByRole('tab')[0]).toHaveTextContent('Mills');
  });

  it('explains itself when the class has no types at all', () => {
    types = [type({ key: 'person', hasDirectoryProfile: true })];
    renderAt('/stations/mill/instances');

    expect(screen.getByText(/No stations are defined yet/)).toBeInTheDocument();
  });

  describe('the floorplan surface', () => {
    it('renders the canvas as the whole body', () => {
      renderFloorplan();

      expect(screen.getByTestId('canvas')).toBeInTheDocument();
    });

    it('highlights the Floorplan tab', () => {
      // Its URL has no third segment for useActiveTab to read, so the tab is named rather than
      // derived — without that the strip would light up Instances over the canvas.
      renderFloorplan();

      const active = screen.getAllByRole('tab').find((t) => t.getAttribute('data-state') === 'active');
      expect(active).toHaveTextContent('Floorplan');
    });

    it('hides the type selector', () => {
      // The canvas draws every placeable type at once; a control that picks one would do nothing.
      renderFloorplan();

      expect(screen.queryByLabelText('Type')).not.toBeInTheDocument();
    });

    it('keeps the same tab strip as the type surfaces', () => {
      renderFloorplan();

      expect(screen.getAllByRole('tab').map((t) => t.textContent)).toEqual([
        'Mills', 'Groups', 'Floorplan',
      ]);
    });

    it('sends the other tabs to the first station type', async () => {
      renderFloorplan();

      await userEvent.click(screen.getByRole('tab', { name: 'Groups' }));

      expect(await screen.findByTestId('path')).toHaveTextContent('/stations/mill/groups');
    });
  });
});
