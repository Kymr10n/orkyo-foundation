/* eslint-disable @typescript-eslint/no-explicit-any */
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import type { ReactNode } from 'react';
import { TemplateDialogBase } from './TemplateDialogBase';

// ── UI mocks ──────────────────────────────────────────
vi.mock('@/components/ui/dialog', () => ({
  Dialog: ({ children, open }: { children: ReactNode; open: boolean }) =>
    open ? <div role="dialog">{children}</div> : null,
  DialogContent: ({ children }: { children: ReactNode }) => <div>{children}</div>,
  DialogHeader: ({ children }: { children: ReactNode }) => <div>{children}</div>,
  DialogTitle: ({ children }: { children: ReactNode }) => <h2>{children}</h2>,
  DialogDescription: ({ children }: { children: ReactNode }) => <p>{children}</p>,
}));

vi.mock('@/components/ui/ErrorAlert', () => ({
  ErrorAlert: ({ message }: { message: string | null }) =>
    message ? <div role="alert">{message}</div> : null,
}));

vi.mock('@/components/ui/DialogFormFooter', () => ({
  DialogFormFooter: ({ submitLabel, onCancel, isSubmitting }: any) => (
    <div>
      <button type="button" onClick={onCancel}>Cancel</button>
      <button type="submit" disabled={isSubmitting}>{submitLabel}</button>
    </div>
  ),
}));

vi.mock('@/components/ui/scroll-area', () => ({
  ScrollArea: ({ children }: { children: ReactNode }) => <div>{children}</div>,
}));

vi.mock('@/components/ui/separator', () => ({
  Separator: () => <hr />,
}));

vi.mock('../requests/CriterionRequirementInput', () => ({
  CriterionRequirementInput: ({ criterion, onChange }: any) => (
    <div data-testid={`req-input-${criterion.id}`}>
      <span>{criterion.name}</span>
      <button onClick={() => onChange('updated-value')}>change-val</button>
    </div>
  ),
}));

// ── API / hook mocks ───────────────────────────────────
const mockCreateTemplate = vi.fn((..._args: any[]) => Promise.resolve({ id: 'new-id' }));
const mockUpdateTemplate = vi.fn((..._args: any[]) => Promise.resolve({ id: 'tpl-1' }));
const mockGetCriteria = vi.fn(() =>
  Promise.resolve([
    { id: 'c1', name: 'Capacity', dataType: 'Number', description: '', unit: 'seats', enumValues: [] },
    { id: 'c2', name: 'HasProjector', dataType: 'Boolean', description: '', unit: null, enumValues: [] },
  ]),
);

vi.mock('@/lib/api/template-api', () => ({
  createTemplate: (...args: unknown[]) => mockCreateTemplate(...args),
  updateTemplate: (...args: unknown[]) => mockUpdateTemplate(...args),
}));

vi.mock('@/lib/api/criteria-api', () => ({
  getCriteria: () => mockGetCriteria(),
}));

vi.mock('@/lib/utils', async (importOriginal) => {
  const actual = await importOriginal<Record<string, unknown>>();
  return { ...actual, getDataTypeColor: () => 'bg-blue-100 text-blue-800' };
});

const mockInvalidateQueries = vi.fn();
vi.mock('@tanstack/react-query', () => ({
  useQueryClient: () => ({ invalidateQueries: mockInvalidateQueries }),
}));

// ── Template fixture ───────────────────────────────────
const existingTemplate = {
  id: 'tpl-1',
  name: 'Weekly',
  description: 'A weekly template',
  entityType: 'request' as const,
  durationValue: 5,
  durationUnit: 'days' as const,
  items: [{ id: 'itm-1', templateId: 'tpl-1', criterionId: 'c1', value: '10' }],
  createdAt: '2024-01-01T00:00:00Z',
};

// ── Helpers ────────────────────────────────────────────
const defaultProps = {
  open: true,
  onOpenChange: vi.fn(),
  onSuccess: vi.fn(),
  template: null as any,
};

function submit() {
  fireEvent.submit(screen.getByRole('dialog').querySelector('form')!);
}

describe('TemplateDialogBase', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  // ── Rendering ─────────────────────────────────────────
  it('renders create mode title', async () => {
    render(<TemplateDialogBase {...defaultProps} />);
    expect(screen.getByText('Create Request Template')).toBeInTheDocument();
    await waitFor(() => expect(mockGetCriteria).toHaveBeenCalled());
  });

  it('renders edit mode title', async () => {
    render(<TemplateDialogBase {...defaultProps} template={existingTemplate} />);
    expect(screen.getByText('Edit Request Template')).toBeInTheDocument();
    await waitFor(() => expect(mockGetCriteria).toHaveBeenCalled());
  });

  it('does not render when closed', () => {
    const { container } = render(<TemplateDialogBase {...defaultProps} open={false} />);
    expect(container.querySelector('[role="dialog"]')).toBeNull();
  });

  it('pre-fills form fields in edit mode', async () => {
    render(<TemplateDialogBase {...defaultProps} template={existingTemplate} />);
    await waitFor(() => expect(mockGetCriteria).toHaveBeenCalled());
    expect(screen.getByDisplayValue('Weekly')).toBeInTheDocument();
    expect(screen.getByDisplayValue('A weekly template')).toBeInTheDocument();
    expect(screen.getByDisplayValue('5')).toBeInTheDocument();
  });

  it('shows "No criteria added yet" in create mode', async () => {
    render(<TemplateDialogBase {...defaultProps} />);
    await waitFor(() => expect(mockGetCriteria).toHaveBeenCalled());
    expect(screen.getByText(/No criteria added yet/)).toBeInTheDocument();
  });

  // ── Criteria loading & display ────────────────────────
  it('loads criteria on open', async () => {
    render(<TemplateDialogBase {...defaultProps} />);
    await waitFor(() => expect(mockGetCriteria).toHaveBeenCalled());
  });

  it('renders existing requirements in edit mode', async () => {
    render(<TemplateDialogBase {...defaultProps} template={existingTemplate} />);
    await waitFor(() => {
      expect(screen.getByTestId('req-input-c1')).toBeInTheDocument();
    });
  });

  it('shows active count badge', async () => {
    render(<TemplateDialogBase {...defaultProps} template={existingTemplate} />);
    await waitFor(() => expect(mockGetCriteria).toHaveBeenCalled());
    expect(screen.getByText('1 active')).toBeInTheDocument();
  });

  // ── Validation ────────────────────────────────────────
  it('shows error when name is empty', async () => {
    render(<TemplateDialogBase {...defaultProps} />);
    await waitFor(() => expect(mockGetCriteria).toHaveBeenCalled());
    submit();
    await waitFor(() => {
      expect(screen.getByText('Name is required')).toBeInTheDocument();
    });
  });

  it('shows error when duration is invalid', async () => {
    render(<TemplateDialogBase {...defaultProps} />);
    await waitFor(() => expect(mockGetCriteria).toHaveBeenCalled());

    fireEvent.change(screen.getByPlaceholderText(/Standard Week/), { target: { value: 'Test' } });
    fireEvent.change(screen.getByPlaceholderText('1'), { target: { value: '0' } });
    submit();

    await waitFor(() => {
      expect(screen.getByText('Minimal duration must be a positive number')).toBeInTheDocument();
    });
  });

  // ── Submit – create mode ──────────────────────────────
  it('creates template on submit', async () => {
    render(<TemplateDialogBase {...defaultProps} />);
    await waitFor(() => expect(mockGetCriteria).toHaveBeenCalled());

    fireEvent.change(screen.getByPlaceholderText(/Standard Week/), { target: { value: 'New Template' } });
    fireEvent.change(screen.getByPlaceholderText('1'), { target: { value: '3' } });
    submit();

    await waitFor(() => {
      expect(mockCreateTemplate).toHaveBeenCalledWith(expect.objectContaining({
        name: 'New Template',
        durationValue: 3,
        durationUnit: 'days',
        entityType: 'request',
      }));
    });
    expect(defaultProps.onSuccess).toHaveBeenCalled();
  });

  // ── Submit – edit mode ────────────────────────────────
  it('updates template on submit in edit mode', async () => {
    render(<TemplateDialogBase {...defaultProps} template={existingTemplate} />);
    await waitFor(() => expect(mockGetCriteria).toHaveBeenCalled());

    fireEvent.change(screen.getByDisplayValue('Weekly'), { target: { value: 'Updated' } });
    submit();

    await waitFor(() => {
      expect(mockUpdateTemplate).toHaveBeenCalledWith('tpl-1', expect.objectContaining({
        name: 'Updated',
      }));
    });
    expect(defaultProps.onSuccess).toHaveBeenCalled();
  });

  // ── Submit error handling ─────────────────────────────
  it('shows error when create fails', async () => {
    mockCreateTemplate.mockRejectedValueOnce(new Error('Server error'));
    render(<TemplateDialogBase {...defaultProps} />);
    await waitFor(() => expect(mockGetCriteria).toHaveBeenCalled());

    fireEvent.change(screen.getByPlaceholderText(/Standard Week/), { target: { value: 'Fail' } });
    submit();

    await waitFor(() => {
      expect(screen.getByText('Server error')).toBeInTheDocument();
    });
  });

  it('shows generic error when create fails with non-Error', async () => {
    mockCreateTemplate.mockRejectedValueOnce('oops');
    render(<TemplateDialogBase {...defaultProps} />);
    await waitFor(() => expect(mockGetCriteria).toHaveBeenCalled());

    fireEvent.change(screen.getByPlaceholderText(/Standard Week/), { target: { value: 'Fail' } });
    submit();

    await waitFor(() => {
      expect(screen.getByText(/Failed to create template/)).toBeInTheDocument();
    });
  });

  it('shows generic error when update fails with non-Error', async () => {
    mockUpdateTemplate.mockRejectedValueOnce('oops');
    render(<TemplateDialogBase {...defaultProps} template={existingTemplate} />);
    await waitFor(() => expect(mockGetCriteria).toHaveBeenCalled());

    submit();
    await waitFor(() => {
      expect(screen.getByText(/Failed to update template/)).toBeInTheDocument();
    });
  });

  // ── Cancel / close ────────────────────────────────────
  it('calls onOpenChange(false) when cancel is clicked', async () => {
    render(<TemplateDialogBase {...defaultProps} />);
    await waitFor(() => expect(mockGetCriteria).toHaveBeenCalled());
    fireEvent.click(screen.getByText('Cancel'));
    expect(defaultProps.onOpenChange).toHaveBeenCalledWith(false);
  });

  // ── Add / remove requirement ──────────────────────────
  it('removes a requirement when trash is clicked', async () => {
    render(<TemplateDialogBase {...defaultProps} template={existingTemplate} />);
    await waitFor(() => {
      expect(screen.getByTestId('req-input-c1')).toBeInTheDocument();
    });

    // The trash/remove button is next to the requirement input
    const removeButtons = screen.getByRole('dialog').querySelectorAll('button');
    const trashBtn = Array.from(removeButtons).find(b =>
      b.querySelector('svg') && b.closest('.mt-7'),
    );
    if (trashBtn) fireEvent.click(trashBtn);

    await waitFor(() => {
      expect(screen.queryByTestId('req-input-c1')).not.toBeInTheDocument();
    });
  });
});
