import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { ListDefinitionEditDialog } from './ListDefinitionEditDialog';
import type { ListDefinition } from '@foundation/src/lib/api/lists-api';

const createDefinition = vi.fn();
const updateDefinition = vi.fn();

vi.mock('@foundation/src/hooks/useListDefinitions', () => ({
  useCreateListDefinition: () => ({ mutateAsync: createDefinition }),
  useUpdateListDefinition: () => ({ mutateAsync: updateDefinition }),
  useListDefinition: () => ({ data: null }),
}));

vi.mock('@foundation/src/hooks/useResourceTypes', () => ({
  useResourceTypes: () => ({
    data: [
      { id: 'rt-mill', key: 'mill', displayName: 'Mill', displayNamePlural: 'Mills' },
      { id: 'rt-person', key: 'person', displayName: 'Person', displayNamePlural: 'People' },
    ],
    isLoading: false,
  }),
}));

function renderDialog(definition: ListDefinition | null = null) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <ListDefinitionEditDialog open onOpenChange={() => {}} definition={definition} />
    </QueryClientProvider>,
  );
}

beforeEach(() => {
  vi.clearAllMocks();
  createDefinition.mockResolvedValue({ id: 'def-new' });
  updateDefinition.mockResolvedValue({});
});

describe('ListDefinitionEditDialog scope', () => {
  it('creates a common definition without naming a type', async () => {
    renderDialog();

    await userEvent.type(screen.getByLabelText(/Name/), 'Countries');
    await userEvent.click(screen.getByRole('button', { name: 'Create' }));

    await waitFor(() => expect(createDefinition).toHaveBeenCalled());
    expect(createDefinition.mock.calls[0][0]).toMatchObject({ name: 'Countries', scope: 'common' });
    // The server rejects a type on a scope that owns none, so it must not be sent.
    expect(createDefinition.mock.calls[0][0]).not.toHaveProperty('resourceTypeId');
  });

  it('asks for the owning type once the resource scope is chosen', async () => {
    renderDialog();

    expect(screen.queryByLabelText(/Resource type/)).not.toBeInTheDocument();
    await userEvent.click(screen.getByLabelText('Scope'));
    await userEvent.click(await screen.findByRole('option', { name: 'Resource type' }));

    expect(await screen.findByLabelText(/Resource type/)).toBeInTheDocument();
  });

  it('blocks the save until a resource-scoped definition names its type', async () => {
    renderDialog();

    await userEvent.type(screen.getByLabelText(/Name/), 'Tooling');
    await userEvent.click(screen.getByLabelText('Scope'));
    await userEvent.click(await screen.findByRole('option', { name: 'Resource type' }));

    expect(screen.getByRole('button', { name: 'Create' })).toBeDisabled();

    await userEvent.click(screen.getByLabelText(/Resource type/));
    await userEvent.click(await screen.findByRole('option', { name: 'Mills' }));

    expect(screen.getByRole('button', { name: 'Create' })).toBeEnabled();
  });

  it('sends the owning type for a resource-scoped definition', async () => {
    renderDialog();

    await userEvent.type(screen.getByLabelText(/Name/), 'Tooling');
    await userEvent.click(screen.getByLabelText('Scope'));
    await userEvent.click(await screen.findByRole('option', { name: 'Resource type' }));
    await userEvent.click(screen.getByLabelText(/Resource type/));
    await userEvent.click(await screen.findByRole('option', { name: 'Mills' }));
    await userEvent.click(screen.getByRole('button', { name: 'Create' }));

    await waitFor(() => expect(createDefinition).toHaveBeenCalled());
    expect(createDefinition.mock.calls[0][0]).toMatchObject({
      scope: 'resource',
      resourceTypeId: 'rt-mill',
    });
  });

  it('drops a chosen type when the scope moves away from resource', async () => {
    renderDialog();

    await userEvent.type(screen.getByLabelText(/Name/), 'Departments');
    await userEvent.click(screen.getByLabelText('Scope'));
    await userEvent.click(await screen.findByRole('option', { name: 'Resource type' }));
    await userEvent.click(screen.getByLabelText(/Resource type/));
    await userEvent.click(await screen.findByRole('option', { name: 'Mills' }));

    await userEvent.click(screen.getByLabelText('Scope'));
    await userEvent.click(await screen.findByRole('option', { name: 'Organization' }));
    await userEvent.click(screen.getByRole('button', { name: 'Create' }));

    await waitFor(() => expect(createDefinition).toHaveBeenCalled());
    expect(createDefinition.mock.calls[0][0]).toMatchObject({ scope: 'organization' });
    expect(createDefinition.mock.calls[0][0]).not.toHaveProperty('resourceTypeId');
  });

  it('hides the scope selector while editing, because ownership is fixed', () => {
    renderDialog({
      id: 'def-1',
      name: 'Tooling',
      scope: 'resource',
      resourceTypeId: 'rt-mill',
      isActive: true,
      createdAt: '2026-01-01T00:00:00Z',
      updatedAt: '2026-01-01T00:00:00Z',
      columns: [],
    } as ListDefinition);

    expect(screen.queryByLabelText('Scope')).not.toBeInTheDocument();
  });
});
