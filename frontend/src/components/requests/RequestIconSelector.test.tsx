import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { RequestIconSelector } from './RequestIconSelector';

describe('RequestIconSelector', () => {
  it('opens the popover on trigger click and shows the curated icons', () => {
    render(<RequestIconSelector value={null} onChange={vi.fn()} />);
    fireEvent.click(screen.getByRole('button', { name: 'Pick an icon' }));
    // A representative subset of the curated set must be reachable by accessible name
    expect(screen.getByRole('button', { name: 'Calendar' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Hammer' })).toBeInTheDocument();
  });

  it('emits the chosen icon id and closes the popover', () => {
    const onChange = vi.fn();
    render(<RequestIconSelector value={null} onChange={onChange} />);
    fireEvent.click(screen.getByRole('button', { name: 'Pick an icon' }));
    fireEvent.click(screen.getByRole('button', { name: 'Calendar' }));
    expect(onChange).toHaveBeenCalledWith('calendar');
    expect(screen.queryByRole('button', { name: 'Hammer' })).toBeNull();
  });

  it('emits null when the user picks None', () => {
    const onChange = vi.fn();
    render(<RequestIconSelector value="calendar" onChange={onChange} />);
    fireEvent.click(screen.getByRole('button', { name: 'Icon: calendar' }));
    fireEvent.click(screen.getByRole('button', { name: /none/i }));
    expect(onChange).toHaveBeenCalledWith(null);
  });

  it('marks the currently selected icon as pressed', () => {
    render(<RequestIconSelector value="hammer" onChange={vi.fn()} />);
    fireEvent.click(screen.getByRole('button', { name: 'Icon: hammer' }));
    expect(screen.getByRole('button', { name: 'Hammer' })).toHaveAttribute('aria-pressed', 'true');
    expect(screen.getByRole('button', { name: 'Calendar' })).toHaveAttribute('aria-pressed', 'false');
  });
});
