import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { SettingsPage } from './SettingsPage';

vi.mock('@foundation/src/contexts/AuthContext', () => ({
  useAuth: () => ({
    membership: { isTenantAdmin: true, tier: 'Professional' },
  }),
}));

vi.mock('@foundation/src/hooks/useSites', () => ({
  useSites: () => ({ data: [{ id: 's1', name: 'HQ' }, { id: 's2', name: 'Branch' }] }),
}));

// Mock all settings sub-components
vi.mock('@foundation/src/components/settings/CriteriaSettings', () => ({
  CriteriaSettings: () => <div data-testid="criteria-settings" />,
}));
vi.mock('@foundation/src/components/settings/GroupSettings', () => ({
  GroupSettings: () => <div data-testid="group-settings" />,
}));
vi.mock('@foundation/src/components/settings/PresetSettings', () => ({
  PresetSettings: () => <div data-testid="preset-settings" />,
}));
vi.mock('@foundation/src/components/settings/TemplateSettings', () => ({
  TemplateSettings: () => <div data-testid="template-settings" />,
}));
vi.mock('@foundation/src/components/settings/SiteSettings', () => ({
  SiteSettings: () => <div data-testid="site-settings" />,
}));
vi.mock('@foundation/src/components/settings/UserSettings', () => ({
  UserSettings: () => <div data-testid="user-settings" />,
}));
vi.mock('@foundation/src/components/settings/OrganizationSettings', () => ({
  OrganizationSettings: () => <div data-testid="org-settings" />,
}));
vi.mock('@foundation/src/components/settings/TenantConfigSettings', () => ({
  TenantConfigSettings: () => <div data-testid="config-settings" />,
}));
vi.mock('@foundation/src/components/settings/SchedulingSettings', () => ({
  SchedulingSettings: () => <div data-testid="scheduling-settings" />,
}));

function renderSettingsPage(initialPath = '/settings') {
  return render(
    <MemoryRouter
      initialEntries={[initialPath]}
    >
      <SettingsPage />
    </MemoryRouter>,
  );
}

describe('SettingsPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('renders page title', () => {
    renderSettingsPage();
    expect(screen.getByText('Settings')).toBeInTheDocument();
  });

  it('renders all tabs for admin user', () => {
    renderSettingsPage();
    expect(screen.getByText('Criteria')).toBeInTheDocument();
    expect(screen.getByText('Sites')).toBeInTheDocument();
    expect(screen.getByText('Groups')).toBeInTheDocument();
    expect(screen.getByText('Templates')).toBeInTheDocument();
    expect(screen.getByText('Presets')).toBeInTheDocument();
    expect(screen.getByText('Users')).toBeInTheDocument();
    expect(screen.getByText('Organization')).toBeInTheDocument();
    expect(screen.getByText('Scheduling')).toBeInTheDocument();
    expect(screen.getByText('Configuration')).toBeInTheDocument();
  });

  it('defaults to Criteria tab', () => {
    renderSettingsPage();
    expect(screen.getByTestId('criteria-settings')).toBeInTheDocument();
  });

  it('opens specific tab from URL param', () => {
    renderSettingsPage('/settings?tab=organization');
    expect(screen.getByTestId('org-settings')).toBeInTheDocument();
  });
});
