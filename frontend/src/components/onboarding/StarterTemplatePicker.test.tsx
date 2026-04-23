import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import {
  StarterTemplatePicker,
  type StarterTemplate,
} from '@foundation/src/components/onboarding/StarterTemplatePicker';

const TEMPLATES: StarterTemplate[] = [
  { key: 'empty', name: 'Empty', description: 'Start from scratch', icon: 'file-plus', includesDemoData: false },
  { key: 'demo', name: 'Demo', description: 'Full sample data', icon: 'layout-dashboard', includesDemoData: true },
  { key: 'camping-site', name: 'Camping Site', description: 'Camping preset', icon: 'tent', includesDemoData: false },
];

describe('StarterTemplatePicker', () => {
  it('renders all template cards', () => {
    render(
      <StarterTemplatePicker
        templates={TEMPLATES}
        selected="empty"
        onSelect={() => {}}
      />
    );

    expect(screen.getByText('Empty')).toBeInTheDocument();
    expect(screen.getByText('Demo')).toBeInTheDocument();
    expect(screen.getByText('Camping Site')).toBeInTheDocument();
  });

  it('shows description text', () => {
    render(
      <StarterTemplatePicker
        templates={TEMPLATES}
        selected="empty"
        onSelect={() => {}}
      />
    );

    expect(screen.getByText('Start from scratch')).toBeInTheDocument();
    expect(screen.getByText('Full sample data')).toBeInTheDocument();
  });

  it('calls onSelect when a card is clicked', () => {
    const onSelect = vi.fn();

    render(
      <StarterTemplatePicker
        templates={TEMPLATES}
        selected="empty"
        onSelect={onSelect}
      />
    );

    fireEvent.click(screen.getByText('Demo'));
    expect(onSelect).toHaveBeenCalledWith('demo');
  });

  it('marks selected card with aria-checked', () => {
    render(
      <StarterTemplatePicker
        templates={TEMPLATES}
        selected="demo"
        onSelect={() => {}}
      />
    );

    const demoCard = screen.getByRole('radio', { checked: true });
    expect(demoCard).toBeInTheDocument();
    // The checked card should contain the "Demo" text
    expect(demoCard).toHaveTextContent('Demo');
  });

  it('shows "sample data" badge only for demo templates', () => {
    render(
      <StarterTemplatePicker
        templates={TEMPLATES}
        selected="empty"
        onSelect={() => {}}
      />
    );

    const badges = screen.getAllByText('sample data');
    expect(badges).toHaveLength(1);
  });

  it('does not call onSelect when disabled', () => {
    const onSelect = vi.fn();

    render(
      <StarterTemplatePicker
        templates={TEMPLATES}
        selected="empty"
        onSelect={onSelect}
        disabled
      />
    );

    fireEvent.click(screen.getByText('Demo'));
    expect(onSelect).not.toHaveBeenCalled();
  });

  it('handles keyboard selection with Enter', () => {
    const onSelect = vi.fn();

    render(
      <StarterTemplatePicker
        templates={TEMPLATES}
        selected="empty"
        onSelect={onSelect}
      />
    );

    const campingCard = screen.getByText('Camping Site').closest('[role="radio"]')!;
    fireEvent.keyDown(campingCard, { key: 'Enter' });
    expect(onSelect).toHaveBeenCalledWith('camping-site');
  });

  it('shows helper text', () => {
    render(
      <StarterTemplatePicker
        templates={TEMPLATES}
        selected="empty"
        onSelect={() => {}}
      />
    );

    expect(screen.getByText(/choose a starting point/i)).toBeInTheDocument();
  });
});
