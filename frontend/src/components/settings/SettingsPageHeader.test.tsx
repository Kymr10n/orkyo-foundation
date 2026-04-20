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

  it('does not render action area without children', () => {
    const { container } = render(
      <SettingsPageHeader title="Title" description="Desc" />,
    );
    // Only the title+description div, no action div
    const topDiv = container.firstChild as HTMLElement;
    expect(topDiv.children).toHaveLength(1);
  });
});
