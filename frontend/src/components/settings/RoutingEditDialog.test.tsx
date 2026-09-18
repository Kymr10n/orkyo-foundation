import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { ReactNode } from 'react';
import { QueryClientProvider } from '@tanstack/react-query';
import { RoutingEditDialog, toStepRequests } from './RoutingEditDialog';
import { createFeedbackTestQueryClientWithSpy } from '@foundation/src/test-utils';
import { createRouting, updateRouting } from '@foundation/src/lib/api/routing-api';
import { getTemplates } from '@foundation/src/lib/api/template-api';
import type { Routing } from '@foundation/src/types/routings';

vi.mock('@foundation/src/lib/api/routing-api', () => ({
  createRouting: vi.fn(),
  updateRouting: vi.fn(),
  deleteRouting: vi.fn(),
  getRoutings: vi.fn(),
  instantiateRouting: vi.fn(),
}));

vi.mock('@foundation/src/lib/api/template-api', () => ({
  getTemplates: vi.fn(),
}));

vi.mock('@foundation/src/components/ui/dialog', () => ({
  useFullScreenOnPhone: () => undefined,
  DIALOG_SIZE: { sm: '', md: '', lg: '', xl: '' },
  Dialog: ({ children, open }: { children: ReactNode; open: boolean }) =>
    open ? <div role="dialog">{children}</div> : null,
  DialogContent: ({ children }: { children: ReactNode }) => <div>{children}</div>,
  ScrollableDialogBody: ({ children }: { children: ReactNode }) => <div>{children}</div>,
  DialogHeader: ({ children }: { children: ReactNode }) => <div>{children}</div>,
  DialogTitle: ({ children }: { children: ReactNode }) => <h2>{children}</h2>,
  DialogDescription: ({ children }: { children: ReactNode }) => <p>{children}</p>,
  DialogFooter: ({ children }: { children: ReactNode }) => <div>{children}</div>,
}));

// Radix Select does not drive in jsdom; a native select carries the same contract.
vi.mock('@foundation/src/components/ui/select', () => ({
  Select: ({ value, onValueChange, children, disabled }: { value: string; onValueChange: (v: string) => void; children: ReactNode; disabled?: boolean }) => (
    <select data-testid="operation-select" value={value} onChange={(e) => onValueChange(e.target.value)} disabled={disabled}>
      <option value="">—</option>
      {children}
    </select>
  ),
  SelectTrigger: () => null,
  SelectValue: () => null,
  SelectContent: ({ children }: { children: ReactNode }) => <>{children}</>,
  SelectItem: ({ value, children }: { value: string; children: ReactNode }) => <option value={value}>{children}</option>,
}));

const templates = [
  { id: 't-saw', name: 'Saw', entityType: 'request' as const },
  { id: 't-mill', name: 'Mill', entityType: 'request' as const },
];

const bracket: Routing = {
  id: 'r1',
  name: 'Bracket',
  description: 'Steel',
  steps: [
    { id: 's1', stepNo: 1, operationTemplateId: 't-saw', operationName: 'Saw', setupMinutes: 10, runMinutesPerUnit: 5, lagMinutesAfter: 0 },
    { id: 's2', stepNo: 2, operationTemplateId: 't-mill', operationName: 'Mill', setupMinutes: 30, runMinutesPerUnit: 60, lagMinutesAfter: 240 },
  ],
};

function renderDialog(props: Partial<Parameters<typeof RoutingEditDialog>[0]> = {}) {
  const { queryClient } = createFeedbackTestQueryClientWithSpy();
  return render(
    <QueryClientProvider client={queryClient}>
      <RoutingEditDialog routing={null} open onOpenChange={vi.fn()} {...props} />
    </QueryClientProvider>,
  );
}

function submit() {
  fireEvent.submit(screen.getByRole('dialog').querySelector('form')!);
}

describe('toStepRequests', () => {
  const step = { operationTemplateId: 't-saw', setupMinutes: '10', runMinutesPerUnit: '5', lagMinutesAfter: '0' };

  it('numbers the steps by position', () => {
    const result = toStepRequests([step, { ...step, operationTemplateId: 't-mill' }]);
    expect(result).toEqual({
      steps: [
        { stepNo: 1, operationTemplateId: 't-saw', setupMinutes: 10, runMinutesPerUnit: 5, lagMinutesAfter: 0 },
        { stepNo: 2, operationTemplateId: 't-mill', setupMinutes: 10, runMinutesPerUnit: 5, lagMinutesAfter: 0 },
      ],
    });
  });

  it('refuses an empty routing', () => {
    expect(toStepRequests([])).toEqual({ error: 'A routing needs at least one step' });
  });

  it('names the step that has no operation', () => {
    expect(toStepRequests([step, { ...step, operationTemplateId: '' }])).toEqual({ error: 'Step 2: choose an operation' });
  });

  it('refuses minutes that are not whole numbers', () => {
    expect(toStepRequests([{ ...step, setupMinutes: '1.5' }])).toEqual({ error: 'Step 1: minutes must be whole numbers' });
    expect(toStepRequests([{ ...step, lagMinutesAfter: '-1' }])).toEqual({ error: 'Step 1: minutes must be whole numbers' });
  });

  it('refuses a step that takes no time', () => {
    expect(toStepRequests([{ ...step, setupMinutes: '0', runMinutesPerUnit: '0' }]))
      .toEqual({ error: 'Step 1: setup or run time per unit must be positive' });
  });
});

describe('RoutingEditDialog', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(getTemplates).mockResolvedValue(templates);
    vi.mocked(createRouting).mockResolvedValue(bracket);
    vi.mocked(updateRouting).mockResolvedValue(bracket);
  });

  it('starts with one empty step in create mode', async () => {
    renderDialog();
    expect(screen.getByRole('heading', { name: 'Create Routing' })).toBeInTheDocument();
    await waitFor(() => expect(getTemplates).toHaveBeenCalledWith('request'));
    expect(screen.getByTestId('routing-step-1')).toBeInTheDocument();
    expect(screen.queryByTestId('routing-step-2')).not.toBeInTheDocument();
  });

  it('pre-fills an existing routing in step order', async () => {
    renderDialog({ routing: bracket });
    expect(screen.getByRole('heading', { name: 'Edit Routing' })).toBeInTheDocument();
    expect(screen.getByDisplayValue('Bracket')).toBeInTheDocument();
    await waitFor(() => expect(screen.getAllByTestId('operation-select')[0]).toHaveValue('t-saw'));
    expect(screen.getByLabelText('Lag after (min)', { selector: '#step-2-lag' })).toHaveValue('240');
  });

  it('creates a routing with numbered steps from the form', async () => {
    const user = userEvent.setup();
    const onOpenChange = vi.fn();
    renderDialog({ onOpenChange });
    await waitFor(() => expect(getTemplates).toHaveBeenCalled());

    fireEvent.change(screen.getByLabelText(/^Name/), { target: { value: 'Bracket' } });
    await user.click(screen.getByRole('button', { name: /Add step/ }));
    const selects = await screen.findAllByTestId('operation-select');
    await user.selectOptions(selects[0], 't-saw');
    await user.selectOptions(selects[1], 't-mill');
    fireEvent.change(screen.getByLabelText('Setup (min)', { selector: '#step-1-setup' }), { target: { value: '10' } });
    fireEvent.change(screen.getByLabelText('Run per unit (min)', { selector: '#step-1-run' }), { target: { value: '5' } });
    fireEvent.change(screen.getByLabelText('Run per unit (min)', { selector: '#step-2-run' }), { target: { value: '60' } });
    fireEvent.change(screen.getByLabelText('Lag after (min)', { selector: '#step-1-lag' }), { target: { value: '240' } });
    submit();

    await waitFor(() => {
      expect(createRouting).toHaveBeenCalledWith({
        name: 'Bracket',
        description: undefined,
        steps: [
          { stepNo: 1, operationTemplateId: 't-saw', setupMinutes: 10, runMinutesPerUnit: 5, lagMinutesAfter: 240 },
          { stepNo: 2, operationTemplateId: 't-mill', setupMinutes: 0, runMinutesPerUnit: 60, lagMinutesAfter: 0 },
        ],
      });
    });
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  it('renumbers after moving a step up', async () => {
    const user = userEvent.setup();
    renderDialog({ routing: bracket });
    await waitFor(() => expect(getTemplates).toHaveBeenCalled());

    await user.click(screen.getByRole('button', { name: 'Move step 2 up' }));
    submit();

    await waitFor(() => {
      expect(updateRouting).toHaveBeenCalledWith('r1', expect.objectContaining({
        steps: [
          expect.objectContaining({ stepNo: 1, operationTemplateId: 't-mill' }),
          expect.objectContaining({ stepNo: 2, operationTemplateId: 't-saw' }),
        ],
      }));
    });
  });

  it('removes a step', async () => {
    const user = userEvent.setup();
    renderDialog({ routing: bracket });
    await user.click(screen.getByRole('button', { name: 'Remove step 1' }));
    expect(screen.queryByTestId('routing-step-2')).not.toBeInTheDocument();
    submit();
    await waitFor(() => {
      expect(updateRouting).toHaveBeenCalledWith('r1', expect.objectContaining({
        steps: [expect.objectContaining({ stepNo: 1, operationTemplateId: 't-mill' })],
      }));
    });
  });

  it('requires a name', async () => {
    renderDialog();
    submit();
    expect(await screen.findByText('Name is required')).toBeInTheDocument();
    expect(createRouting).not.toHaveBeenCalled();
  });

  it('shows a step problem inline and does not save', async () => {
    renderDialog();
    fireEvent.change(screen.getByLabelText(/^Name/), { target: { value: 'Bracket' } });
    submit();
    expect(await screen.findByText('Step 1: choose an operation')).toBeInTheDocument();
    expect(createRouting).not.toHaveBeenCalled();
  });

  it('shows the server error when saving fails', async () => {
    vi.mocked(updateRouting).mockRejectedValueOnce(new Error("Template 'Mill' is a space template, not an operation"));
    renderDialog({ routing: bracket });
    submit();
    expect(await screen.findByText("Template 'Mill' is a space template, not an operation")).toBeInTheDocument();
  });

  it('points at Templates when there is nothing to pick from', async () => {
    vi.mocked(getTemplates).mockResolvedValue([]);
    renderDialog();
    expect(await screen.findByText(/No request templates yet/)).toBeInTheDocument();
  });
});
