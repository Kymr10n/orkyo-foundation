import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import { OrganizationPage } from './OrganizationPage';
import type { ListDefinition, ListDefinitionScope } from '@foundation/src/lib/api/lists-api';

let definitions: ListDefinition[] = [];

// Scope and activity are filtered server-side now; the mock replays that filter so the
// tests can assert the page asks for exactly the slice it renders.
const useListDefinitionsMock = vi.fn((includeInactive: boolean = false, scope?: ListDefinitionScope) => ({
  data: definitions.filter(
    (d) => (includeInactive || d.isActive) && (scope === undefined || d.scope === scope),
  ),
  isLoading: false,
  error: null,
}));

vi.mock('@foundation/src/hooks/useListDefinitions', () => ({
  useListDefinitions: (includeInactive?: boolean, scope?: ListDefinitionScope) =>
    useListDefinitionsMock(includeInactive, scope),
}));

// The panel has its own suite; here it only reports which entries it was handed.
vi.mock('@foundation/src/components/lists/SharedListRowsPanel', () => ({
  SharedListRowsPanel: ({ entries }: { entries: { label: string; definitionId: string }[] }) => (
    <div data-testid="panel" data-labels={entries.map((e) => e.label).join(',')} />
  ),
}));

function definition(over: Partial<ListDefinition> & { id: string; name: string }): ListDefinition {
  return {
    scope: 'organization',
    isActive: true,
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
    columns: [],
    ...over,
  } as ListDefinition;
}

beforeEach(() => {
  vi.clearAllMocks();
  definitions = [
    definition({ id: 'd-dept', name: 'Departments' }),
    definition({ id: 'd-job', name: 'Job Titles' }),
  ];
});

describe('OrganizationPage', () => {
  it('asks the server for active organization-scoped definitions only', () => {
    render(<OrganizationPage />);

    expect(useListDefinitionsMock).toHaveBeenCalledWith(false, 'organization');
  });

  it('hands the panel every organization-scoped definition', () => {
    render(<OrganizationPage />);

    expect(screen.getByTestId('panel')).toHaveAttribute('data-labels', 'Departments,Job Titles');
  });

  it('leaves out definitions of another scope', () => {
    definitions = [
      definition({ id: 'd-dept', name: 'Departments' }),
      definition({ id: 'd-tool', name: 'Tooling Catalog', scope: 'common' }),
      definition({ id: 'd-cert', name: 'Certification', scope: 'resource' }),
    ];
    render(<OrganizationPage />);

    expect(screen.getByTestId('panel')).toHaveAttribute('data-labels', 'Departments');
  });

  it('hides inactive definitions', () => {
    definitions = [
      definition({ id: 'd-dept', name: 'Departments' }),
      definition({ id: 'd-old', name: 'Retired List', isActive: false }),
    ];
    render(<OrganizationPage />);

    expect(screen.getByTestId('panel')).toHaveAttribute('data-labels', 'Departments');
  });

  it('still renders the panel with nothing to show, so it can explain itself', () => {
    definitions = [];
    render(<OrganizationPage />);

    expect(screen.getByTestId('panel')).toHaveAttribute('data-labels', '');
  });
});
