import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { AutoSchedulePreviewDialog } from './AutoSchedulePreviewDialog';
import type { AutoSchedulePreviewResponse } from '@foundation/src/lib/api/auto-schedule-api';

const mockPreview: AutoSchedulePreviewResponse = {
  solverUsed: 'OrToolsCpSat',
  status: 'Optimal',
  score: {
    scheduledCount: 3,
    unscheduledCount: 1,
    priorityScore: 0.75,
  },
  assignments: [
    {
      requestId: 'r-1',
      requestName: 'Task Alpha',
      spaceId: 'sp-1',
      spaceName: 'Room A',
      start: '2026-03-01',
      end: '2026-03-05',
      durationDays: 4,
    },
    {
      requestId: 'r-2',
      requestName: 'Task Beta',
      spaceId: 'sp-2',
      spaceName: 'Room B',
      start: '2026-03-01',
      end: '2026-03-03',
      durationDays: 2,
    },
  ],
  unscheduled: [
    {
      requestId: 'r-3',
      requestName: 'Task Gamma',
      reasonCodes: ['NoCompatibleSpace'],
    },
  ],
  diagnostics: ['Solver completed in 1.2s'],
  fingerprint: 'abc123',
};

const emptyPreview: AutoSchedulePreviewResponse = {
  solverUsed: 'Greedy',
  status: 'Optimal',
  score: { scheduledCount: 0, unscheduledCount: 0, priorityScore: 0 },
  assignments: [],
  unscheduled: [],
  diagnostics: [],
  fingerprint: 'empty',
};

const defaultProps = {
  open: true,
  preview: mockPreview,
  isApplying: false,
  applyError: null,
  onApply: vi.fn(),
  onClose: vi.fn(),
};

function renderDialog(props: Partial<React.ComponentProps<typeof AutoSchedulePreviewDialog>> = {}) {
  return render(<AutoSchedulePreviewDialog {...defaultProps} {...props} />);
}

describe('AutoSchedulePreviewDialog', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('renders dialog title', () => {
    renderDialog();
    expect(screen.getByText('Auto-schedule preview')).toBeInTheDocument();
  });

  it('shows loading spinner when preview is null', () => {
    renderDialog({ preview: null });
    // Loader2 renders via animate-spin class — just verify no summary cards
    expect(screen.queryByText('Solver')).not.toBeInTheDocument();
  });

  it('renders solver type (OR-Tools)', () => {
    renderDialog();
    expect(screen.getByText('OR-Tools CP-SAT')).toBeInTheDocument();
  });

  it('renders solver type (Greedy)', () => {
    renderDialog({ preview: emptyPreview });
    expect(screen.getByText('Greedy')).toBeInTheDocument();
  });

  it('shows scheduled and unscheduled counts', () => {
    renderDialog();
    expect(screen.getByText('3')).toBeInTheDocument();
    expect(screen.getByText('1')).toBeInTheDocument();
  });

  it('renders assignments table', () => {
    renderDialog();
    expect(screen.getByText('Assignments (2)')).toBeInTheDocument();
    expect(screen.getByText('Task Alpha')).toBeInTheDocument();
    expect(screen.getByText('Room A')).toBeInTheDocument();
    expect(screen.getByText('Task Beta')).toBeInTheDocument();
  });

  it('shows "No assignments proposed" when empty', () => {
    renderDialog({ preview: emptyPreview });
    expect(screen.getByText('No assignments proposed.')).toBeInTheDocument();
  });

  it('renders unscheduled section with reason labels', () => {
    renderDialog();
    expect(screen.getByText('Unscheduled (1)')).toBeInTheDocument();
    expect(screen.getByText(/Task Gamma/)).toBeInTheDocument();
    expect(screen.getByText(/No compatible space/)).toBeInTheDocument();
  });

  it('renders diagnostics when present', () => {
    renderDialog();
    expect(screen.getByText('Diagnostics')).toBeInTheDocument();
    expect(screen.getByText('Solver completed in 1.2s')).toBeInTheDocument();
  });

  it('hides diagnostics section when empty', () => {
    renderDialog({ preview: emptyPreview });
    expect(screen.queryByText('Diagnostics')).not.toBeInTheDocument();
  });

  it('shows apply error message', () => {
    renderDialog({ applyError: 'Something went wrong' });
    expect(screen.getByText('Something went wrong')).toBeInTheDocument();
  });

  it('Apply button shows count', () => {
    renderDialog();
    expect(screen.getByRole('button', { name: /Apply \(2\)/ })).toBeInTheDocument();
  });

  it('disables Apply when no assignments', () => {
    renderDialog({ preview: emptyPreview });
    expect(screen.getByRole('button', { name: /Apply/ })).toBeDisabled();
  });

  it('disables Apply while applying', () => {
    renderDialog({ isApplying: true });
    expect(screen.getByRole('button', { name: /Apply/ })).toBeDisabled();
  });

  it('disables Apply when preview is null', () => {
    renderDialog({ preview: null });
    expect(screen.getByRole('button', { name: /Apply/ })).toBeDisabled();
  });

  it('calls onApply when Apply button clicked', () => {
    renderDialog();
    fireEvent.click(screen.getByRole('button', { name: /Apply/ }));
    expect(defaultProps.onApply).toHaveBeenCalled();
  });

  it('calls onClose when Cancel button clicked', () => {
    renderDialog();
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    expect(defaultProps.onClose).toHaveBeenCalled();
  });
});
