import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { REQUEST_STATUS_ORDER } from '@foundation/src/constants/request-status';
import { ScheduleFilterBar } from './ScheduleFilterBar';
import { ISSUE_FILTER, ISSUE_FILTER_ORDER, type ScheduleFilter } from './schedule-filter';

const ALL: ScheduleFilter = {
  query: '',
  statuses: REQUEST_STATUS_ORDER,
  issues: ISSUE_FILTER_ORDER,
};

function renderBar(value: Partial<ScheduleFilter> = {}, counts = { match: 4, total: 4 }) {
  const onChange = vi.fn();
  render(
    <ScheduleFilterBar
      value={{ ...ALL, ...value }}
      onChange={onChange}
      matchCount={counts.match}
      totalCount={counts.total}
    />,
  );
  return onChange;
}

describe('ScheduleFilterBar', () => {
  it('reports a typed query', async () => {
    const onChange = renderBar();

    await userEvent.type(screen.getByLabelText('Search requests'), 'w');
    expect(onChange).toHaveBeenCalledWith({ query: 'w' });
  });

  it('offers every status the legend shows', async () => {
    renderBar();

    await userEvent.click(screen.getByRole('button', { name: 'Filter by status' }));
    for (const label of ['New', 'In Progress', 'Done', 'Deferred', 'Canceled']) {
      expect(await screen.findByRole('menuitem', { name: label })).toBeInTheDocument();
    }
  });

  it('offers the two severities plus a clean option', async () => {
    renderBar();

    await userEvent.click(screen.getByRole('button', { name: 'Filter by issue' }));
    for (const label of ['Conflicts', 'Warnings', 'No issues']) {
      expect(await screen.findByRole('menuitem', { name: label })).toBeInTheDocument();
    }
  });

  it('reports a narrowed status set', async () => {
    const onChange = renderBar();

    await userEvent.click(screen.getByRole('button', { name: 'Filter by status' }));
    await userEvent.click(await screen.findByRole('menuitem', { name: 'Done' }));

    expect(onChange).toHaveBeenCalledWith({
      statuses: REQUEST_STATUS_ORDER.filter((s) => s !== 'done'),
    });
  });

  it('shows no count or Clear while nothing is filtered', () => {
    renderBar();

    expect(screen.queryByRole('status')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Clear/ })).not.toBeInTheDocument();
  });

  it('shows how much is hidden once a filter is on', () => {
    // A zero-result calendar otherwise looks like a week with nothing scheduled.
    renderBar({ query: 'weld' }, { match: 2, total: 9 });

    expect(screen.getByRole('status')).toHaveTextContent('2 of 9');
  });

  it('counts a narrowed issue set as filtered', () => {
    renderBar({ issues: [ISSUE_FILTER.ERROR] }, { match: 1, total: 9 });

    expect(screen.getByRole('status')).toHaveTextContent('1 of 9');
  });

  it('resets everything from Clear', async () => {
    const onChange = renderBar({ query: 'weld', statuses: ['done'] });

    await userEvent.click(screen.getByRole('button', { name: /Clear/ }));

    expect(onChange).toHaveBeenCalledWith({
      query: '',
      statuses: REQUEST_STATUS_ORDER,
      issues: ISSUE_FILTER_ORDER,
    });
  });
});
