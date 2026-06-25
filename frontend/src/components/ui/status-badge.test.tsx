import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import { StatusBadge, statusToVariant } from './status-badge';

describe('statusToVariant', () => {
  it('maps healthy/active to success', () => {
    expect(statusToVariant('active')).toBe('success');
  });

  it('maps paused states (pending, suspended) to warning', () => {
    expect(statusToVariant('pending')).toBe('warning');
    expect(statusToVariant('suspended')).toBe('warning');
  });

  it('maps terminal/destructive states (deleting, disabled) to destructive', () => {
    expect(statusToVariant('deleting')).toBe('destructive');
    expect(statusToVariant('disabled')).toBe('destructive');
  });

  it('maps inactive and unknown statuses to secondary', () => {
    expect(statusToVariant('inactive')).toBe('secondary');
    expect(statusToVariant('something-else')).toBe('secondary');
  });

  it('is case-insensitive', () => {
    expect(statusToVariant('ACTIVE')).toBe('success');
    expect(statusToVariant('Deleting')).toBe('destructive');
  });

  it('gives terminal states a distinct variant from healthy and paused states', () => {
    const active = statusToVariant('active');
    const paused = statusToVariant('suspended');
    const terminal = statusToVariant('deleting');
    expect(new Set([active, paused, terminal]).size).toBe(3);
  });
});

describe('StatusBadge', () => {
  it('renders the status string as the default label', () => {
    render(<StatusBadge status="active" />);
    expect(screen.getByText('active')).toBeInTheDocument();
  });

  it('renders a custom label when provided', () => {
    render(<StatusBadge status="inactive" label="Inactive" />);
    expect(screen.getByText('Inactive')).toBeInTheDocument();
  });
});
