import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import { FormField } from './FormField';

describe('FormField', () => {
  it('renders the label wired to the control and no marker/help by default', () => {
    render(
      <FormField htmlFor="fld" label="Name">
        <input id="fld" />
      </FormField>,
    );
    // Label is associated with the control (htmlFor → id).
    expect(screen.getByLabelText('Name')).toBe(document.getElementById('fld'));
    expect(screen.queryByText('*')).not.toBeInTheDocument();
  });

  it('renders a required asterisk in a destructive class when required', () => {
    render(
      <FormField htmlFor="email" label="Email" required>
        <input id="email" />
      </FormField>,
    );
    const marker = screen.getByText('*');
    expect(marker).toHaveClass('text-destructive');
    // The asterisk is part of the label's accessible name.
    expect(screen.getByLabelText(/Email/)).toBe(document.getElementById('email'));
  });

  it('renders help text under the control', () => {
    render(
      <FormField htmlFor="code" label="Code" help="Alphanumeric only">
        <input id="code" />
      </FormField>,
    );
    const help = screen.getByText('Alphanumeric only');
    expect(help).toHaveClass('text-xs', 'text-muted-foreground');
  });
});
