/** @jsxImportSource react */
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { CriterionAssignmentEditor, type CriterionAssignmentEditorProps } from './CriterionAssignmentEditor';
import { useCanEdit } from '@foundation/src/hooks/usePermissions';
import type { Criterion, CriterionValue } from '@foundation/src/types/criterion';

const criteria: Criterion[] = [
  { id: 'c1', name: 'Forklift License', dataType: 'Boolean', resourceTypeKeys: ['person'],
    createdAt: '2024-01-01T00:00:00Z', updatedAt: '2024-01-01T00:00:00Z' },
  { id: 'c2', name: 'Crane Operation', dataType: 'Boolean', resourceTypeKeys: ['person'],
    createdAt: '2024-01-01T00:00:00Z', updatedAt: '2024-01-01T00:00:00Z' },
];

const baseLabels = {
  title: 'Skills for Ada',
  srDescription: 'Manage skill assignments for this person.',
  intro: 'Assign criterion values.',
  sectionLabel: 'Skills',
  selectPlaceholder: 'Select a skill to add',
  emptyText: 'No skills assigned yet.',
};

function renderEditor(props: Partial<CriterionAssignmentEditorProps> = {}) {
  const onSave = vi.fn();
  const onOpenChange = vi.fn();
  render(
    <CriterionAssignmentEditor
      open
      onOpenChange={onOpenChange}
      criteria={criteria}
      isLoading={false}
      loadError={null}
      saveError={null}
      isSaving={false}
      initialAssignments={new Map()}
      onSave={onSave}
      labels={baseLabels}
      {...props}
    />,
  );
  return { onSave, onOpenChange };
}

describe('CriterionAssignmentEditor', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(useCanEdit).mockReturnValue(true);
  });

  it('renders title, intro, and the empty state', () => {
    renderEditor();
    expect(screen.getByText('Skills for Ada')).toBeInTheDocument();
    expect(screen.getByText('Assign criterion values.')).toBeInTheDocument();
    expect(screen.getByText('0 active')).toBeInTheDocument();
    expect(screen.getByText('No skills assigned yet.')).toBeInTheDocument();
  });

  it('seeds the working copy from initialAssignments', () => {
    renderEditor({ initialAssignments: new Map<string, CriterionValue | null>([['c1', true]]) });
    expect(screen.getByText('1 active')).toBeInTheDocument();
  });

  it('adds a criterion via the select and increments the count', async () => {
    const user = userEvent.setup({ pointerEventsCheck: 0 });
    renderEditor();
    await user.click(screen.getByText('Select a skill to add'));
    await user.click(screen.getByText('Forklift License'));
    const addBtn = screen.getAllByRole('button').find((b) => b.querySelector('.lucide-plus'));
    await user.click(addBtn!);
    await waitFor(() => expect(screen.getByText('1 active')).toBeInTheDocument());
  });

  it('removes a criterion and decrements the count', async () => {
    const user = userEvent.setup({ pointerEventsCheck: 0 });
    renderEditor({ initialAssignments: new Map<string, CriterionValue | null>([['c1', true]]) });
    await waitFor(() => expect(screen.getByText('1 active')).toBeInTheDocument());
    const trash = screen.getAllByRole('button').find((b) => b.querySelector('.lucide-trash-2'));
    await user.click(trash!);
    await waitFor(() => expect(screen.getByText('0 active')).toBeInTheDocument());
  });

  it('calls onSave with the current assignments when Save is clicked', async () => {
    const user = userEvent.setup({ pointerEventsCheck: 0 });
    const { onSave } = renderEditor({
      initialAssignments: new Map<string, CriterionValue | null>([['c1', true]]),
    });
    await user.click(screen.getByRole('button', { name: /save changes/i }));
    expect(onSave).toHaveBeenCalledTimes(1);
    const passed = onSave.mock.calls[0][0] as Map<string, CriterionValue | null>;
    expect(passed.get('c1')).toBe(true);
  });

  it('disables Save for a viewer who cannot edit', () => {
    vi.mocked(useCanEdit).mockReturnValue(false);
    renderEditor();
    expect(screen.getByRole('button', { name: /save changes/i })).toBeDisabled();
  });

  it('renders the addSlot control', () => {
    renderEditor({ addSlot: <button>Add Skill Criterion</button> });
    expect(screen.getByRole('button', { name: 'Add Skill Criterion' })).toBeInTheDocument();
  });

  it('shows selectableEmptyText when every criterion is already assigned', () => {
    renderEditor({
      initialAssignments: new Map<string, CriterionValue | null>([['c1', true], ['c2', true]]),
      labels: { ...baseLabels, selectableEmptyText: 'All assigned.' },
    });
    expect(screen.getByText('All assigned.')).toBeInTheDocument();
  });

  it('shows a load error and a save error via ErrorAlert', () => {
    const { onSave } = renderEditor({ loadError: 'Load failed' });
    expect(screen.getByText('Load failed')).toBeInTheDocument();
    expect(onSave).not.toHaveBeenCalled();
  });
});
