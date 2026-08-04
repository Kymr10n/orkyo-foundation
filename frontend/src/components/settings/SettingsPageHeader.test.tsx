import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import { SettingsPageHeader } from './SettingsPageHeader';

describe('SettingsPageHeader', () => {
  it('renders title and description', () => {
    render(<SettingsPageHeader title="My Settings" description="Manage your configuration" />);
    expect(screen.getByText('My Settings')).toBeInTheDocument();
    expect(screen.getByText('Manage your configuration')).toBeInTheDocument();
  });

  it('renders children in the action area', () => {
    render(
      <SettingsPageHeader title="Title" description="Desc">
        <button>Action</button>
      </SettingsPageHeader>,
    );
    expect(screen.getByText('Action')).toBeInTheDocument();
  });

  it('lets the text column shrink while the action area holds its width', () => {
    // Regression: on a phone a long description ran underneath the action button, which
    // is whitespace-nowrap and so refuses to shrink.
    const { container } = render(
      <SettingsPageHeader title="Criteria Definitions" description="Define reusable criteria.">
        <button>Add Criterion</button>
      </SettingsPageHeader>,
    );
    const row = container.firstChild as HTMLElement;
    const [text, actions] = Array.from(row.children) as HTMLElement[];

    expect(row.className).toContain('gap-4');
    expect(text.className).toContain('min-w-0');
    expect(actions.className).toContain('shrink-0');
  });

  it('does not render action area without children', () => {
    const { container } = render(
      <SettingsPageHeader title="Title" description="Desc" />,
    );
    // Only the title+description div, no action div
    const topDiv = container.firstChild as HTMLElement;
    expect(topDiv.children).toHaveLength(1);
  });
});
