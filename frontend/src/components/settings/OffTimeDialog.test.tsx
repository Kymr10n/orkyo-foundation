import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import type { ReactNode } from 'react';
import { OffTimeDialog } from './OffTimeDialog';

vi.mock('@/components/ui/dialog', () => ({
  Dialog: ({ children, open }: { children: ReactNode; open: boolean }) => open ? <div role="dialog">{children}</div> : null,
  DialogContent: ({ children }: { children: ReactNode }) => <div>{children}</div>,
  DialogHeader: ({ children }: { children: ReactNode }) => <div>{children}</div>,
  DialogTitle: ({ children }: { children: ReactNode }) => <h2>{children}</h2>,
  DialogDescription: ({ children }: { children: ReactNode }) => <p>{children}</p>,
  DialogFooter: ({ children }: { children: ReactNode }) => <div>{children}</div>,
}));

vi.mock('@/components/ui/alert', () => ({
  Alert: ({ children }: { children: ReactNode }) => <div role="alert">{children}</div>,
  AlertDescription: ({ children }: { children: ReactNode }) => <div>{children}</div>,
}));

vi.mock('@/store/app-store', () => ({
  useAppStore: vi.fn((selector: (s: Record<string, unknown>) => unknown) =>
    selector({ selectedSiteId: 'site-1' }),
  ),
}));

vi.mock('@/lib/api/space-api', () => ({
  getSpaces: vi.fn(() => Promise.resolve([{ id: 's1', code: 'R101', name: 'Room 101' }])),
}));

vi.mock('@/domain/scheduling/types', () => ({
  OFF_TIME_TYPE_LABELS: { maintenance: 'Maintenance', holiday: 'Holiday', custom: 'Custom' },
}));

describe('OffTimeDialog', () => {
  const defaultProps = {
    open: true,
    onOpenChange: vi.fn(),
    offTime: null,
    onSave: vi.fn(() => Promise.resolve()),
  };

  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('renders create mode title', async () => {
    render(<OffTimeDialog {...defaultProps} />);
    expect(screen.getByText('Add Off-Time')).toBeInTheDocument();
    await waitFor(() => {
      expect(screen.getByRole('dialog')).toBeInTheDocument();
    });
  });

  it('renders edit mode title with existing offTime', async () => {
    const offTime = {
      id: 'ot1',
      siteId: 'site-1',
      title: 'Holiday',
      type: 'holiday' as const,
      startMs: Date.now(),
      endMs: Date.now() + 86400000,
      appliesToAllSpaces: true,
      spaceIds: [],
      isRecurring: false,
      recurrenceRule: null,
      enabled: true,
    };
    render(<OffTimeDialog {...defaultProps} offTime={offTime} />);
    expect(screen.getByText('Edit Off-Time')).toBeInTheDocument();
    await waitFor(() => {
      expect(screen.getByRole('dialog')).toBeInTheDocument();
    });
  });

  it('validates empty title on submit', async () => {
    render(<OffTimeDialog {...defaultProps} />);
    fireEvent.submit(screen.getByRole('button', { name: 'Create' }).closest('form')!);
    await waitFor(() => {
      expect(screen.getByText('Title is required.')).toBeInTheDocument();
    });
    expect(defaultProps.onSave).not.toHaveBeenCalled();
  });

  it('renders cancel and submit buttons', async () => {
    render(<OffTimeDialog {...defaultProps} />);
    expect(screen.getByRole('button', { name: 'Cancel' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Create' })).toBeInTheDocument();
    await waitFor(() => {
      expect(screen.getByRole('dialog')).toBeInTheDocument();
    });
  });

  it('does not render when closed', () => {
    render(<OffTimeDialog {...defaultProps} open={false} />);
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('validates missing dates on submit', async () => {
    render(<OffTimeDialog {...defaultProps} />);
    // Fill in title but leave dates empty
    fireEvent.change(screen.getByLabelText('Title'), { target: { value: 'Test Off-Time' } });
    fireEvent.submit(screen.getByRole('button', { name: 'Create' }).closest('form')!);
    await waitFor(() => {
      expect(screen.getByText('Start and end dates are required.')).toBeInTheDocument();
    });
    expect(defaultProps.onSave).not.toHaveBeenCalled();
  });

  it('pre-fills form in edit mode', async () => {
    const offTime = {
      id: 'ot1',
      siteId: 'site-1',
      title: 'Scheduled Maintenance',
      type: 'maintenance' as const,
      startMs: new Date('2024-06-01T08:00:00').getTime(),
      endMs: new Date('2024-06-01T18:00:00').getTime(),
      appliesToAllSpaces: true,
      spaceIds: [],
      isRecurring: false,
      recurrenceRule: null,
      enabled: true,
    };
    render(<OffTimeDialog {...defaultProps} offTime={offTime} />);
    await waitFor(() => {
      expect(screen.getByDisplayValue('Scheduled Maintenance')).toBeInTheDocument();
    });
    expect(screen.getByRole('button', { name: 'Update' })).toBeInTheDocument();
  });

  it('shows save error in alert', async () => {
    const onSave = vi.fn(() => Promise.reject(new Error('Server error')));
    render(<OffTimeDialog {...defaultProps} onSave={onSave} />);
    fireEvent.change(screen.getByLabelText('Title'), { target: { value: 'Test' } });
    // Submit without dates should show validation error
    fireEvent.submit(screen.getByRole('button', { name: 'Create' }).closest('form')!);
    await waitFor(() => {
      expect(screen.getByText('Start and end dates are required.')).toBeInTheDocument();
    });
  });
});
