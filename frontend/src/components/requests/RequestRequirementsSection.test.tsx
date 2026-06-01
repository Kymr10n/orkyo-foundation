import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { RequestRequirementsSection } from './RequestRequirementsSection';
import type { Criterion } from '@foundation/src/types/criterion';
import type { RequirementEntry } from '@foundation/src/hooks/useRequestForm';
import type { ReactNode } from 'react';

vi.mock('@foundation/src/components/ui/collapsible', () => ({
  Collapsible: ({ children, onOpenChange }: { children: ReactNode; open: boolean; onOpenChange?: () => void }) =>
    <div onClick={onOpenChange}>{children}</div>,
  CollapsibleTrigger: ({ children, ...props }: { children: ReactNode } & Record<string, unknown>) =>
    <button {...props}>{children}</button>,
  CollapsibleContent: ({ children }: { children: ReactNode }) => <div>{children}</div>,
}));

vi.mock('@foundation/src/components/ui/select', () => ({
  Select: ({ children }: { children: ReactNode }) => <div>{children}</div>,
  SelectTrigger: ({ children }: { children: ReactNode }) => <div>{children}</div>,
  SelectValue: ({ placeholder }: { placeholder: string }) => <span>{placeholder}</span>,
  SelectContent: ({ children }: { children: ReactNode }) => <div>{children}</div>,
  SelectItem: ({ children }: { children: ReactNode; value: string }) => <div>{children}</div>,
}));

vi.mock('@foundation/src/components/ui/badge', () => ({
  Badge: ({ children }: { children: ReactNode }) => <span>{children}</span>,
}));

vi.mock('@foundation/src/components/ui/button', () => ({
  Button: ({ children, onClick, disabled, ...props }: { children: ReactNode; onClick?: () => void; disabled?: boolean } & Record<string, unknown>) =>
    <button onClick={onClick} disabled={disabled} {...props}>{children}</button>,
}));

vi.mock('./CriterionRequirementInput', () => ({
  CriterionRequirementInput: ({ criterion }: { criterion: Criterion }) =>
    <div data-testid={`input-${criterion.id}`}>{criterion.name}</div>,
}));

const mockCriteria: Criterion[] = [
  { id: 'c1', name: 'Power', dataType: 'Boolean', description: '', unit: undefined, enumValues: [], resourceTypeKeys: ['space'],
      createdAt: '', updatedAt: '' },
  { id: 'c2', name: 'Load', dataType: 'Number', description: '', unit: 'kg', enumValues: [], resourceTypeKeys: ['space'],
      createdAt: '', updatedAt: '' },
];

const baseState = {
  name: '',
  description: '',
  icon: null,
  planningMode: 'leaf' as const,
  parentRequestId: '',
  selectedResourceId: '',
  startDate: '',
  startTime: '',
  endDate: '',
  endTime: '',
  earliestStartDate: '',
  earliestStartTime: '',
  latestEndDate: '',
  latestEndTime: '',
  durationValue: 0,
  durationUnit: 'hours' as const,
  schedulingSettingsApply: false,
  requirements: new Map<string, RequirementEntry>(),
  selectedCriterionId: '',
  openSections: { basic: true, schedule: true, constraints: true, duration: true, requirements: true },
};

describe('RequestRequirementsSection', () => {
  const defaultProps = {
    state: baseState,
    toggleSection: vi.fn(),
    availableCriteria: mockCriteria,
    selectedCriterionId: '',
    setSelectedCriterionId: vi.fn(),
    isLoading: false,
    onAddRequirement: vi.fn(),
    onRemoveRequirement: vi.fn(),
    onRequirementChange: vi.fn(),
  };

  it('renders heading and badge', () => {
    render(<RequestRequirementsSection {...defaultProps} />);
    expect(screen.getByText('Requirements')).toBeInTheDocument();
    expect(screen.getByText('0 active')).toBeInTheDocument();
  });

  it('shows empty state when no requirements', () => {
    render(<RequestRequirementsSection {...defaultProps} />);
    expect(screen.getByText(/no requirements added yet/i)).toBeInTheDocument();
  });

  it('renders active requirements with input', () => {
    const stateWithReqs = {
      ...baseState,
      requirements: new Map<string, RequirementEntry>([['c1', { value: true }]]),
    };
    render(<RequestRequirementsSection {...defaultProps} state={stateWithReqs} />);
    expect(screen.getByText('1 active')).toBeInTheDocument();
    expect(screen.getByTestId('input-c1')).toBeInTheDocument();
  });

  it('calls onAddRequirement when add button clicked', () => {
    const onAdd = vi.fn();
    render(
      <RequestRequirementsSection
        {...defaultProps}
        selectedCriterionId="c1"
        onAddRequirement={onAdd}
      />,
    );
    const addBtn = screen.getAllByRole('button').find(b => !b.textContent?.includes('Requirements'));
    fireEvent.click(addBtn!);
    expect(onAdd).toHaveBeenCalled();
  });

  it('add button is disabled when no criterion is selected', () => {
    render(
      <RequestRequirementsSection
        {...defaultProps}
        selectedCriterionId=""
      />,
    );
    const addBtn = screen.getAllByRole('button').find(b => !b.textContent?.includes('Requirements'));
    expect(addBtn).toBeDisabled();
  });

  it('calls toggleSection when trigger is clicked', () => {
    const toggleSection = vi.fn();
    render(<RequestRequirementsSection {...defaultProps} toggleSection={toggleSection} />);
    fireEvent.click(screen.getByRole('button', { name: /requirements/i }));
    expect(toggleSection).toHaveBeenCalledWith('requirements');
  });

  it('calls onRemoveRequirement when trash button clicked', () => {
    const onRemove = vi.fn();
    const stateWithReqs = {
      ...baseState,
      requirements: new Map<string, RequirementEntry>([['c1', { value: true }]]),
    };
    render(
      <RequestRequirementsSection
        {...defaultProps}
        state={stateWithReqs}
        onRemoveRequirement={onRemove}
      />,
    );
    // All buttons: Collapsible trigger, add (+), and the remove trash button
    const buttons = screen.getAllByRole('button');
    // Remove button is the last one (after trigger and add)
    const removeBtn = buttons[buttons.length - 1];
    fireEvent.click(removeBtn);
    expect(onRemove).toHaveBeenCalledWith('c1');
  });

  it('hides add row when all criteria already have requirements', () => {
    const stateWithAll = {
      ...baseState,
      requirements: new Map<string, RequirementEntry>([
        ['c1', { value: true }],
        ['c2', { value: 5 }],
      ]),
    };
    render(<RequestRequirementsSection {...defaultProps} state={stateWithAll} />);
    // Both criteria used — no unused criteria → add row not rendered
    expect(screen.queryByText('Select a criterion to add')).not.toBeInTheDocument();
  });

  it('renders multiple requirements', () => {
    const stateWithTwo = {
      ...baseState,
      requirements: new Map<string, RequirementEntry>([
        ['c1', { value: true }],
        ['c2', { value: 10 }],
      ]),
    };
    render(<RequestRequirementsSection {...defaultProps} state={stateWithTwo} />);
    expect(screen.getByText('2 active')).toBeInTheDocument();
    expect(screen.getByTestId('input-c1')).toBeInTheDocument();
    expect(screen.getByTestId('input-c2')).toBeInTheDocument();
  });
});
