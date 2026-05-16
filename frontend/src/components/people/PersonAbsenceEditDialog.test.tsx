import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { PersonAbsenceEditDialog } from './PersonAbsenceEditDialog';
import type { ResourceInfo, ResourcesResponse } from '@foundation/src/lib/api/resources-api';

vi.mock('@foundation/src/lib/api/resources-api', () => ({
  getResources: vi.fn(),
}));

vi.mock('@foundation/src/lib/api/resource-absences-api', () => ({
  createResourceAbsence: vi.fn(),
}));

import { getResources } from '@foundation/src/lib/api/resources-api';
import { createResourceAbsence } from '@foundation/src/lib/api/resource-absences-api';

const mockPeople: ResourceInfo[] = [
  {
    id: 'person-alice',
    resourceTypeId: 'rt-person',
    resourceTypeKey: 'person',
    name: 'Alice',
    allocationMode: 'Exclusive',
    baseAvailabilityPercent: 100,
    isActive: true,
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
  },
];

const emptyResponse: ResourcesResponse = { data: [], total: 0, page: 1, pageSize: 50 };
const peopleResponse: ResourcesResponse = { data: mockPeople, total: 1, page: 1, pageSize: 50 };

function renderDialog(props: Partial<React.ComponentProps<typeof PersonAbsenceEditDialog>> = {}) {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return render(
    <QueryClientProvider client={queryClient}>
      <PersonAbsenceEditDialog
        siteId="site-1"
        isOpen={true}
        onClose={() => {}}
        onSaved={() => {}}
        {...props}
      />
    </QueryClientProvider>,
  );
}

describe('PersonAbsenceEditDialog', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(getResources).mockResolvedValue(peopleResponse);
    vi.mocked(createResourceAbsence).mockResolvedValue({
      id: 'abs-1',
      siteId: 'site-1',
      title: 'Vacation',
      type: 'vacation',
      appliesToAllResources: false,
      resourceIds: ['person-alice'],
      startTs: '2026-06-01T00:00:00Z',
      endTs: '2026-06-07T00:00:00Z',
      isRecurring: false,
      enabled: true,
      createdAt: '2026-01-01T00:00:00Z',
      updatedAt: '2026-01-01T00:00:00Z',
    });
  });

  it('renders dialog title and required form fields', () => {
    renderDialog();
    expect(screen.getByText('Add Absence')).toBeInTheDocument();
    expect(screen.getByText(/Person \*/)).toBeInTheDocument();
    expect(screen.getByText('Type')).toBeInTheDocument();
    expect(screen.getByText(/Start Date \*/)).toBeInTheDocument();
    expect(screen.getByText(/End Date \*/)).toBeInTheDocument();
  });

  it('fetches person list from getResources when open', async () => {
    renderDialog();
    await waitFor(() =>
      expect(getResources).toHaveBeenCalledWith({ resourceTypeKey: 'person' }),
    );
  });

  it('does not fetch when dialog is closed', () => {
    renderDialog({ isOpen: false });
    expect(getResources).not.toHaveBeenCalled();
  });

  it('Save button is disabled before person and dates are selected', () => {
    renderDialog();
    expect(screen.getByRole('button', { name: /Save/i })).toBeDisabled();
  });

  it('Cancel button calls onClose', () => {
    const onClose = vi.fn();
    renderDialog({ onClose });
    fireEvent.click(screen.getByRole('button', { name: /Cancel/i }));
    expect(onClose).toHaveBeenCalled();
  });

  it('shows no hardcoded person entries when people list is empty', async () => {
    vi.mocked(getResources).mockResolvedValue(emptyResponse);
    renderDialog();
    await waitFor(() => expect(getResources).toHaveBeenCalled());
    expect(screen.queryByText('Person 1')).not.toBeInTheDocument();
  });
});
