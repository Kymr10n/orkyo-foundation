import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { ReactNode } from 'react';
import { QueryClientProvider } from '@tanstack/react-query';
import { NewFromRoutingDialog, buildInstantiateRequest } from './NewFromRoutingDialog';
import { createFeedbackTestQueryClientWithSpy } from '@foundation/src/test-utils';
import { makeRequest } from '@foundation/src/test-utils/request-fixtures';
import { getRoutings, instantiateRouting } from '@foundation/src/lib/api/routing-api';
import { getSites } from '@foundation/src/lib/api/site-api';
import type { Routing } from '@foundation/src/types/routings';

vi.mock('@foundation/src/lib/api/routing-api', () => ({
  getRoutings: vi.fn(),
  createRouting: vi.fn(),
  updateRouting: vi.fn(),
  deleteRouting: vi.fn(),
  instantiateRouting: vi.fn(),
}));

vi.mock('@foundation/src/lib/api/site-api', () => ({
  getSites: vi.fn(),
  createSite: vi.fn(),
  updateSite: vi.fn(),
  deleteSite: vi.fn(),
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

vi.mock('@foundation/src/components/ui/select', () => ({
  Select: ({ value, onValueChange, children }: { value: string; onValueChange: (v: string) => void; children: ReactNode }) => (
    <select value={value} onChange={(e) => onValueChange(e.target.value)}>
      <option value="">—</option>
      {children}
    </select>
  ),
  SelectTrigger: ({ id }: { id?: string }) => <span data-testid={id} />,
  SelectValue: () => null,
  SelectContent: ({ children }: { children: ReactNode }) => <>{children}</>,
  SelectItem: ({ value, children }: { value: string; children: ReactNode }) => <option value={value}>{children}</option>,
}));

// The picker owns a calendar popover; a plain input carries the same "YYYY-MM-DDTHH:mm" contract.
vi.mock('@foundation/src/components/ui/date-time-picker', () => ({
  DateTimePicker: ({ id, value, onChange }: { id?: string; value: string; onChange: (v: string) => void }) => (
    <input id={id} value={value} onChange={(e) => onChange(e.target.value)} />
  ),
}));

const bracket: Routing = { id: 'r1', name: 'Bracket BR-100', steps: [] };

function renderDialog(props: Partial<Parameters<typeof NewFromRoutingDialog>[0]> = {}) {
  const { queryClient } = createFeedbackTestQueryClientWithSpy();
  return render(
    <QueryClientProvider client={queryClient}>
      <NewFromRoutingDialog open onOpenChange={vi.fn()} requests={[]} {...props} />
    </QueryClientProvider>,
  );
}

function submit() {
  fireEvent.submit(screen.getByRole('dialog').querySelector('form')!);
}

/** The (mocked, native) select that offers the given option — the ids sit on the trigger. */
function selectOffering(optionName: string): HTMLSelectElement {
  const option = screen.getByRole('option', { name: optionName });
  return option.closest('select')!;
}

describe('buildInstantiateRequest', () => {
  const valid = {
    routingId: 'r1', name: 'WO-1', siteId: '', quantity: '4',
    earliestStart: '', latestEnd: '', parentRequestId: '',
  };

  it('builds the request and drops the blanks', () => {
    expect(buildInstantiateRequest(valid)).toEqual({
      request: { name: 'WO-1', siteId: undefined, quantity: 4, earliestStartTs: undefined, latestEndTs: undefined, parentRequestId: undefined },
    });
  });

  it('turns the local window into ISO instants', () => {
    const result = buildInstantiateRequest({ ...valid, earliestStart: '2026-03-02T08:00', latestEnd: '2026-03-06T17:00' });
    expect(result).toHaveProperty('request.earliestStartTs', new Date('2026-03-02T08:00').toISOString());
    expect(result).toHaveProperty('request.latestEndTs', new Date('2026-03-06T17:00').toISOString());
  });

  it('refuses a missing routing, a blank name, and a bad quantity', () => {
    expect(buildInstantiateRequest({ ...valid, routingId: '' })).toEqual({ error: 'Choose a routing' });
    expect(buildInstantiateRequest({ ...valid, name: '  ' })).toEqual({ error: 'Name is required' });
    expect(buildInstantiateRequest({ ...valid, quantity: '0' })).toEqual({ error: 'Quantity must be a whole number of at least 1' });
    expect(buildInstantiateRequest({ ...valid, quantity: '2.5' })).toEqual({ error: 'Quantity must be a whole number of at least 1' });
  });

  it('refuses a window that ends before it starts', () => {
    expect(buildInstantiateRequest({ ...valid, earliestStart: '2026-03-06T17:00', latestEnd: '2026-03-02T08:00' }))
      .toEqual({ error: 'Latest end must be after earliest start' });
  });
});

describe('NewFromRoutingDialog', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(getRoutings).mockResolvedValue([bracket]);
    vi.mocked(getSites).mockResolvedValue([]);
    vi.mocked(instantiateRouting).mockResolvedValue({ parent: { id: 'p1', name: 'WO-1' }, childIds: ['c1', 'c2'] });
  });

  it('creates the work order from the chosen routing and closes', async () => {
    const user = userEvent.setup();
    const onOpenChange = vi.fn();
    renderDialog({ onOpenChange, defaultSiteId: 'site-1' });
    await screen.findByRole('option', { name: 'Bracket BR-100' });

    await user.selectOptions(selectOffering('Bracket BR-100'), 'r1');
    fireEvent.change(screen.getByLabelText(/Quantity/), { target: { value: '4' } });
    fireEvent.change(screen.getByLabelText('Earliest start'), { target: { value: '2026-03-02T08:00' } });
    submit();

    await waitFor(() => {
      expect(instantiateRouting).toHaveBeenCalledWith('r1', {
        // The routing's name filled the empty name field.
        name: 'Bracket BR-100',
        siteId: 'site-1',
        quantity: 4,
        earliestStartTs: new Date('2026-03-02T08:00').toISOString(),
        latestEndTs: undefined,
        parentRequestId: undefined,
      });
    });
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  it('keeps a name the planner typed before choosing the routing', async () => {
    const user = userEvent.setup();
    renderDialog();
    await screen.findByRole('option', { name: 'Bracket BR-100' });
    fireEvent.change(screen.getByLabelText(/^Name/), { target: { value: 'WO-2026-0142' } });
    await user.selectOptions(selectOffering('Bracket BR-100'), 'r1');
    expect(screen.getByLabelText(/^Name/)).toHaveValue('WO-2026-0142');
  });

  it('offers only non-leaf requests as the parent', async () => {
    const user = userEvent.setup();
    const container = makeRequest({ id: 'c', name: 'Line 1', planningMode: 'container' });
    const leaf = makeRequest({ id: 'l', name: 'Leaf', planningMode: 'leaf' });
    renderDialog({ requests: [container, leaf] });
    await screen.findByRole('option', { name: 'Bracket BR-100' });

    expect(screen.getByRole('option', { name: 'Line 1' })).toBeInTheDocument();
    expect(screen.queryByRole('option', { name: 'Leaf' })).not.toBeInTheDocument();

    await user.selectOptions(selectOffering('Bracket BR-100'), 'r1');
    await user.selectOptions(selectOffering('Line 1'), 'c');
    submit();
    await waitFor(() => {
      expect(instantiateRouting).toHaveBeenCalledWith('r1', expect.objectContaining({ parentRequestId: 'c' }));
    });
  });

  it('hides the parent picker when nothing can hold children', async () => {
    renderDialog({ requests: [makeRequest({ planningMode: 'leaf' })] });
    await screen.findByRole('option', { name: 'Bracket BR-100' });
    expect(screen.queryByText('Parent')).not.toBeInTheDocument();
  });

  it('shows the site picker only for a multi-site tenant', async () => {
    vi.mocked(getSites).mockResolvedValue([
      { id: 's1', code: 'a', name: 'Plant A', createdAt: '', updatedAt: '' },
      { id: 's2', code: 'b', name: 'Plant B', createdAt: '', updatedAt: '' },
    ]);
    renderDialog();
    expect(await screen.findByRole('option', { name: 'Plant B' })).toBeInTheDocument();
  });

  it('refuses to submit without a routing', async () => {
    renderDialog();
    await screen.findByRole('option', { name: 'Bracket BR-100' });
    submit();
    expect(await screen.findByText('Choose a routing')).toBeInTheDocument();
    expect(instantiateRouting).not.toHaveBeenCalled();
  });

  it('shows the server refusal inline', async () => {
    const user = userEvent.setup();
    vi.mocked(instantiateRouting).mockRejectedValueOnce(new Error("Step 2 ('Mill') targets no resource type, so it cannot be scheduled"));
    renderDialog();
    await screen.findByRole('option', { name: 'Bracket BR-100' });
    await user.selectOptions(selectOffering('Bracket BR-100'), 'r1');
    submit();
    expect(await screen.findByText(/Step 2 \('Mill'\) targets no resource type/)).toBeInTheDocument();
  });
});
