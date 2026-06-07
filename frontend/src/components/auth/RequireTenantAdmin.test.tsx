import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MemoryRouter, Routes, Route } from 'react-router-dom';
import { RequireTenantAdmin } from './RequireTenantAdmin';

const authState: { membership: { isTenantAdmin?: boolean } | null } = { membership: null };

vi.mock('@foundation/src/contexts/AuthContext', () => ({
  useAuth: () => ({ membership: authState.membership }),
}));

const toastError = vi.fn();
vi.mock('sonner', () => ({ toast: { error: (...a: unknown[]) => toastError(...a) } }));

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
    expect(toastError).toHaveBeenCalled();
  });

  it('redirects when membership is absent', () => {
    authState.membership = null;
    renderGuard();
    expect(screen.queryByTestId('admin-content')).not.toBeInTheDocument();
    expect(screen.getByTestId('home')).toBeInTheDocument();
  });
});
