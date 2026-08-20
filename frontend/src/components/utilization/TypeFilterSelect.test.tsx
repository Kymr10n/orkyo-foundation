import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { TypeFilterSelect } from './TypeFilterSelect';
import type { ResourceTypeInfo } from '@foundation/src/lib/api/resource-types-api';

const MILL = { key: 'mill', displayNamePlural: 'Mills' } as ResourceTypeInfo;
const DRILL = { key: 'drill', displayNamePlural: 'Drills' } as ResourceTypeInfo;
const PRESS = { key: 'press', displayNamePlural: 'Presses' } as ResourceTypeInfo;
const ALL = [MILL, DRILL, PRESS];

function renderSelect(selected: string[], available = ALL) {
  const onChange = vi.fn();
  render(<TypeFilterSelect available={available} selected={selected} onChange={onChange} />);
  return onChange;
}

describe('TypeFilterSelect', () => {
  it('reads "All types" when nothing is filtered out', () => {
    renderSelect(['mill', 'drill', 'press']);

    expect(screen.getByRole('button', { name: 'Filter by type' })).toHaveTextContent('All types');
  });

  it('names a single choice rather than counting it', () => {
    renderSelect(['drill']);

    expect(screen.getByRole('button', { name: 'Filter by type' })).toHaveTextContent('Drills');
  });

  it('counts a partial choice', () => {
    renderSelect(['mill', 'drill']);

    expect(screen.getByRole('button', { name: 'Filter by type' })).toHaveTextContent('2 types');
  });

  it('hides itself when there is nothing to choose between', () => {
    render(<TypeFilterSelect available={[MILL]} selected={['mill']} onChange={vi.fn()} />);

    expect(screen.queryByRole('button', { name: 'Filter by type' })).not.toBeInTheDocument();
  });

  it('removes a type that was on', async () => {
    const onChange = renderSelect(['mill', 'drill']);

    await userEvent.click(screen.getByRole('button', { name: 'Filter by type' }));
    await userEvent.click(await screen.findByRole('menuitem', { name: 'Mills' }));

    expect(onChange).toHaveBeenCalledWith(['drill']);
  });

  it('adds a type that was off', async () => {
    const onChange = renderSelect(['mill']);

    await userEvent.click(screen.getByRole('button', { name: 'Filter by type' }));
    await userEvent.click(await screen.findByRole('menuitem', { name: 'Drills' }));

    expect(onChange).toHaveBeenCalledWith(['mill', 'drill']);
  });

  it('never emits an empty selection', async () => {
    // An empty grid with no visible reason reads as missing data, so clearing the last type
    // falls back to everything.
    const onChange = renderSelect(['mill']);

    await userEvent.click(screen.getByRole('button', { name: 'Filter by type' }));
    await userEvent.click(await screen.findByRole('menuitem', { name: 'Mills' }));

    expect(onChange).toHaveBeenCalledWith(['mill', 'drill', 'press']);
  });

  it('restores everything from the All entry', async () => {
    const onChange = renderSelect(['mill']);

    await userEvent.click(screen.getByRole('button', { name: 'Filter by type' }));
    await userEvent.click(await screen.findByRole('menuitem', { name: 'All types' }));

    expect(onChange).toHaveBeenCalledWith(['mill', 'drill', 'press']);
  });

  it('stays open so a set can be built in one visit', async () => {
    const onChange = renderSelect(['mill', 'drill', 'press']);

    await userEvent.click(screen.getByRole('button', { name: 'Filter by type' }));
    await userEvent.click(await screen.findByRole('menuitem', { name: 'Mills' }));

    expect(screen.getByRole('menuitem', { name: 'Drills' })).toBeInTheDocument();
    expect(onChange).toHaveBeenCalledTimes(1);
  });
});
