import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import { Button } from './button';

describe('Button', () => {
  it('renders children', () => {
    render(<Button>Click me</Button>);
    expect(screen.getByRole('button', { name: 'Click me' })).toBeInTheDocument();
  });

  it('shows the spinner and disables the button when loading', () => {
    render(<Button loading>Save</Button>);
    const button = screen.getByRole('button', { name: 'Save' });
    expect(button).toBeDisabled();
    expect(button.querySelector('.animate-spin')).toBeInTheDocument();
  });

  it('does not show the spinner when not loading', () => {
    render(<Button>Save</Button>);
    const button = screen.getByRole('button', { name: 'Save' });
    expect(button).toBeEnabled();
    expect(button.querySelector('.animate-spin')).toBeNull();
  });

  it('stays disabled when loading even without an explicit disabled prop', () => {
    render(<Button loading>Save</Button>);
    expect(screen.getByRole('button', { name: 'Save' })).toBeDisabled();
  });

  it('applies the icon-sm size classes', () => {
    render(<Button size="icon-sm" aria-label="icon-sm-button">X</Button>);
    const button = screen.getByRole('button', { name: 'icon-sm-button' });
    expect(button.className).toContain('h-7');
    expect(button.className).toContain('w-7');
  });
});
