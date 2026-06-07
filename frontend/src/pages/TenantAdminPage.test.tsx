import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Routes, Route, Navigate } from 'react-router-dom';
import { TenantAdminPage } from './TenantAdminPage';

const authState = { tier: 'professional' as string };
vi.mock('@foundation/src/contexts/AuthContext', () => ({
  useAuth: () => ({ membership: { isTenantAdmin: true, tier: authState.tier } }),
}));

const sitesState = { data: [{ id: 's1', name: 'HQ' }, { id: 's2', name: 'Branch' }] as { id: string; name: string }[] };
vi.mock('@foundation/src/hooks/useSites', () => ({
  useSites: () => ({ data: sitesState.data }),
}));

function Stub({ id }: { id: string }) {
  return <div data-testid={id} />;
}

function renderAt(initialPath: string) {
  return render(
    <MemoryRouter initialEntries={[initialPath]}>
      <Routes>
        <Route path="/tenant-admin" element={<TenantAdminPage />}>
          <Route index element={<Navigate to="sites" replace />} />
          <Route path="sites" element={<Stub id="site-settings" />} />
          <Route path="users" element={<Stub id="user-settings" />} />
          <Route path="organization" element={<Stub id="org-settings" />} />
          <Route path="configuration" element={<Stub id="config-settings" />} />
          <Route path="integrations" element={<Stub id="integrations-settings" />} />
          <Route path="usage-limits" element={<Stub id="usage-settings" />} />
        </Route>
      </Routes>
    </MemoryRouter>,
  );
}

describe('TenantAdminPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    authState.tier = 'professional';
    sitesState.data = [{ id: 's1', name: 'HQ' }, { id: 's2', name: 'Branch' }];
  });

  it('renders the Administration title', () => {
    renderAt('/tenant-admin/sites');
    expect(screen.getByRole('heading', { level: 1, name: 'Administration' })).toBeInTheDocument();
  });

  it('renders all six governance tabs', () => {
    renderAt('/tenant-admin/sites');
    for (const label of ['Sites', 'Users', 'Organization', 'Configuration', 'Integrations', 'Usage & Limits']) {
      expect(screen.getByRole('tab', { name: label })).toBeInTheDocument();
    }
  });

  it('hides the Sites tab on the free tier with a single site', () => {
    authState.tier = 'free';
    sitesState.data = [{ id: 's1', name: 'HQ' }];
    renderAt('/tenant-admin/users');
    expect(screen.queryByRole('tab', { name: 'Sites' })).not.toBeInTheDocument();
    expect(screen.getByRole('tab', { name: 'Users' })).toBeInTheDocument();
  });

  it('index route redirects to /tenant-admin/sites', () => {
    renderAt('/tenant-admin');
    expect(screen.getByTestId('site-settings')).toBeInTheDocument();
  });

  it('clicking a tab navigates to its sub-route', async () => {
    renderAt('/tenant-admin/sites');
    await userEvent.click(screen.getByRole('tab', { name: 'Users' }));
    expect(screen.getByTestId('user-settings')).toBeInTheDocument();
  });
});
