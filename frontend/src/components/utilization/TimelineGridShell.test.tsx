import { describe, it, expect, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { TimelineGridShell, type ShellGroup } from './TimelineGridShell';
import { useAppStore } from '@foundation/src/store/app-store';
import type { TimeColumn } from './scheduler-types';

function makeColumn(hour: string): TimeColumn {
  const start = new Date(`2026-01-06T${hour}:00:00Z`);
  const end = new Date(start.getTime() + 60 * 60 * 1000);
  return { start, end, label: `${hour}:00` };
}

interface Row { id: string; name: string }

const columns = [makeColumn('08'), makeColumn('09'), makeColumn('10')];
const groups: ShellGroup<Row>[] = [
  { id: 'g1', name: 'Crew A', rows: [{ id: 'r1', name: 'Alice' }, { id: 'r2', name: 'Bob' }] },
];

function renderShell(extra: Partial<React.ComponentProps<typeof TimelineGridShell<Row>>> = {}) {
  return render(
    <TimelineGridShell<Row>
      labelHeader="Person"
      columns={columns}
      scale="week"
      groups={groups}
      collapseIdPrefix="people"
      getRowId={(r) => r.id}
      emptyMessage="Nothing here."
      renderRow={(r) => <div data-testid={`row-${r.id}`}>{r.name}</div>}
      {...extra}
    />,
  );
}

describe('TimelineGridShell', () => {
  beforeEach(() => {
    useAppStore.setState({ collapsedGroupIds: [] });
  });

  it('renders the label header and one header cell per column', () => {
    renderShell();
    expect(screen.getByText('Person')).toBeInTheDocument();
    expect(screen.getByText('08:00')).toBeInTheDocument();
    expect(screen.getByText('09:00')).toBeInTheDocument();
    expect(screen.getByText('10:00')).toBeInTheDocument();
  });

  it('renders a group header and its rows', () => {
    renderShell();
    expect(screen.getByText('Crew A')).toBeInTheDocument();
    expect(screen.getByTestId('row-r1')).toBeInTheDocument();
    expect(screen.getByTestId('row-r2')).toBeInTheDocument();
  });

  it('collapses the group when its header is clicked', () => {
    renderShell();
    fireEvent.click(screen.getByText('Crew A'));
    expect(screen.queryByTestId('row-r1')).not.toBeInTheDocument();
    expect(screen.queryByTestId('row-r2')).not.toBeInTheDocument();
  });

  it('shows the empty message when no rows exist', () => {
    renderShell({ groups: [{ id: 'g1', name: 'Crew A', rows: [] }] });
    expect(screen.getByText('Nothing here.')).toBeInTheDocument();
  });

  it('fires onColumnHeaderClick with the clicked column', () => {
    let clicked: TimeColumn | null = null;
    renderShell({ onColumnHeaderClick: (c) => { clicked = c; } });
    fireEvent.click(screen.getByText('09:00'));
    expect(clicked).not.toBeNull();
    expect(clicked!.label).toBe('09:00');
  });
});
