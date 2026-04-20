import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { RequestRequirementsSection } from './RequestRequirementsSection';
import type { Criterion } from '@/types/criterion';
import type { ReactNode } from 'react';

vi.mock('@/components/ui/collapsible', () => ({
  Collapsible: ({ children, open }: { children: ReactNode; open: boolean }) =>
    open ? <div>{children}</div> : <div>{(children as ReactNode[])?.[0]}</div>,
  CollapsibleTrigger: ({ children, ...props }: { children: ReactNode } & Record<string, unknown>) =>
    <button {...props}>{children}</button>,
  CollapsibleContent: ({ children }: { children: ReactNode }) => <div>{children}</div>,
}));

vi.mock('@/components/ui/select', () => ({
  Select: ({ children }: { children: ReactNode }) => <div>{children}</div>,
  SelectTrigger: ({ children }: { children: ReactNode }) => <div>{children}</div>,
  SelectValue: ({ placeholder }: { placeholder: string }) => <span>{placeholder}</span>,
  SelectContent: ({ children }: { children: ReactNode }) => <div>{children}</div>,
  SelectItem: ({ children }: { children: ReactNode; value: string }) => <div>{children}</div>,
}));

vi.mock('@/components/ui/badge', () => ({
  Badge: ({ children }: { children: ReactNode }) => <span>{children}</span>,
}));

vi.mock('@/components/ui/button', () => ({
  Button: ({ children, onClick, disabled, ...props }: { children: ReactNode; onClick?: () => void; disabled?: boolean } & Record<string, unknown>) =>
    <button onClick={onClick} disabled={disabled} {...props}>{children}</button>,
}));

vi.mock('./CriterionRequirementInput', () => ({
  CriterionRequirementInput: ({ criterion }: { criterion: Criterion }) =>
    <div data-testid={`input-${criterion.id}`}>{criterion.name}</div>,
}));

const mockCriteria: Criterion[] = [
  { id: 'c1', name: 'Power', dataType: 'Boolean', description: '', unit: undefined, enumValues: [], createdAt: '', updatedAt: '' },
  { id: 'c2', name: 'Load', dataType: 'Number', description: '', unit: 'kg', enumValues: [], createdAt: '', updatedAt: '' },
];

const baseState = {
  name: '',
  description: '',
  planningMode: 'leaf' as const,
  parentRequestId: '',
  selectedSpaceId: '',
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
  requirements: new Map<string, boolean | number | string | null>(),
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
    onRequirementValueChange: vi.fn(),
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
      requirements: new Map([['c1', true]]),
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
});
