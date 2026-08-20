import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Routes, Route, Navigate } from 'react-router';
import { ConfigurationPage } from './ConfigurationPage';

function Stub({ id }: { id: string }) {
  return <div data-testid={id} />;
}

function renderAt(initialPath: string) {
  return render(
    <MemoryRouter initialEntries={[initialPath]}>
      <Routes>
        <Route path="/configuration" element={<ConfigurationPage />}>
          <Route index element={<Navigate to="resource-types" replace />} />
          <Route path="resource-types" element={<Stub id="resource-types" />} />
          <Route path="catalog" element={<Stub id="catalog" />} />
          <Route path="list-definitions" element={<Stub id="list-definitions" />} />
        </Route>
      </Routes>
    </MemoryRouter>,
  );
}

describe('ConfigurationPage', () => {
  it('defaults to resource types', () => {
    renderAt('/configuration');

    expect(screen.getByTestId('resource-types')).toBeInTheDocument();
  });

  it('offers all tabs of the section', () => {
    renderAt('/configuration/resource-types');

    expect(screen.getByRole('tab', { name: 'Resource Types' })).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: 'Type catalog' })).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: 'List definitions' })).toBeInTheDocument();
  });

  it('switches to the type catalog', async () => {
    const user = userEvent.setup();
    renderAt('/configuration/resource-types');

    await user.click(screen.getByRole('tab', { name: 'Type catalog' }));

    expect(await screen.findByTestId('catalog')).toBeInTheDocument();
  });

  it('switches to list definitions', async () => {
    const user = userEvent.setup();
    renderAt('/configuration/resource-types');

    await user.click(screen.getByRole('tab', { name: 'List definitions' }));

    expect(await screen.findByTestId('list-definitions')).toBeInTheDocument();
  });

  it('is titled Resources even though it lives at /configuration', () => {
    // The label and the path differ on purpose: /resources belongs to the per-type resource
    // pages, and Administration sets the precedent for a section named unlike its route.
    renderAt('/configuration/list-definitions');

    expect(screen.getByRole('heading', { name: 'Resources' })).toBeInTheDocument();
  });
});
