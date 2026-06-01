import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { PersonAbsenceEditDialog } from './PersonAbsenceEditDialog';

vi.mock('@foundation/src/lib/api/resource-absences-api', () => ({
  createResourceAbsence: vi.fn(),
}));

// Mock Calendar so tests can select dates without real DOM date-picker interaction
vi.mock('@foundation/src/components/ui/calendar', () => ({
  Calendar: ({ onSelect }: { onSelect: (d: Date) => void; selected?: Date }) => (
    <button type="button" data-testid="mock-calendar" onClick={() => onSelect(new Date('2026-06-01'))}>
      Pick date
    </button>
  ),
}));
vi.mock('@foundation/src/components/ui/popover', () => ({
  Popover: ({ children }: { children: ReactNode }) => <div>{children}</div>,
  PopoverTrigger: ({ children }: { asChild?: boolean; children: ReactNode }) => <div>{children}</div>,
  PopoverContent: ({ children }: { children: ReactNode }) => <div>{children}</div>,
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

  it('does not render when isOpen is false', () => {
    renderDialog({ isOpen: false });
    expect(screen.queryByText('Add Absence')).not.toBeInTheDocument();
  });

  it('Save button becomes enabled after both dates are selected', async () => {
    renderDialog();
    const calendarBtns = screen.getAllByTestId('mock-calendar');
    fireEvent.click(calendarBtns[0]); // set start date
    fireEvent.click(calendarBtns[1]); // set end date
    await waitFor(() =>
      expect(screen.getByRole('button', { name: /^save$/i })).not.toBeDisabled(),
    );
  });

  it('calls createResourceAbsence and onSaved after successful save', async () => {
    const onSaved = vi.fn();
    renderDialog({ onSaved });
    const calendarBtns = screen.getAllByTestId('mock-calendar');
    fireEvent.click(calendarBtns[0]);
    fireEvent.click(calendarBtns[1]);
    await waitFor(() => expect(screen.getByRole('button', { name: /^save$/i })).not.toBeDisabled());
    fireEvent.click(screen.getByRole('button', { name: /^save$/i }));
    await waitFor(() => expect(createResourceAbsence).toHaveBeenCalledWith(
      'person-alice',
      expect.objectContaining({ absenceType: 'vacation' }),
    ));
    await waitFor(() => expect(onSaved).toHaveBeenCalled());
  });

  it('submits title in payload when reason is filled', async () => {
    const user = userEvent.setup();
    renderDialog();
    await user.type(screen.getByPlaceholderText('Optional'), 'Annual leave');
    const calendarBtns = screen.getAllByTestId('mock-calendar');
    fireEvent.click(calendarBtns[0]);
    fireEvent.click(calendarBtns[1]);
    await waitFor(() => expect(screen.getByRole('button', { name: /^save$/i })).not.toBeDisabled());
    fireEvent.click(screen.getByRole('button', { name: /^save$/i }));
    await waitFor(() =>
      expect(createResourceAbsence).toHaveBeenCalledWith(
        'person-alice',
        expect.objectContaining({ title: 'Annual leave' }),
      ),
    );
  });

  it('uses absenceType as title when reason is empty', async () => {
    renderDialog();
    const calendarBtns = screen.getAllByTestId('mock-calendar');
    fireEvent.click(calendarBtns[0]);
    fireEvent.click(calendarBtns[1]);
    await waitFor(() => expect(screen.getByRole('button', { name: /^save$/i })).not.toBeDisabled());
    fireEvent.click(screen.getByRole('button', { name: /^save$/i }));
    await waitFor(() =>
      expect(createResourceAbsence).toHaveBeenCalledWith(
        'person-alice',
        expect.objectContaining({ title: 'vacation' }),
      ),
    );
  });

  it('shows formatted date after start date is picked', async () => {
    renderDialog();
    fireEvent.click(screen.getAllByTestId('mock-calendar')[0]);
    // date-fns PP: "Jun 1, 2026" (no leading zero for single-digit days)
    await waitFor(() => expect(screen.getByText(/Jun 1, 2026/)).toBeInTheDocument());
  });
});
