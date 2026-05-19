import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Routes, Route, Navigate, useLocation } from 'react-router-dom';
import { SettingsPage } from './SettingsPage';

vi.mock('@foundation/src/contexts/AuthContext', () => ({
  useAuth: () => ({
    membership: { isTenantAdmin: true, tier: 'Professional' },
  }),
}));

vi.mock('@foundation/src/hooks/useSites', () => ({
  useSites: () => ({ data: [{ id: 's1', name: 'HQ' }, { id: 's2', name: 'Branch' }] }),
}));

function Stub({ id }: { id: string }) {
  return <div data-testid={id} />;
}

function LocationProbe() {
  const loc = useLocation();
  return <div data-testid="path">{loc.pathname}</div>;
}

function renderAt(initialPath: string) {
  return render(
    <MemoryRouter initialEntries={[initialPath]}>
      <Routes>
        <Route path="/settings" element={<SettingsPage />}>
          <Route index element={<Navigate to="criteria" replace />} />
          <Route path="criteria" element={<Stub id="criteria-settings" />} />
          <Route path="sites" element={<Stub id="site-settings" />} />
          <Route path="templates" element={<Stub id="template-settings" />} />
          <Route path="presets" element={<Stub id="preset-settings" />} />
          <Route path="users" element={<Stub id="user-settings" />} />
          <Route path="organization" element={<Stub id="org-settings" />} />
          <Route path="scheduling" element={<Stub id="scheduling-settings" />} />
          <Route path="configuration" element={<Stub id="config-settings" />} />
        </Route>
        <Route path="*" element={<LocationProbe />} />
      </Routes>
    </MemoryRouter>,
  );
}

describe('SettingsPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('renders page title', () => {
    renderAt('/settings/criteria');
    expect(screen.getByRole('heading', { level: 1, name: 'Settings' })).toBeInTheDocument();
  });

  it('renders all tabs for admin user on Professional tier', () => {
    renderAt('/settings/criteria');
    for (const label of [
      'Criteria',
      'Sites',
      'Templates',
      'Presets',
      'Users',
      'Organization',
      'Scheduling',
      'Configuration',
    ]) {
      expect(screen.getByRole('tab', { name: label })).toBeInTheDocument();
    }
  });

  it('does not expose resource-domain master data tabs (moved to owning pages)', () => {
    renderAt('/settings/criteria');
    expect(screen.queryByRole('tab', { name: 'Groups' })).not.toBeInTheDocument();
    expect(screen.queryByRole('tab', { name: 'Departments' })).not.toBeInTheDocument();
    expect(screen.queryByRole('tab', { name: 'Job Titles' })).not.toBeInTheDocument();
  });

  it('index route redirects to /settings/criteria', () => {
    renderAt('/settings');
    expect(screen.getByTestId('criteria-settings')).toBeInTheDocument();
  });

  it.each([
    ['/settings/criteria', 'criteria-settings'],
    ['/settings/sites', 'site-settings'],
    ['/settings/templates', 'template-settings'],
    ['/settings/presets', 'preset-settings'],
    ['/settings/users', 'user-settings'],
    ['/settings/organization', 'org-settings'],
    ['/settings/scheduling', 'scheduling-settings'],
    ['/settings/configuration', 'config-settings'],
  ])('deep-links %s renders the right child', (path, id) => {
    renderAt(path);
    expect(screen.getByTestId(id)).toBeInTheDocument();
  });

  it('clicking a tab navigates to its sub-route', async () => {
    renderAt('/settings/criteria');
    await userEvent.click(screen.getByRole('tab', { name: 'Organization' }));
    expect(screen.getByTestId('org-settings')).toBeInTheDocument();
  });

  it.each([
    ['criteria', '/settings/criteria'],
    ['organization', '/settings/organization'],
    ['jobTitles', '/people/job-titles'],
    ['departments', '/people/departments'],
  ])('legacy ?tab=%s redirects to %s', (legacy, target) => {
    renderAt(`/settings?tab=${legacy}`);
    if (target.startsWith('/settings/')) {
      const id = `${target.split('/').pop()!}-settings`.replace('organization-settings', 'org-settings');
      expect(screen.getByTestId(id)).toBeInTheDocument();
    } else {
      expect(screen.getByTestId('path').textContent).toBe(target);
    }
  });
});
