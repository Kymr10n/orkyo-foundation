/* eslint-disable @typescript-eslint/no-explicit-any */
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor, fireEvent } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { AvailabilityEventDialog } from './AvailabilityEventDialog';
import type { AvailabilityEventInfo } from '@foundation/src/lib/api/availability-events-api';

// ── API mocks ─────────────────────────────────────────────────────────────────

vi.mock('@foundation/src/lib/api/resources-api', () => ({
  getResources: vi.fn(() => Promise.resolve({ data: [], total: 0 })),
}));
vi.mock('@foundation/src/lib/api/resource-groups-api', () => ({
  getResourceGroups: vi.fn(() => Promise.resolve([])),
}));
vi.mock('@foundation/src/lib/core/api-client', () => ({
  apiGet: vi.fn(() => Promise.resolve([])),
}));
vi.mock('@foundation/src/lib/core/api-paths', () => ({
  API_PATHS: { RESOURCE_TYPES: '/api/resource-types' },
}));
vi.mock('@foundation/src/lib/api/availability-events-api', () => ({
  addAvailabilityEventScope: vi.fn(() => Promise.resolve()),
  deleteAvailabilityEventScope: vi.fn(() => Promise.resolve()),
}));

// ── UI mocks ──────────────────────────────────────────────────────────────────

vi.mock('@foundation/src/components/ui/dialog', () => ({
  DIALOG_SIZE: { sm: '', md: '', lg: '', xl: '' },
  Dialog: ({ children, open }: { children: ReactNode; open: boolean }) =>
    open ? <div role="dialog">{children}</div> : null,
  DialogContent: ({ children }: { children: ReactNode }) => <div>{children}</div>,
  DialogHeader: ({ children }: { children: ReactNode }) => <div>{children}</div>,
  DialogTitle: ({ children }: { children: ReactNode }) => <h2>{children}</h2>,
  DialogDescription: ({ children }: { children: ReactNode }) => <p>{children}</p>,
}));

vi.mock('@foundation/src/components/ui/date-time-picker', () => ({
  DateTimePicker: ({
    id,
    value,
    onChange,
    placeholder,
  }: {
    id: string;
    value: string;
    onChange: (v: string) => void;
    placeholder?: string;
  }) => (
    <input
      data-testid={`picker-${id}`}
      id={id}
      value={value ?? ''}
      onChange={(e) => onChange(e.target.value)}
      placeholder={placeholder}
    />
  ),
}));

vi.mock('@foundation/src/components/ui/combobox', () => ({
  Combobox: ({ onChange, placeholder }: any) => (
    <select data-testid="combobox" onChange={(e) => onChange(e.target.value)}>
      <option value="">{placeholder}</option>
    </select>
  ),
}));

// ── Helpers ───────────────────────────────────────────────────────────────────

function makeQC() {
  return new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
}

function renderDialog(props: Partial<Parameters<typeof AvailabilityEventDialog>[0]> = {}) {
  return render(
    <QueryClientProvider client={makeQC()}>
      <AvailabilityEventDialog
        open
        onOpenChange={vi.fn()}
        siteId="site-1"
        event={null}
        onSave={vi.fn(() => Promise.resolve())}
        {...props}
      />
    </QueryClientProvider>,
  );
}

const existingEvent: AvailabilityEventInfo = {
  id: 'evt-1',
  siteId: 'site-1',
  title: 'Christmas Shutdown',
  eventType: 'shutdown',
  defaultEffect: 'closed',
  startTs: '2026-12-24T00:00:00.000Z',
  endTs: '2026-12-26T00:00:00.000Z',
  isRecurring: false,
  recurrenceRule: undefined,
  enabled: true,
  scopes: [],
  createdAt: '2026-01-01T00:00:00.000Z',
  updatedAt: '2026-01-01T00:00:00.000Z',
};

// ── Tests ─────────────────────────────────────────────────────────────────────

describe('AvailabilityEventDialog — create mode', () => {
  beforeEach(() => vi.clearAllMocks());

  it('renders with "Add Availability Event" heading', () => {
    renderDialog();
    expect(screen.getByRole('heading', { name: /add availability event/i })).toBeInTheDocument();
  });

  it('renders title input, type selector, start and end pickers', () => {
    renderDialog();
    expect(screen.getByLabelText(/title/i)).toBeInTheDocument();
    expect(screen.getByTestId('picker-ae-start')).toBeInTheDocument();
    expect(screen.getByTestId('picker-ae-end')).toBeInTheDocument();
  });

  it('does not render when closed', () => {
    renderDialog({ open: false });
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('shows "Create" submit button', () => {
    renderDialog();
    expect(screen.getByRole('button', { name: /^create$/i })).toBeInTheDocument();
  });

  it('shows error when title is empty on submit', () => {
    renderDialog();
    fireEvent.submit(screen.getByRole('dialog').querySelector('form')!);
    expect(screen.getByText('Title is required.')).toBeInTheDocument();
  });

  it('shows error when dates are missing on submit', () => {
    renderDialog();
    fireEvent.change(screen.getByLabelText(/title/i), { target: { value: 'Holiday' } });
    fireEvent.submit(screen.getByRole('dialog').querySelector('form')!);
    expect(screen.getByText(/start and end dates are required/i)).toBeInTheDocument();
  });

  it('shows error when start >= end', () => {
    renderDialog();
    fireEvent.change(screen.getByLabelText(/title/i), { target: { value: 'Holiday' } });
    fireEvent.change(screen.getByTestId('picker-ae-start'), { target: { value: '2026-12-26T00:00' } });
    fireEvent.change(screen.getByTestId('picker-ae-end'), { target: { value: '2026-12-24T00:00' } });
    fireEvent.submit(screen.getByRole('dialog').querySelector('form')!);
    expect(screen.getByText(/start must be before end/i)).toBeInTheDocument();
  });

  it('calls onSave with correct payload on valid submit', async () => {
    const onSave = vi.fn(() => Promise.resolve());
    renderDialog({ onSave });
    fireEvent.change(screen.getByLabelText(/title/i), { target: { value: 'Summer Break' } });
    fireEvent.change(screen.getByTestId('picker-ae-start'), { target: { value: '2026-07-01T00:00' } });
    fireEvent.change(screen.getByTestId('picker-ae-end'), { target: { value: '2026-07-15T00:00' } });
    fireEvent.submit(screen.getByRole('dialog').querySelector('form')!);
    await waitFor(() =>
      expect(onSave).toHaveBeenCalledWith(
        expect.objectContaining({ title: 'Summer Break' }),
      ),
    );
  });

  it('shows error when onSave throws', async () => {
    const onSave = vi.fn(() => Promise.reject(new Error('API error')));
    renderDialog({ onSave });
    fireEvent.change(screen.getByLabelText(/title/i), { target: { value: 'Break' } });
    fireEvent.change(screen.getByTestId('picker-ae-start'), { target: { value: '2026-07-01T00:00' } });
    fireEvent.change(screen.getByTestId('picker-ae-end'), { target: { value: '2026-07-15T00:00' } });
    fireEvent.submit(screen.getByRole('dialog').querySelector('form')!);
    await waitFor(() => expect(screen.getByText('API error')).toBeInTheDocument());
  });

  it('calls onOpenChange when Cancel is clicked', () => {
    const onOpenChange = vi.fn();
    renderDialog({ onOpenChange });
    fireEvent.click(screen.getByRole('button', { name: /cancel/i }));
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  it('shows recurrence rule input when recurring is toggled on', async () => {
    const user = userEvent.setup();
    renderDialog();
    const recurringSwitch = screen.getByLabelText(/recurring/i);
    await user.click(recurringSwitch);
    expect(screen.getByLabelText(/recurrence rule/i)).toBeInTheDocument();
  });

  it('shows error when recurring enabled but no rule provided', async () => {
    const user = userEvent.setup();
    renderDialog();
    fireEvent.change(screen.getByLabelText(/title/i), { target: { value: 'Annual' } });
    fireEvent.change(screen.getByTestId('picker-ae-start'), { target: { value: '2026-12-24T00:00' } });
    fireEvent.change(screen.getByTestId('picker-ae-end'), { target: { value: '2026-12-26T00:00' } });
    await user.click(screen.getByLabelText(/recurring/i));
    fireEvent.submit(screen.getByRole('dialog').querySelector('form')!);
    expect(screen.getByText(/recurrence rule is required/i)).toBeInTheDocument();
  });
});

describe('AvailabilityEventDialog — edit mode', () => {
  beforeEach(() => vi.clearAllMocks());

  it('renders with "Edit Availability Event" heading', () => {
    renderDialog({ event: existingEvent });
    expect(screen.getByRole('heading', { name: /edit availability event/i })).toBeInTheDocument();
  });

  it('pre-fills title from existing event', () => {
    renderDialog({ event: existingEvent });
    expect(screen.getByLabelText(/title/i)).toHaveValue('Christmas Shutdown');
  });

  it('shows "Update" submit button in edit mode', () => {
    renderDialog({ event: existingEvent });
    expect(screen.getByRole('button', { name: /^update$/i })).toBeInTheDocument();
  });

  it('shows "No overrides" message when event has no scopes', () => {
    renderDialog({ event: existingEvent });
    expect(screen.getByText(/no overrides/i)).toBeInTheDocument();
  });

  it('shows the default effect description text', () => {
    renderDialog({ event: existingEvent });
    expect(screen.getByText(/all resources closed unless overridden/i)).toBeInTheDocument();
  });
});
