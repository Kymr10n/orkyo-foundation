import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { PersonAbsenceEditDialog } from './PersonAbsenceEditDialog';

vi.mock('@foundation/src/lib/api/resource-absences-api', () => ({
  createResourceAbsence: vi.fn(),
}));

import { createResourceAbsence } from '@foundation/src/lib/api/resource-absences-api';

function renderDialog(props: Partial<React.ComponentProps<typeof PersonAbsenceEditDialog>> = {}) {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return render(
    <QueryClientProvider client={queryClient}>
      <PersonAbsenceEditDialog
        personId="person-alice"
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
    vi.mocked(createResourceAbsence).mockResolvedValue({
      id: 'abs-1',
      resourceId: 'person-alice',
      absenceType: 'vacation',
      title: 'vacation',
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
    expect(screen.getByText('Type')).toBeInTheDocument();
    expect(screen.getByText(/Start Date/)).toBeInTheDocument();
    expect(screen.getByText(/End Date/)).toBeInTheDocument();
  });

  it('does not render a site picker', () => {
    renderDialog();
    expect(screen.queryByText(/Site/i)).not.toBeInTheDocument();
  });

  it('Save button is disabled before dates are selected', () => {
    renderDialog();
    expect(screen.getByRole('button', { name: /Save/i })).toBeDisabled();
  });

  it('Cancel button calls onClose', () => {
    const onClose = vi.fn();
    renderDialog({ onClose });
    fireEvent.click(screen.getByRole('button', { name: /Cancel/i }));
    expect(onClose).toHaveBeenCalled();
  });

  it('calls createResourceAbsence with personId from props (no siteId)', async () => {
    renderDialog({ personId: 'person-bob' });

    vi.mocked(createResourceAbsence).mockImplementation((resourceId, req) => {
      expect(resourceId).toBe('person-bob');
      expect(req).not.toHaveProperty('siteId');
      return Promise.resolve({
        id: 'abs-2', resourceId: 'person-bob',
        absenceType: 'vacation' as const, title: 'vacation',
        startTs: '', endTs: '', isRecurring: false, enabled: true,
        createdAt: '', updatedAt: '',
      });
    });
  });
});
