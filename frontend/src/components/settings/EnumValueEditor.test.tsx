import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { EnumValueEditor } from './EnumValueEditor';

describe('EnumValueEditor', () => {
  const onChange = vi.fn();

  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('renders input and add button', () => {
    render(<EnumValueEditor values={[]} onChange={onChange} />);
    expect(screen.getByPlaceholderText('Add value')).toBeInTheDocument();
    expect(screen.getByRole('button')).toBeInTheDocument();
  });

  it('adds a value on Enter', () => {
    render(<EnumValueEditor values={[]} onChange={onChange} />);
    const input = screen.getByPlaceholderText('Add value');
    fireEvent.change(input, { target: { value: 'Large' } });
    fireEvent.keyDown(input, { key: 'Enter' });
    expect(onChange).toHaveBeenCalledWith(['Large']);
  });

  it('adds a value on button click', () => {
    render(<EnumValueEditor values={[]} onChange={onChange} />);
    const input = screen.getByPlaceholderText('Add value');
    fireEvent.change(input, { target: { value: 'Small' } });
    fireEvent.click(screen.getAllByRole('button')[0]);
    expect(onChange).toHaveBeenCalledWith(['Small']);
  });

  it('does not add duplicate values', () => {
    render(<EnumValueEditor values={['Large']} onChange={onChange} />);
    const input = screen.getByPlaceholderText('Add value');
    fireEvent.change(input, { target: { value: 'Large' } });
    fireEvent.keyDown(input, { key: 'Enter' });
    expect(onChange).not.toHaveBeenCalled();
  });

  it('does not add empty values', () => {
    render(<EnumValueEditor values={[]} onChange={onChange} />);
    const input = screen.getByPlaceholderText('Add value');
    fireEvent.change(input, { target: { value: '   ' } });
    fireEvent.keyDown(input, { key: 'Enter' });
    expect(onChange).not.toHaveBeenCalled();
  });

  it('renders existing values as badges', () => {
    render(<EnumValueEditor values={['Small', 'Large']} onChange={onChange} />);
    expect(screen.getByText('Small')).toBeInTheDocument();
    expect(screen.getByText('Large')).toBeInTheDocument();
  });

  it('removes a value when X is clicked', () => {
    render(<EnumValueEditor values={['Small', 'Large']} onChange={onChange} />);
    // The remove buttons are inside badges; get all buttons and skip the Add button
    const smallBadge = screen.getByText('Small').closest('.gap-1');
    const removeButton = smallBadge!.querySelector('button')!;
    fireEvent.click(removeButton);
    expect(onChange).toHaveBeenCalledWith(['Large']);
  });

  it('disables input and buttons when disabled prop is true', () => {
    render(<EnumValueEditor values={[]} onChange={onChange} disabled />);
    expect(screen.getByPlaceholderText('Add value')).toBeDisabled();
  });
});
