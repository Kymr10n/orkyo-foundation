import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MemoryRouter, Routes, Route } from 'react-router';
import { RequireTenantAdmin } from './RequireTenantAdmin';
import { toast } from 'sonner';
import { mockAuth } from '@foundation/src/test-utils/auth';

// Real permission hook, not the global test-mock from src/test/setup.ts.
vi.unmock('@foundation/src/hooks/usePermissions');

const authState: { membership: { isTenantAdmin?: boolean } | null; isSiteAdmin: boolean } = {
  membership: null,
  isSiteAdmin: false,
};

vi.mock('@foundation/src/contexts/AuthContext', () => ({
  useAuth: () => mockAuth({ membership: authState.membership, isSiteAdmin: authState.isSiteAdmin }),
}));

function renderGuard() {
  return render(
    <MemoryRouter initialEntries={['/tenant-admin']}>
      <Routes>
        <Route path="/" element={<div data-testid="home" />} />
        <Route
          path="/tenant-admin"
          element={
            <RequireTenantAdmin>
              <div data-testid="admin-content" />
            </RequireTenantAdmin>
          }
        />
      </Routes>
    </MemoryRouter>,
  );
}

describe('RequireTenantAdmin', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    authState.membership = null;
    authState.isSiteAdmin = false;
  });

  it('renders children for a site admin without a tenant-admin membership', () => {
    // The nav offers Administration to a site admin (useIsTenantAdmin); the guard must agree.
    authState.membership = { isTenantAdmin: false };
    authState.isSiteAdmin = true;
    renderGuard();
    expect(screen.getByTestId('admin-content')).toBeInTheDocument();
    expect(vi.mocked(toast.error)).not.toHaveBeenCalled();
  });

  it('renders children for tenant admins', () => {
    authState.membership = { isTenantAdmin: true };
    renderGuard();
    expect(screen.getByTestId('admin-content')).toBeInTheDocument();
  });

  it('redirects non-admin members to the app root', () => {
    authState.membership = { isTenantAdmin: false };
    renderGuard();
    expect(screen.queryByTestId('admin-content')).not.toBeInTheDocument();
    expect(screen.getByTestId('home')).toBeInTheDocument();
    expect(vi.mocked(toast.error)).toHaveBeenCalled();
  });

  it('redirects when membership is absent', () => {
    authState.membership = null;
    renderGuard();
    expect(screen.queryByTestId('admin-content')).not.toBeInTheDocument();
    expect(screen.getByTestId('home')).toBeInTheDocument();
  });
});
