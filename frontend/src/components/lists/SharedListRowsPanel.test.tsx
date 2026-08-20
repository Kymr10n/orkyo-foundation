import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { SharedListRowsPanel, type SharedListEntry } from './SharedListRowsPanel';
import type { ListDefinition, ListInstance } from '@foundation/src/lib/api/lists-api';

let definition: ListDefinition | null = null;
let instances: ListInstance[] = [];
let instance: ListInstance | null = null;
const definitionIdsAsked: (string | null)[] = [];
const sharedInstancesAsked: (string | null)[] = [];
const instanceIdsAsked: (string | null)[] = [];

vi.mock('@foundation/src/hooks/useListDefinitions', () => ({
  useListDefinition: (id: string | null) => {
    definitionIdsAsked.push(id);
    return { data: id ? definition : null };
  },
  useSharedListInstances: (id: string | null) => {
    sharedInstancesAsked.push(id);
    return { data: id ? instances : [] };
  },
}));
vi.mock('@foundation/src/hooks/useListRows', () => ({
  useListInstance: (id: string | null) => {
    instanceIdsAsked.push(id);
    return { data: id ? instance : null };
  },
}));
vi.mock('@foundation/src/hooks/usePermissions', () => ({ useCanEdit: () => true }));

vi.mock('@foundation/src/components/lists/ListRowsEditor', () => ({
  ListRowsEditor: ({
    instanceId,
    columns,
    toolbar,
    entityLabel,
  }: {
    instanceId: string | null;
    columns: unknown[];
    toolbar?: React.ReactNode;
    entityLabel?: string;
  }) => (
    // The selector rides in the editor's action row, so the stub has to render that slot or the
    // panel's only control disappears from the test.
    <div
      data-testid="rows-editor"
      data-instance={instanceId ?? ''}
      data-columns={columns.length}
      data-entity={entityLabel ?? ''}
    >
      {toolbar}
    </div>
  ),
}));

function renderPanel(entries: SharedListEntry[]) {
  return render(
    <SharedListRowsPanel entries={entries} selectId="test-list" emptyMessage="Nothing here." />,
  );
}

beforeEach(() => {
  vi.clearAllMocks();
  definitionIdsAsked.length = 0;
  sharedInstancesAsked.length = 0;
  instanceIdsAsked.length = 0;
  definition = {
    id: 'def-1',
    name: 'Departments',
    scope: 'organization',
    isActive: true,
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
    columns: [
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      { id: 'c1', key: 'name', label: 'Name', isActive: true } as any,
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      { id: 'c2', key: 'old', label: 'Retired', isActive: false } as any,
    ],
  } as ListDefinition;
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  instances = [{ id: 'inst-1', listDefinitionId: 'def-1', kind: 'shared', name: 'D' } as any];
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  instance = { id: 'inst-1', listDefinitionId: 'def-1', kind: 'shared', name: 'D' } as any;
});

describe('SharedListRowsPanel', () => {
  it('explains itself when there is nothing to select', () => {
    renderPanel([]);

    expect(screen.getByText('Nothing here.')).toBeInTheDocument();
    expect(screen.queryByTestId('rows-editor')).not.toBeInTheDocument();
  });

  it('opens on the first entry rather than on an empty frame', () => {
    renderPanel([
      { id: 'a', label: 'Departments', definitionId: 'def-1' },
      { id: 'b', label: 'Job Titles', definitionId: 'def-2' },
    ]);

    expect(screen.getByTestId('rows-editor')).toHaveAttribute('data-instance', 'inst-1');
  });

  it('switches the rows when another entry is chosen', async () => {
    renderPanel([
      { id: 'a', label: 'Departments', instanceId: 'inst-1' },
      { id: 'b', label: 'Job Titles', instanceId: 'inst-2' },
    ]);

    await userEvent.click(screen.getByLabelText('List'));
    await userEvent.click(await screen.findByRole('option', { name: 'Job Titles' }));

    expect(screen.getByTestId('rows-editor')).toHaveAttribute('data-instance', 'inst-2');
  });

  it('passes only the active columns to the editor', () => {
    renderPanel([{ id: 'a', label: 'Departments', definitionId: 'def-1' }]);

    // A retired column would render a cell nobody can fill.
    expect(screen.getByTestId('rows-editor')).toHaveAttribute('data-columns', '1');
  });

  it('resolves the instance from a definition-only entry', () => {
    renderPanel([{ id: 'a', label: 'Departments', definitionId: 'def-1' }]);

    expect(sharedInstancesAsked).toContain('def-1');
    // The definition is already known, so it is not looked up through the instance.
    expect(instanceIdsAsked.every((id) => id === null)).toBe(true);
  });

  it('resolves the definition from an instance-only entry', () => {
    // A `list_lookup` custom field carries an instance id and never a definition id — the
    // binding CHECK in migration 1780 forbids both.
    renderPanel([{ id: 'a', label: 'Tooling', instanceId: 'inst-1' }]);

    expect(instanceIdsAsked).toContain('inst-1');
    expect(definitionIdsAsked).toContain('def-1');
    expect(sharedInstancesAsked.every((id) => id === null)).toBe(true);
  });

  it('offers no editor when the definition has no shared instance yet', () => {
    instances = [];
    renderPanel([{ id: 'a', label: 'Departments', definitionId: 'def-empty' }]);

    // An editor here would put an Add button over a POST to a URL containing `null`.
    expect(screen.queryByTestId('rows-editor')).not.toBeInTheDocument();
    expect(screen.getByText(/has no shared list behind it yet/)).toBeInTheDocument();
  });

  it('shows no "List" heading above the selector', () => {
    // The selector's value already says which list this is; a word on top only repeats it.
    renderPanel([{ id: 'a', label: 'Departments', definitionId: 'def-1' }]);

    expect(screen.getByLabelText('List')).toBeInTheDocument();
    expect(screen.queryByText('List')).not.toBeInTheDocument();
  });

  it('puts the selector in the editor\'s action row', () => {
    renderPanel([{ id: 'a', label: 'Departments', definitionId: 'def-1' }]);

    expect(screen.getByTestId('rows-editor')).toContainElement(screen.getByLabelText('List'));
  });

  it('names one row after the list, singularised', () => {
    renderPanel([{ id: 'a', label: 'Departments', definitionId: 'def-1' }]);

    expect(screen.getByTestId('rows-editor')).toHaveAttribute('data-entity', 'Department');
  });

  it('keeps the selector reachable when the entry has no rows behind it', () => {
    instances = [];
    renderPanel([{ id: 'a', label: 'Departments', definitionId: 'def-empty' }]);

    // Otherwise a reader who lands on the empty entry cannot leave it.
    expect(screen.getByLabelText('List')).toBeInTheDocument();
  });
});
