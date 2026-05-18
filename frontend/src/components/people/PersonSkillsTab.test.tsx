/** @jsxImportSource react */
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactNode } from 'react';
import { PersonSkillsTab } from './PersonSkillsTab';
import type { ResourceInfo, ResourcesResponse } from '@foundation/src/lib/api/resources-api';
import type { ResourceCapability } from '@foundation/src/lib/api/resource-capabilities-api';

vi.mock('@foundation/src/lib/api/resources-api', () => ({
  getResources: vi.fn(),
}));
vi.mock('@foundation/src/lib/api/resource-capabilities-api', () => ({
  getResourceCapabilities: vi.fn(),
}));
// PersonSkillsEditor is exercised in its own test; mock here so we only verify mount.
vi.mock('./PersonSkillsEditor', () => ({
  PersonSkillsEditor: ({ personName }: { personName: string }) => (
    <div data-testid="editor">editor:{personName}</div>
  ),
}));

import { getResources } from '@foundation/src/lib/api/resources-api';
import { getResourceCapabilities } from '@foundation/src/lib/api/resource-capabilities-api';

function person(id: string, name: string): ResourceInfo {
  return {
    id,
    resourceTypeId: 'rt-person',
    resourceTypeKey: 'person',
    name,
    allocationMode: 'Exclusive',
    baseAvailabilityPercent: 100,
    isActive: true,
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
  };
}

function makeResponse(items: ResourceInfo[]): ResourcesResponse {
  return { data: items, total: items.length, page: 1, pageSize: 100 };
}

function makeCapability(criterionId: string): ResourceCapability {
  return {
    id: `cap-${criterionId}`,
    resourceId: 'p-1',
    criterionId,
    value: true,
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
    criterion: { id: criterionId, name: 'First-aid', dataType: 'Boolean' },
  };
}

function createWrapper() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
  );
}

describe('PersonSkillsTab', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(getResources).mockResolvedValue(makeResponse([person('p-1', 'Alice'), person('p-2', 'Bob')]));
    vi.mocked(getResourceCapabilities).mockResolvedValue([makeCapability('c-1')]);
  });

  it('fetches active person resources via getResources', async () => {
    render(<PersonSkillsTab />, { wrapper: createWrapper() });
    await waitFor(() => {
      expect(getResources).toHaveBeenCalledWith({ resourceTypeKey: 'person', isActive: true });
    });
  });

  it('lists every person and their skill count', async () => {
    render(<PersonSkillsTab />, { wrapper: createWrapper() });
    await waitFor(() => expect(screen.getByText('Alice')).toBeInTheDocument());
    expect(screen.getByText('Bob')).toBeInTheDocument();
    // 1 capability for each person → "1 skill"
    const counts = await screen.findAllByText('1 skill');
    expect(counts).toHaveLength(2);
  });

  it('shows empty state when no persons exist', async () => {
    vi.mocked(getResources).mockResolvedValue(makeResponse([]));
    render(<PersonSkillsTab />, { wrapper: createWrapper() });
    await waitFor(() => {
      expect(screen.getByText('No people defined yet.')).toBeInTheDocument();
    });
  });

  it('clicking Manage skills opens the editor for that person', async () => {
    const user = userEvent.setup();
    render(<PersonSkillsTab />, { wrapper: createWrapper() });

    await waitFor(() => expect(screen.getByText('Alice')).toBeInTheDocument());
    const buttons = await screen.findAllByRole('button', { name: /Manage skills/i });
    await user.click(buttons[0]);

    expect(screen.getByTestId('editor').textContent).toBe('editor:Alice');
  });
});
