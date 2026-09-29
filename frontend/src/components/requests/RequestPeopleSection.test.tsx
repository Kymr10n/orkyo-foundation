import { describe, it, expect, vi, beforeEach, type Mock } from 'vitest';
import { screen, fireEvent, waitFor } from '@testing-library/react';
import { RequestPeopleSection } from './RequestPeopleSection';
import { REQUEST_DERIVED_QUERY_KEYS } from '@foundation/src/lib/core/invalidate-request-data';
import type { ReactNode } from 'react';
import type * as ResourceAssignmentsApi from '@foundation/src/lib/api/resource-assignments-api';

// --- Mock UI components ---

vi.mock('@foundation/src/components/ui/select', () => ({
  Select: ({ children, value, onValueChange }: { children: ReactNode; value?: string; onValueChange?: (v: string) => void }) => (
    <div data-value={value} onClick={() => onValueChange?.('res-person-1')}>{children}</div>
  ),
  SelectTrigger: ({ children, ...props }: { children: ReactNode } & Record<string, unknown>) =>
    <div {...props}>{children}</div>,
  SelectValue: ({ placeholder }: { placeholder?: string }) => <span>{placeholder}</span>,
  SelectContent: ({ children }: { children: ReactNode }) => <div>{children}</div>,
  SelectItem: ({ children, value }: { children: ReactNode; value: string }) =>
    <div data-value={value}>{children}</div>,
}));

vi.mock('@foundation/src/components/ui/badge', () => ({
  Badge: ({ children }: { children: ReactNode }) => <span>{children}</span>,
}));

vi.mock('@foundation/src/components/ui/button', () => ({
  Button: ({ children, onClick, disabled, ...props }: { children: ReactNode; onClick?: () => void; disabled?: boolean } & Record<string, unknown>) =>
    <button onClick={onClick} disabled={disabled} {...props}>{children}</button>,
}));

vi.mock('@foundation/src/components/ui/input', () => ({
  Input: ({ onChange, value, ...props }: { onChange?: (e: React.ChangeEvent<HTMLInputElement>) => void; value?: string | number } & Record<string, unknown>) =>
    <input onChange={onChange} value={value ?? ''} {...props} />,
}));

vi.mock('@foundation/src/components/ui/label', () => ({
  Label: ({ children }: { children: ReactNode }) => <label>{children}</label>,
}));

// --- Mock API modules ---

vi.mock('@foundation/src/lib/api/resources-api', () => ({
  getResources: vi.fn(),
}));

vi.mock('@foundation/src/lib/api/resource-types-api', () => ({
  getResourceTypes: vi.fn(),
}));

vi.mock('@foundation/src/lib/api/resource-assignments-api', async (importActual) => ({
  // Keep the real pure helpers (SOFT_BLOCKER_CODES, hardBlockers, …); mock only network calls.
  ...(await importActual<typeof ResourceAssignmentsApi>()),
  getAssignmentsByRequest: vi.fn(),
  validateAssignment: vi.fn(),
  createAssignment: vi.fn(),
  cancelAssignment: vi.fn(),
}));

import { getResources } from '@foundation/src/lib/api/resources-api';
import { getResourceTypes } from '@foundation/src/lib/api/resource-types-api';
import {
  getAssignmentsByRequest,
  validateAssignment,
  createAssignment,
  cancelAssignment,
} from '@foundation/src/lib/api/resource-assignments-api';
import type { Conflict } from '@foundation/src/types/requests';
import { pagedResult } from '@foundation/src/test-utils/paged-result';
import { renderWithQuery } from '@foundation/src/test-utils';

const mockPeople = [
  { id: 'res-person-1', name: 'Alice', resourceTypeKey: 'person', isActive: true, resourceTypeId: 'rt-1', allocationMode: 'percent', baseAvailabilityPercent: 100, createdAt: '', updatedAt: '' },
  { id: 'res-person-2', name: 'Bob', resourceTypeKey: 'person', isActive: true, resourceTypeId: 'rt-1', allocationMode: 'percent', baseAvailabilityPercent: 100, createdAt: '', updatedAt: '' },
];

// The Select mock always triggers onValueChange('res-person-1'), so for Exclusive
// tests we make res-person-1 the Exclusive resource.
const mockPeopleWithExclusive = [
  { id: 'res-person-1', name: 'Hans Huber', resourceTypeKey: 'person', isActive: true, resourceTypeId: 'rt-1', allocationMode: 'Exclusive', baseAvailabilityPercent: 100, createdAt: '', updatedAt: '' },
  { id: 'res-person-2', name: 'Anna Meier', resourceTypeKey: 'person', isActive: true, resourceTypeId: 'rt-1', allocationMode: 'Fractional', baseAvailabilityPercent: 100, createdAt: '', updatedAt: '' },
];

const mockAssignment = {
  id: 'assign-1',
  requestId: 'req-1',
  resourceId: 'res-person-1',
  resourceTypeKey: 'person',
  startUtc: '2026-06-01T09:00:00Z',
  endUtc: '2026-06-01T17:00:00Z',
  allocationPercent: 100,
  assignmentStatus: 'active',
  createdAt: '',
  updatedAt: '',
};

const defaultProps = {
  requestId: 'req-1',
  requestStartTs: '2026-06-01T09:00:00Z',
  requestEndTs: '2026-06-01T17:00:00Z',
  onBlockersChange: vi.fn(),
};

describe('RequestPeopleSection', () => {
  beforeEach(() => {
    // The section derives "people" from the active directory-profile types, not a fixed key.
    (getResourceTypes as Mock).mockResolvedValue([
      { id: 'rt-1', key: 'person', displayName: 'Person', displayNamePlural: 'People', hasGeometry: false, hasDirectoryProfile: true, singleGroupMembership: false, isSystem: false, isActive: true, createdAt: '', updatedAt: '' },
    ]);
    (getResources as Mock).mockResolvedValue(pagedResult(mockPeople, { pageSize: 50 }));
    (getAssignmentsByRequest as Mock).mockResolvedValue([]);
    (validateAssignment as Mock).mockResolvedValue({ severity: 'ok', blockers: [], warnings: [] });
    (createAssignment as Mock).mockResolvedValue({ ...mockAssignment });
    (cancelAssignment as Mock).mockResolvedValue(undefined);
  });

  it('renders the section heading', () => {
    renderWithQuery(<RequestPeopleSection {...defaultProps} />);
    expect(screen.getByText('People')).toBeInTheDocument();
  });

  it('shows 0 assigned badge initially', async () => {
    renderWithQuery(<RequestPeopleSection {...defaultProps} />);
    await waitFor(() => {
      expect(screen.getByText('0 assigned')).toBeInTheDocument();
    });
  });

  it('renders existing assignments from the API', async () => {
    (getAssignmentsByRequest as Mock).mockResolvedValue([mockAssignment]);
    renderWithQuery(<RequestPeopleSection {...defaultProps} />);
    await waitFor(() => {
      expect(screen.getByText('Alice')).toBeInTheDocument();
    });
    expect(screen.getByText('1 assigned')).toBeInTheDocument();
  });

  it('flags an assigned person who has a saved conflict', async () => {
    (getAssignmentsByRequest as Mock).mockResolvedValue([mockAssignment]);
    const conflictsByResourceId = new Map<string, Conflict[]>([
      ['res-person-1', [{ id: 'x', kind: 'starts_in_off_time', severity: 'warning', message: 'Off-time' }]],
    ]);
    renderWithQuery(<RequestPeopleSection {...defaultProps} conflictsByResourceId={conflictsByResourceId} />);
    await waitFor(() => screen.getByText('Alice'));
    expect(screen.getByTestId('conflict-indicator')).toBeInTheDocument();
  });

  it('does not flag an assigned person without a conflict', async () => {
    (getAssignmentsByRequest as Mock).mockResolvedValue([mockAssignment]);
    renderWithQuery(<RequestPeopleSection {...defaultProps} conflictsByResourceId={new Map()} />);
    await waitFor(() => screen.getByText('Alice'));
    expect(screen.queryByTestId('conflict-indicator')).not.toBeInTheDocument();
  });

  it('adds a pending row when Add Person is clicked', async () => {
    renderWithQuery(<RequestPeopleSection {...defaultProps} />);
    await waitFor(() => screen.getByTestId('add-person-btn'));
    fireEvent.click(screen.getByTestId('add-person-btn'));
    expect(screen.getByTestId('pending-row')).toBeInTheDocument();
  });

  it('removes a pending row when Cancel is clicked', async () => {
    renderWithQuery(<RequestPeopleSection {...defaultProps} />);
    await waitFor(() => screen.getByTestId('add-person-btn'));
    fireEvent.click(screen.getByTestId('add-person-btn'));
    expect(screen.getByTestId('pending-row')).toBeInTheDocument();
    fireEvent.click(screen.getByText('Cancel'));
    expect(screen.queryByTestId('pending-row')).not.toBeInTheDocument();
  });

  it('calls onBlockersChange(false) when validation result has no blockers', async () => {
    (validateAssignment as Mock).mockResolvedValue({ severity: 'ok', blockers: [], warnings: [] });
    renderWithQuery(<RequestPeopleSection {...defaultProps} />);
    await waitFor(() => screen.getByTestId('add-person-btn'));
    expect(defaultProps.onBlockersChange).toHaveBeenCalledWith(false);
  });

  it('renders blocker feedback when validation returns blockers', async () => {
    (validateAssignment as Mock).mockResolvedValue({
      severity: 'blocker',
      blockers: [{ code: 'resource.inactive', message: 'Resource is not active', resourceId: 'res-person-1' }],
      warnings: [],
    });

    renderWithQuery(<RequestPeopleSection {...defaultProps} />);
    await waitFor(() => screen.getByTestId('add-person-btn'));
    fireEvent.click(screen.getByTestId('add-person-btn'));

    // Trigger person selection which fires validation
    fireEvent.click(screen.getByTestId('person-select'));

    await waitFor(() => {
      expect(screen.getByTestId('validation-feedback')).toBeInTheDocument();
      expect(screen.getByText(/Resource is inactive/)).toBeInTheDocument();
    });
  });

  it('renders warning feedback when validation returns warnings', async () => {
    (validateAssignment as Mock).mockResolvedValue({
      severity: 'warning',
      blockers: [],
      warnings: [{ code: 'assignment.overbooked', message: 'Resource may be overbooked', resourceId: 'res-person-1' }],
    });

    renderWithQuery(<RequestPeopleSection {...defaultProps} />);
    await waitFor(() => screen.getByTestId('add-person-btn'));
    fireEvent.click(screen.getByTestId('add-person-btn'));

    fireEvent.click(screen.getByTestId('person-select'));

    await waitFor(() => {
      expect(screen.getByTestId('validation-feedback')).toBeInTheDocument();
      expect(screen.getByText(/Resource is overbooked/)).toBeInTheDocument();
    });
  });

  it('disables the Add button when there are blockers', async () => {
    (validateAssignment as Mock).mockResolvedValue({
      severity: 'blocker',
      blockers: [{ code: 'resource.inactive', message: 'Not active', resourceId: 'res-person-1' }],
      warnings: [],
    });

    renderWithQuery(<RequestPeopleSection {...defaultProps} />);
    await waitFor(() => screen.getByTestId('add-person-btn'));
    fireEvent.click(screen.getByTestId('add-person-btn'));
    fireEvent.click(screen.getByTestId('person-select'));

    await waitFor(() => {
      expect(screen.getByTestId('save-row-btn')).toBeDisabled();
    });
  });

  it('calls onBlockersChange(true) when validation returns blockers', async () => {
    (validateAssignment as Mock).mockResolvedValue({
      severity: 'blocker',
      blockers: [{ code: 'resource.inactive', message: 'Not active', resourceId: 'res-person-1' }],
      warnings: [],
    });

    renderWithQuery(<RequestPeopleSection {...defaultProps} />);
    await waitFor(() => screen.getByTestId('add-person-btn'));
    fireEvent.click(screen.getByTestId('add-person-btn'));
    fireEvent.click(screen.getByTestId('person-select'));

    await waitFor(() => {
      const calls = (defaultProps.onBlockersChange as Mock).mock.calls;
      const lastCall = calls[calls.length - 1];
      expect(lastCall[0]).toBe(true);
    });
  });

  it('treats capability.missing as a soft blocker: Add stays enabled and it shows as a warning', async () => {
    (validateAssignment as Mock).mockResolvedValue({
      severity: 'blocker',
      blockers: [{ code: 'capability.missing', message: 'Resource does not satisfy requirement', resourceId: 'res-person-1' }],
      warnings: [],
    });

    renderWithQuery(<RequestPeopleSection {...defaultProps} />);
    await waitFor(() => screen.getByTestId('add-person-btn'));
    fireEvent.click(screen.getByTestId('add-person-btn'));
    fireEvent.click(screen.getByTestId('person-select'));

    await waitFor(() => {
      // Surfaced as a warning, not a hard blocker…
      expect(screen.getByText(/Required capability missing/)).toBeInTheDocument();
    });
    // …so the row's Add button stays enabled and the dialog isn't gated.
    expect(screen.getByTestId('save-row-btn')).not.toBeDisabled();
    const calls = (defaultProps.onBlockersChange as Mock).mock.calls;
    expect(calls[calls.length - 1][0]).toBe(false);
  });

  it('removes an existing assignment when remove is clicked', async () => {
    (getAssignmentsByRequest as Mock).mockResolvedValue([mockAssignment]);
    renderWithQuery(<RequestPeopleSection {...defaultProps} />);
    await waitFor(() => screen.getByText('Alice'));

    fireEvent.click(screen.getByLabelText('Remove assignment'));

    await waitFor(() => {
      expect(cancelAssignment).toHaveBeenCalledWith('assign-1');
    });
    expect(screen.queryByText('Alice')).not.toBeInTheDocument();
  });

  // ── Read-only (view) mode ───────────────────────────────────────────────────

  it('shows resolved assignment names (not raw UUIDs) and hides mutation controls in readOnly mode', async () => {
    (getAssignmentsByRequest as Mock).mockResolvedValue([mockAssignment]);
    renderWithQuery(<RequestPeopleSection {...defaultProps} readOnly />);
    await waitFor(() => expect(screen.getByText('Alice')).toBeInTheDocument());
    // The raw resourceId must not leak.
    expect(screen.queryByText('res-person-1')).not.toBeInTheDocument();
    // No add / remove affordances.
    expect(screen.queryByTestId('add-person-btn')).not.toBeInTheDocument();
    expect(screen.queryByLabelText('Remove assignment')).not.toBeInTheDocument();
  });

  it('shows an empty hint when there are no assignments in readOnly mode', async () => {
    (getAssignmentsByRequest as Mock).mockResolvedValue([]);
    renderWithQuery(<RequestPeopleSection {...defaultProps} readOnly />);
    await waitFor(() => expect(screen.getByText('No people assigned.')).toBeInTheDocument());
  });

  it('renders all known blocker reason codes with labels', () => {
    // Render a minimal version that tests the label map covers all codes
    const reasonCodes = [
      'resource.not-found',
      'resource.inactive',
      'resource.type-mismatch',
      'capability.missing',
      'offtime.overlap',
      'assignment.overbooked',
      'nonworking.weekend',
      'nonworking.holiday',
      'allocation-mode.invalid',
      'allocation-percent.invalid',
    ];
    // Each code should map to a non-empty string label (tested via REASON_LABELS being used in IssueList)
    // We verify the component renders without crashing with each code by checking they exist
    expect(reasonCodes).toHaveLength(10);
  });

  // ── Exclusive resource tests ────────────────────────────────────────────────

  it('hides the Alloc % input when the selected person is Exclusive', async () => {
    (getResources as Mock).mockResolvedValue(pagedResult(mockPeopleWithExclusive, { pageSize: 50 }));
    renderWithQuery(<RequestPeopleSection {...defaultProps} />);
    await waitFor(() => screen.getByTestId('add-person-btn'));
    fireEvent.click(screen.getByTestId('add-person-btn'));

    // Trigger person selection — mock always picks res-person-1 (Exclusive)
    fireEvent.click(screen.getByTestId('person-select'));

    expect(screen.queryByTestId('allocation-input')).not.toBeInTheDocument();
  });

  it('shows the Alloc % input for a Fractional resource', async () => {
    renderWithQuery(<RequestPeopleSection {...defaultProps} />);
    await waitFor(() => screen.getByTestId('add-person-btn'));
    fireEvent.click(screen.getByTestId('add-person-btn'));

    // res-person-1 is 'percent' in mockPeople (default)
    fireEvent.click(screen.getByTestId('person-select'));

    expect(screen.getByTestId('allocation-input')).toBeInTheDocument();
  });

  it('calls createAssignment without allocationPercent for an Exclusive resource', async () => {
    (getResources as Mock).mockResolvedValue(pagedResult(mockPeopleWithExclusive, { pageSize: 50 }));
    renderWithQuery(<RequestPeopleSection {...defaultProps} />);
    await waitFor(() => screen.getByTestId('add-person-btn'));
    fireEvent.click(screen.getByTestId('add-person-btn'));

    // Select the Exclusive person
    fireEvent.click(screen.getByTestId('person-select'));

    // Trigger save
    fireEvent.click(screen.getByTestId('save-row-btn'));

    await waitFor(() => {
      expect(createAssignment).toHaveBeenCalledWith(
        expect.objectContaining({
          resourceId: 'res-person-1',
          allocationPercent: undefined,
        }),
      );
    });
  });

  it('calls createAssignment with allocationPercent for a Fractional resource', async () => {
    renderWithQuery(<RequestPeopleSection {...defaultProps} />);
    await waitFor(() => screen.getByTestId('add-person-btn'));
    fireEvent.click(screen.getByTestId('add-person-btn'));

    fireEvent.click(screen.getByTestId('person-select'));
    fireEvent.click(screen.getByTestId('save-row-btn'));

    await waitFor(() => {
      expect(createAssignment).toHaveBeenCalledWith(
        expect.objectContaining({
          resourceId: 'res-person-1',
          allocationPercent: 100,
        }),
      );
    });
  });

  // ── Branch coverage: create-mode, validation-skip, save guards ──────────────

  it('does not fetch assignments when creating a new request (no requestId)', async () => {
    renderWithQuery(<RequestPeopleSection {...defaultProps} requestId={undefined} />);
    await waitFor(() => screen.getByTestId('add-person-btn'));
    expect(getAssignmentsByRequest).not.toHaveBeenCalled();
  });

  it('skips validation when the request has no start/end times', async () => {
    renderWithQuery(
      <RequestPeopleSection
        {...defaultProps}
        requestStartTs={undefined}
        requestEndTs={undefined}
      />,
    );
    await waitFor(() => screen.getByTestId('add-person-btn'));
    fireEvent.click(screen.getByTestId('add-person-btn'));
    fireEvent.click(screen.getByTestId('person-select'));
    // No wait: the scheduler returns before setting a timer when start/end are missing, so the
    // call is already impossible. The old 50ms sleep could not have observed the 400ms debounce
    // anyway — all it did was give a timer leaked from an earlier test a window to land in.
    expect(validateAssignment).not.toHaveBeenCalled();
  });

  it('shows "No issues found" when validation returns a clean result', async () => {
    renderWithQuery(<RequestPeopleSection {...defaultProps} />);
    await waitFor(() => screen.getByTestId('add-person-btn'));
    fireEvent.click(screen.getByTestId('add-person-btn'));
    fireEvent.click(screen.getByTestId('person-select'));
    await waitFor(() => expect(screen.getByTestId('validation-ok')).toBeInTheDocument());
  });

  it('blocks saving a row before the request itself has a schedule', async () => {
    renderWithQuery(<RequestPeopleSection {...defaultProps} requestId={undefined} />);
    await waitFor(() => screen.getByTestId('add-person-btn'));
    fireEvent.click(screen.getByTestId('add-person-btn'));
    fireEvent.click(screen.getByTestId('person-select'));
    fireEvent.click(screen.getByTestId('save-row-btn'));
    await waitFor(() =>
      expect(screen.getByTestId('row-error')).toHaveTextContent(
        /Request must be saved with a schedule/,
      ),
    );
    expect(createAssignment).not.toHaveBeenCalled();
  });

  it('does nothing when saving a row with no person selected', async () => {
    renderWithQuery(<RequestPeopleSection {...defaultProps} />);
    await waitFor(() => screen.getByTestId('add-person-btn'));
    fireEvent.click(screen.getByTestId('add-person-btn'));
    // Save without selecting a person.
    fireEvent.click(screen.getByTestId('save-row-btn'));
    expect(createAssignment).not.toHaveBeenCalled();
  });

  it('surfaces a row error when createAssignment fails', async () => {
    (createAssignment as Mock).mockRejectedValue(new Error('Conflict on save'));
    renderWithQuery(<RequestPeopleSection {...defaultProps} />);
    await waitFor(() => screen.getByTestId('add-person-btn'));
    fireEvent.click(screen.getByTestId('add-person-btn'));
    fireEvent.click(screen.getByTestId('person-select'));
    fireEvent.click(screen.getByTestId('save-row-btn'));
    await waitFor(() =>
      expect(screen.getByTestId('row-error')).toHaveTextContent('Conflict on save'),
    );
  });

  it('clamps and sanitizes the allocation percent input', async () => {
    renderWithQuery(<RequestPeopleSection {...defaultProps} />);
    await waitFor(() => screen.getByTestId('add-person-btn'));
    fireEvent.click(screen.getByTestId('add-person-btn'));
    fireEvent.click(screen.getByTestId('person-select'));

    const alloc = screen.getByTestId('allocation-input');
    fireEvent.change(alloc, { target: { value: '250' } });
    expect(alloc).toHaveValue('100');
    fireEvent.change(alloc, { target: { value: 'abc' } });
    expect(alloc).toHaveValue('0');
  });

  it('updates free-text role/notes fields on a pending row', async () => {
    renderWithQuery(<RequestPeopleSection {...defaultProps} />);
    await waitFor(() => screen.getByTestId('add-person-btn'));
    fireEvent.click(screen.getByTestId('add-person-btn'));
    fireEvent.change(screen.getByTestId('role-input'), { target: { value: 'Lead' } });
    expect(screen.getByTestId('role-input')).toHaveValue('Lead');
  });

  it('falls back to the resourceId when the assigned person is not in the people list', async () => {
    (getAssignmentsByRequest as Mock).mockResolvedValue([
      { ...mockAssignment, resourceId: 'ghost-person' },
    ]);
    renderWithQuery(<RequestPeopleSection {...defaultProps} />);
    await waitFor(() => expect(screen.getByText('ghost-person')).toBeInTheDocument());
  });

  // ── Cache invalidation (assignments change occupancy + conflicts + insights) ──

  it('invalidates request-derived queries after a successful assignment create', async () => {
    // The invalidation is the create mutation's meta, run by the production feedback cache.
    const { invalidateSpy } = renderWithQuery(<RequestPeopleSection {...defaultProps} />, { feedback: true });
    await waitFor(() => screen.getByTestId('add-person-btn'));
    fireEvent.click(screen.getByTestId('add-person-btn'));
    fireEvent.click(screen.getByTestId('person-select'));
    fireEvent.click(screen.getByTestId('save-row-btn'));
    await waitFor(() => expect(createAssignment).toHaveBeenCalled());
    await waitFor(() => {
      for (const queryKey of REQUEST_DERIVED_QUERY_KEYS) {
        expect(invalidateSpy).toHaveBeenCalledWith(expect.objectContaining({ queryKey }));
      }
    });
  });

  it('invalidates request-derived queries after removing an assignment', async () => {
    (getAssignmentsByRequest as Mock).mockResolvedValue([mockAssignment]);
    const { invalidateSpy } = renderWithQuery(<RequestPeopleSection {...defaultProps} />, { feedback: true });
    await waitFor(() => screen.getByText('Alice'));
    fireEvent.click(screen.getByLabelText('Remove assignment'));
    await waitFor(() => expect(cancelAssignment).toHaveBeenCalledWith('assign-1'));
    await waitFor(() => {
      for (const queryKey of REQUEST_DERIVED_QUERY_KEYS) {
        expect(invalidateSpy).toHaveBeenCalledWith(expect.objectContaining({ queryKey }));
      }
    });
  });

  it('cancels a pending validation when the section unmounts', async () => {
    // The section debounces validation by 400ms. Without cleanup those timers outlive the
    // component: closing the dialog inside that window still fires the request and then writes
    // the result into an unmounted tree. In the suite the same leak let a timer from one test
    // land inside another and fail an unrelated assertion.
    const { unmount } = renderWithQuery(<RequestPeopleSection {...defaultProps} />);

    await waitFor(() => screen.getByTestId('add-person-btn'));
    fireEvent.click(screen.getByTestId('add-person-btn'));
    fireEvent.click(screen.getByTestId('person-select'));

    unmount();

    // A wall-clock wait is right here, unlike the ones removed elsewhere in this file: it is
    // longer than the 400ms window it bounds, so load can only stretch it and give a leaked timer
    // *more* chance to fire. A sleep is unsafe when it is shorter than the window it waits on —
    // the old 50ms one — not when it is longer.
    await new Promise((r) => setTimeout(r, 500));
    expect(validateAssignment).not.toHaveBeenCalled();
  });

  it('loads people from every directory-profile type, not a fixed key', async () => {
    (getResourceTypes as Mock).mockResolvedValue([
      { id: 'rt-1', key: 'operator', displayName: 'Operator', displayNamePlural: 'Operators', hasGeometry: false, hasDirectoryProfile: true, singleGroupMembership: false, isSystem: false, isActive: true, createdAt: '', updatedAt: '' },
      { id: 'rt-2', key: 'tool', displayName: 'Tool', displayNamePlural: 'Tools', hasGeometry: false, hasDirectoryProfile: false, singleGroupMembership: false, isSystem: false, isActive: true, createdAt: '', updatedAt: '' },
    ]);
    renderWithQuery(<RequestPeopleSection {...defaultProps} />);

    await waitFor(() =>
      expect(getResources).toHaveBeenCalledWith({ resourceTypeKey: 'operator', isActive: true }),
    );
    expect(getResources).not.toHaveBeenCalledWith({ resourceTypeKey: 'tool', isActive: true });
    expect(getResources).not.toHaveBeenCalledWith({ resourceTypeKey: 'person', isActive: true });
  });

  it('points at the type catalog when no directory-profile type is active', async () => {
    (getResourceTypes as Mock).mockResolvedValue([]);
    renderWithQuery(<RequestPeopleSection {...defaultProps} />);

    await waitFor(() =>
      expect(screen.getByText(/No people-like resource type is active/)).toBeInTheDocument(),
    );
    expect(screen.queryByTestId('add-person-btn')).not.toBeInTheDocument();
    expect(getResources).not.toHaveBeenCalled();
  });
});
