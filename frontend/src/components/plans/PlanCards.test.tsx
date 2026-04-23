import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor, fireEvent } from '@testing-library/react';
import { PlanCards } from './PlanCards';

// Mock the interest API
const mockRegisterInterest = vi.fn();
vi.mock('@foundation/src/lib/api/interest-api', () => ({
  registerInterest: (...args: unknown[]) => mockRegisterInterest(...args),
}));

// Mock AuthContext
vi.mock('@foundation/src/contexts/AuthContext', () => ({
  useAuth: () => ({
    membership: { tenantId: 'tenant-abc-123', slug: 'acme', displayName: 'ACME Corp' },
    appUser: { id: 'user-1', email: 'owner@acme.com', displayName: 'Owner' },
  }),
}));

describe('PlanCards', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockRegisterInterest.mockResolvedValue({ message: 'Interest registered successfully' });
  });

  it('renders all three plan cards', () => {
    render(<PlanCards />);

    expect(screen.getByText('Free')).toBeInTheDocument();
    expect(screen.getByText('Professional')).toBeInTheDocument();
    expect(screen.getByText('Enterprise')).toBeInTheDocument();
  });

  it('shows Current badge on free plan', () => {
    render(<PlanCards />);

    expect(screen.getByText('Current')).toBeInTheDocument();
  });

  it('shows Coming Soon badges on premium plans', () => {
    render(<PlanCards />);

    const comingSoon = screen.getAllByText('Coming Soon');
    expect(comingSoon).toHaveLength(2);
  });

  it('shows free plan limits', () => {
    render(<PlanCards />);

    expect(screen.getByText('5')).toBeInTheDocument(); // Users
    expect(screen.getByText('1 GB')).toBeInTheDocument(); // Storage
  });

  it('shows Notify me buttons for premium plans', () => {
    render(<PlanCards />);

    const notifyButtons = screen.getAllByRole('button', { name: /notify me/i });
    expect(notifyButtons).toHaveLength(2);
  });

  it('opens interest dialog when clicking Notify me on Professional', async () => {
    render(<PlanCards />);

    const notifyButtons = screen.getAllByRole('button', { name: /notify me/i });
    fireEvent.click(notifyButtons[0]);

    await waitFor(() => {
      expect(screen.getByText(/get notified about professional/i)).toBeInTheDocument();
    });
  });

  it('opens interest dialog when clicking Notify me on Enterprise', async () => {
    render(<PlanCards />);

    const notifyButtons = screen.getAllByRole('button', { name: /notify me/i });
    fireEvent.click(notifyButtons[1]);

    await waitFor(() => {
      expect(screen.getByText(/get notified about enterprise/i)).toBeInTheDocument();
    });
  });

  it('pre-fills email from auth context', async () => {
    render(<PlanCards />);

    const notifyButtons = screen.getAllByRole('button', { name: /notify me/i });
    fireEvent.click(notifyButtons[0]);

    await waitFor(() => {
      const emailInput = screen.getByLabelText(/email for interest registration/i);
      expect(emailInput).toHaveValue('owner@acme.com');
    });
  });

  it('prefers userEmail prop over auth context', async () => {
    render(<PlanCards userEmail="override@example.com" />);

    const notifyButtons = screen.getAllByRole('button', { name: /notify me/i });
    fireEvent.click(notifyButtons[0]);

    await waitFor(() => {
      const emailInput = screen.getByLabelText(/email for interest registration/i);
      expect(emailInput).toHaveValue('override@example.com');
    });
  });

  it.each([
    ['Professional', 0, 1],
    ['Enterprise',   1, 2],
  ])('submits interest registration with correct tier for %s', async (_name, buttonIndex, tier) => {
    render(<PlanCards />);

    const notifyButtons = screen.getAllByRole('button', { name: /notify me/i });
    fireEvent.click(notifyButtons[buttonIndex]);

    await waitFor(() => {
      expect(screen.getByText(/register interest/i)).toBeInTheDocument();
    });

    fireEvent.click(screen.getByRole('button', { name: /register interest/i }));

    await waitFor(() => {
      expect(mockRegisterInterest).toHaveBeenCalledWith(
        expect.objectContaining({
          email: 'owner@acme.com',
          tier,
          organizationId: 'tenant-abc-123',
          source: 'onboarding',
        })
      );
    });
  });

  it('shows Interest registered after successful submission', async () => {
    render(<PlanCards />);

    const notifyButtons = screen.getAllByRole('button', { name: /notify me/i });
    fireEvent.click(notifyButtons[0]);

    await waitFor(() => {
      expect(screen.getByRole('button', { name: /register interest/i })).toBeInTheDocument();
    });

    fireEvent.click(screen.getByRole('button', { name: /register interest/i }));

    await waitFor(() => {
      expect(screen.getByText(/interest registered/i)).toBeInTheDocument();
    });
  });

  it('shows error when registration fails', async () => {
    mockRegisterInterest.mockRejectedValue(new Error('Network error'));

    render(<PlanCards />);

    const notifyButtons = screen.getAllByRole('button', { name: /notify me/i });
    fireEvent.click(notifyButtons[0]);

    await waitFor(() => {
      expect(screen.getByRole('button', { name: /register interest/i })).toBeInTheDocument();
    });

    fireEvent.click(screen.getByRole('button', { name: /register interest/i }));

    await waitFor(() => {
      expect(screen.getByText(/network error/i)).toBeInTheDocument();
    });
  });

  it('handles already registered gracefully', async () => {
    mockRegisterInterest.mockRejectedValue(new Error('Interest already registered'));

    render(<PlanCards />);

    const notifyButtons = screen.getAllByRole('button', { name: /notify me/i });
    fireEvent.click(notifyButtons[0]);

    await waitFor(() => {
      expect(screen.getByRole('button', { name: /register interest/i })).toBeInTheDocument();
    });

    fireEvent.click(screen.getByRole('button', { name: /register interest/i }));

    await waitFor(() => {
      expect(screen.getByText(/interest registered/i)).toBeInTheDocument();
    });
  });

  it('shows free plan features', () => {
    render(<PlanCards />);

    // These features appear in multiple cards, so use getAllByText
    const coreApp = screen.getAllByText('Core application');
    expect(coreApp.length).toBeGreaterThanOrEqual(1);

    const dataExport = screen.getAllByText('Data export');
    expect(dataExport.length).toBeGreaterThanOrEqual(1);
  });

  it('shows premium features on Professional card', () => {
    render(<PlanCards />);

    // Audit log appears in Professional and Enterprise but not Free
    const auditLogEntries = screen.getAllByText('Audit log');
    expect(auditLogEntries.length).toBeGreaterThanOrEqual(2);
  });
});
