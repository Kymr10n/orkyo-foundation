import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { ResourceDirectoryFields, type DirectoryFormValues } from './ResourceDirectoryFields';

const EMPTY: DirectoryFormValues = { email: '', notes: '' };

function renderFields(value: Partial<DirectoryFormValues> = {}) {
  const onChange = vi.fn();
  render(<ResourceDirectoryFields value={{ ...EMPTY, ...value }} onChange={onChange} />);
  return onChange;
}

describe('ResourceDirectoryFields', () => {
  it('reports an edited email through onChange', async () => {
    const onChange = renderFields();

    await userEvent.type(screen.getByLabelText('Email'), 'a');
    expect(onChange).toHaveBeenCalledWith({ email: 'a' });
  });

  it('reports edited notes through onChange', async () => {
    const onChange = renderFields();

    await userEvent.type(screen.getByLabelText('Notes'), 'x');
    expect(onChange).toHaveBeenCalledWith({ notes: 'x' });
  });

  it('shows the stored values', () => {
    renderFields({ email: 'ada@example.com', notes: 'On leave' });

    expect(screen.getByLabelText('Email')).toHaveValue('ada@example.com');
    expect(screen.getByLabelText('Notes')).toHaveValue('On leave');
  });

  it('renders no job title or department control', () => {
    // Both became organization lists in 1820. They reach the form as ordinary list_lookup custom
    // fields, so this block must not grow a second, special-cased copy of them.
    renderFields();

    expect(screen.queryByLabelText(/Job Title/i)).not.toBeInTheDocument();
    expect(screen.queryByLabelText(/Department/i)).not.toBeInTheDocument();
  });
});
