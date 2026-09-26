import { afterEach, beforeEach, describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent, waitFor, within } from '@testing-library/react';
import { restoreViewport, setViewport } from '@foundation/src/test-utils/viewport';
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
  it('opens as a modal popover, so its list scrolls inside a modal dialog', () => {
    // A modal Dialog's scroll lock blocks scrolling on everything outside it, and the popover is
    // portalled outside it. A modal popover brings its own lock, which then wins. The only
    // observable trace of that mode is the outside-pointer block it also switches on.
    render(<Combobox value="" onChange={() => {}} options={options} />);
    open();

    expect(document.body.style.pointerEvents).toBe('none');

    fireEvent.click(within(screen.getByRole('listbox')).getByText('Apple'));
    expect(document.body.style.pointerEvents).toBe('');
  });

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

describe('Combobox on a phone', () => {
  beforeEach(() => setViewport(375));
  afterEach(restoreViewport);

  it('opens as a full-screen sheet titled with the placeholder, listing every option', () => {
    render(<Combobox value="" onChange={() => {}} options={options} placeholder="Select a resource…" />);
    open();

    const sheet = screen.getByRole('dialog', { name: 'Select a resource…' });
    expect(within(sheet).getByPlaceholderText('Search…')).toBeInTheDocument();
    expect(within(sheet).getAllByRole('option')).toHaveLength(3);
  });

  it('does not raise the keyboard on open: the search box is not focused', async () => {
    render(<Combobox value="" onChange={() => {}} options={options} />);
    open();
    // The popover branch focuses on a timeout; give it the same chance here.
    await new Promise((r) => setTimeout(r, 10));

    expect(document.activeElement).not.toBe(screen.getByPlaceholderText('Search…'));
  });

  it('filters inside the sheet', () => {
    render(<Combobox value="" onChange={() => {}} options={options} />);
    open();
    fireEvent.change(screen.getByPlaceholderText('Search…'), { target: { value: 'ban' } });

    const listbox = screen.getByRole('listbox');
    expect(within(listbox).getAllByRole('option')).toHaveLength(1);
    expect(within(listbox).getByText('Banana')).toBeInTheDocument();
  });

  it('choosing an option reports it and closes the sheet', async () => {
    const onChange = vi.fn();
    render(<Combobox value="" onChange={onChange} options={options} />);
    open();
    fireEvent.click(within(screen.getByRole('listbox')).getByText('Cherry'));

    expect(onChange).toHaveBeenCalledWith('c');
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
  });

  it('shows the empty text in the sheet when nothing matches', () => {
    render(<Combobox value="" onChange={() => {}} options={options} emptyText="Nope" />);
    open();
    fireEvent.change(screen.getByPlaceholderText('Search…'), { target: { value: 'zzz' } });

    expect(screen.getByText('Nope')).toBeInTheDocument();
  });
});
