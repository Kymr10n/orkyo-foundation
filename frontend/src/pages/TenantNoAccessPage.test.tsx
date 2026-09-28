import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';

// Mock useAuth — the page reads sessionData/isSiteAdmin and sends LOGOUT.
const mockSend = vi.fn();
const mockAuthState = {
  sessionData: null as Record<string, unknown> | null,
  isSiteAdmin: false,
  send: mockSend,
};

vi.mock('@foundation/src/contexts/AuthContext', () => ({
  useAuth: () => mockAuthState,
}));

const mockNavigateToApex = vi.fn<(path?: string) => boolean>(() => true);
const mockGoToApex = vi.fn<(path?: string) => void>();
vi.mock('@foundation/src/lib/utils/tenant-navigation', () => ({
  navigateToApex: (...args: unknown[]) => mockNavigateToApex(...(args as [string])),
  goToApex: (...args: unknown[]) => mockGoToApex(...(args as [string])),
}));

const { configMock } = vi.hoisted(() => ({ configMock: { supportEmail: 'support@example.test' } }));
vi.mock('@foundation/src/config/runtime', () => ({ runtimeConfig: configMock }));

import { TenantNoAccessPage } from '@foundation/src/pages/TenantNoAccessPage';

describe('TenantNoAccessPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockAuthState.sessionData = { tenants: [{ slug: 'acme' }] };
    mockAuthState.isSiteAdmin = false;
    configMock.supportEmail = 'support@example.test';
    mockNavigateToApex.mockReturnValue(true);
  });

  it('explains the lack of access rather than showing a redirect spinner', () => {
    render(<TenantNoAccessPage />);

    expect(screen.getByRole('heading', { name: /no access to this workspace/i })).toBeInTheDocument();
    expect(screen.getByText(/isn't a member of this workspace/i)).toBeInTheDocument();
  });

  it('offers the apex workspace selector when the user has other workspaces', () => {
    render(<TenantNoAccessPage />);

    fireEvent.click(screen.getByRole('button', { name: /go to my workspaces/i }));

    // Apex "/" is the marketing page — the SPA entry point must be used.
    expect(mockGoToApex).toHaveBeenCalledWith('/login?auto=1');
  });

  it('hides the workspace switcher when the user belongs to no workspace', () => {
    mockAuthState.sessionData = { tenants: [] };

    render(<TenantNoAccessPage />);

    expect(screen.queryByRole('button', { name: /go to my workspaces/i })).not.toBeInTheDocument();
    expect(screen.getByText(/ask this workspace's administrator/i)).toBeInTheDocument();
  });

  it('shows the site-admin shortcut only for site admins', () => {
    render(<TenantNoAccessPage />);
    expect(screen.queryByRole('button', { name: /open site admin/i })).not.toBeInTheDocument();

    mockAuthState.isSiteAdmin = true;
    render(<TenantNoAccessPage />);
    fireEvent.click(screen.getByRole('button', { name: /open site admin/i }));

    expect(mockNavigateToApex).toHaveBeenCalledWith('/site-admin');
  });

  it('offers a support mailto when a support address is configured', () => {
    render(<TenantNoAccessPage />);

    expect(screen.getByRole('link', { name: /contact support/i }))
      .toHaveAttribute('href', 'mailto:support@example.test');
  });

  it('omits the support link when no support address is configured', () => {
    configMock.supportEmail = '';

    render(<TenantNoAccessPage />);

    expect(screen.queryByRole('link', { name: /contact support/i })).not.toBeInTheDocument();
  });

  it('signs the user out via the auth machine', () => {
    render(<TenantNoAccessPage />);

    fireEvent.click(screen.getByRole('button', { name: /sign out/i }));

    expect(mockSend).toHaveBeenCalledWith({ type: 'LOGOUT' });
  });
});
