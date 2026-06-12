import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent, within } from '@testing-library/react';
import { Combobox, type ComboboxOption } from './combobox';

const options: ComboboxOption[] = [
  { id: 'a', label: 'Apple' },
  { id: 'b', label: 'Banana' },
  { id: 'c', label: 'Cherry' },
];

function open() {
  fireEvent.click(screen.getByRole('combobox'));
}

describe('Combobox', () => {
  it('shows the placeholder when nothing is selected', () => {
    render(<Combobox value="" onChange={() => {}} options={options} placeholder="Pick fruit" />);
    expect(screen.getByText('Pick fruit')).toBeInTheDocument();
  });

  it('shows the selected option label', () => {
    render(<Combobox value="b" onChange={() => {}} options={options} />);
    expect(screen.getByText('Banana')).toBeInTheDocument();
  });

  it('opens, lists options, and selecting one calls onChange', () => {
    const onChange = vi.fn();
    render(<Combobox value="" onChange={onChange} options={options} />);
    open();
    const listbox = screen.getByRole('listbox');
    expect(within(listbox).getAllByRole('option')).toHaveLength(3);
    fireEvent.click(within(listbox).getByText('Cherry'));
    expect(onChange).toHaveBeenCalledWith('c');
  });

  it('filters options by the search query', () => {
    render(<Combobox value="" onChange={() => {}} options={options} searchPlaceholder="Find" />);
    open();
    fireEvent.change(screen.getByPlaceholderText('Find'), { target: { value: 'ban' } });
    const listbox = screen.getByRole('listbox');
    expect(within(listbox).getAllByRole('option')).toHaveLength(1);
    expect(within(listbox).getByText('Banana')).toBeInTheDocument();
  });

  it('shows the empty text when nothing matches', () => {
    render(<Combobox value="" onChange={() => {}} options={options} emptyText="Nope" />);
    open();
    fireEvent.change(screen.getByRole('textbox'), { target: { value: 'zzz' } });
    expect(screen.getByText('Nope')).toBeInTheDocument();
  });

  it('marks the active option as aria-selected', () => {
    render(<Combobox value="a" onChange={() => {}} options={options} />);
    open();
    const selected = within(screen.getByRole('listbox')).getByText('Apple').closest('button');
    expect(selected).toHaveAttribute('aria-selected', 'true');
  });
});
