import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import { SecuritySettings } from './SecuritySettings';

const mockSend = vi.fn();
vi.mock('@/contexts/AuthContext', () => ({
  useAuth: () => ({ send: mockSend }),
}));

const mockQueryResult = vi.hoisted(() => ({
  current: { data: null as unknown, isLoading: false, error: null as unknown },
}));

vi.mock('@tanstack/react-query', () => ({
  useQuery: () => mockQueryResult.current,
}));

vi.mock('@/lib/api/security-api', () => ({
  getSecurityInfo: vi.fn(),
}));

vi.mock('./PasswordSection', () => ({
  PasswordSection: ({ isFederated }: { isFederated: boolean }) => (
    <div data-testid="password-section">federated={String(isFederated)}</div>
  ),
}));

vi.mock('./MfaSection', () => ({
  MfaSection: () => <div data-testid="mfa-section" />,
}));

vi.mock('./SessionsSection', () => ({
  SessionsSection: () => <div data-testid="sessions-section" />,
}));

describe('SecuritySettings', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('shows loading spinner when loading', () => {
    mockQueryResult.current = { data: null, isLoading: true, error: null };
    const { container } = render(<SecuritySettings />);
    expect(container.querySelector('.animate-spin')).toBeTruthy();
  });

  it('shows error alert when query fails', () => {
    mockQueryResult.current = { data: null, isLoading: false, error: new Error('fail') };
    render(<SecuritySettings />);
    expect(screen.getByText(/Failed to load security settings/)).toBeInTheDocument();
  });

  it('renders all sections when data is loaded', () => {
    mockQueryResult.current = {
      data: { isFederated: false, identityProvider: null },
      isLoading: false,
      error: null,
    };
    render(<SecuritySettings />);
    expect(screen.getByText('Security')).toBeInTheDocument();
    expect(screen.getByTestId('password-section')).toBeInTheDocument();
    expect(screen.getByTestId('mfa-section')).toBeInTheDocument();
    expect(screen.getByTestId('sessions-section')).toBeInTheDocument();
  });

  it('passes isFederated from security info', () => {
    mockQueryResult.current = {
      data: { isFederated: true, identityProvider: 'google' },
      isLoading: false,
      error: null,
    };
    render(<SecuritySettings />);
    expect(screen.getByTestId('password-section').textContent).toContain('federated=true');
  });
});
