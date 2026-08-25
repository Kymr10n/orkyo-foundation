import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { AiAssistantSettings } from './AiAssistantSettings';
import {
  useAiAllowances,
  useAiCredential,
  useAiDailyLimits,
  useSaveAiDailyLimits,
} from '@foundation/src/hooks/useAiAssistant';
import { useAiAssistantAvailable } from '@foundation/src/hooks/useAiAssistantAvailable';

vi.mock('@foundation/src/hooks/useAiAssistantAvailable', () => ({
  useAiAssistantAvailable: vi.fn(() => true),
}));

const saveLimits = vi.fn();

vi.mock('@foundation/src/hooks/useAiAssistant', () => ({
  useAiCredential: vi.fn(),
  useAiAllowances: vi.fn(),
  useAiDailyLimits: vi.fn(),
  useSaveAiCredential: () => ({ mutateAsync: vi.fn(), isPending: false }),
  useDeleteAiCredential: () => ({ mutateAsync: vi.fn(), isPending: false }),
  useTestAiCredential: () => ({ mutateAsync: vi.fn(), isPending: false }),
  useSaveAiAllowance: () => ({ mutateAsync: vi.fn(), isPending: false }),
  useRevokeAiAllowance: () => ({ mutateAsync: vi.fn(), isPending: false }),
  useSaveAiDailyLimits: vi.fn(),
}));

const toastError = vi.fn();
const toastSuccess = vi.fn();
vi.mock('sonner', () => ({
  toast: {
    error: (...args: unknown[]) => toastError(...args),
    success: (...args: unknown[]) => toastSuccess(...args),
  },
}));

/** Sets the daily-limits query state; defaults to a configured workspace. */
function setLimits(over: Record<string, unknown> = {}) {
  vi.mocked(useAiDailyLimits).mockReturnValue({
    data: { userDailyTurns: 15, tenantDailyTurns: 150 },
    isLoading: false,
    isError: false,
    ...over,
  } as unknown as ReturnType<typeof useAiDailyLimits>);
}

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(useAiAssistantAvailable).mockReturnValue(true);
  vi.mocked(useAiCredential).mockReturnValue({
    data: { configured: true, keyHint: 'hAAA', lastVerifiedAt: null },
    isLoading: false,
  } as unknown as ReturnType<typeof useAiCredential>);
  vi.mocked(useAiAllowances).mockReturnValue({
    data: [],
    isLoading: false,
  } as unknown as ReturnType<typeof useAiAllowances>);
  vi.mocked(useSaveAiDailyLimits).mockReturnValue({
    mutateAsync: saveLimits,
    isPending: false,
  } as unknown as ReturnType<typeof useSaveAiDailyLimits>);
  saveLimits.mockResolvedValue(undefined);
  setLimits();
});

const perPerson = () => screen.getByLabelText(/interactions per person each day/i);
const perWorkspace = () => screen.getByLabelText(/whole workspace each day/i);

describe('AiAssistantSettings daily limits', () => {
  it('shows the limits the workspace already has', () => {
    render(<AiAssistantSettings />);

    expect(perPerson()).toHaveValue(15);
    expect(perWorkspace()).toHaveValue(150);
  });

  it('sends both fields together, so neither is overwritten by a stale value', async () => {
    render(<AiAssistantSettings />);
    const user = userEvent.setup();
    await user.clear(perPerson());
    await user.type(perPerson(), '20');
    await user.click(screen.getByRole('button', { name: /save limits/i }));

    expect(saveLimits).toHaveBeenCalledWith({ userDailyTurns: 20, tenantDailyTurns: 150 });
  });

  it('treats an empty field as no limit', async () => {
    render(<AiAssistantSettings />);
    const user = userEvent.setup();
    await user.clear(perWorkspace());
    await user.click(screen.getByRole('button', { name: /save limits/i }));

    expect(saveLimits).toHaveBeenCalledWith({ userDailyTurns: 15, tenantDailyTurns: null });
  });

  it('refuses an out-of-range limit instead of clearing it', async () => {
    // Coercing a bad value to null would read as "no limit" — the one outcome nobody
    // types by accident, and it would silently lift the ceiling.
    render(<AiAssistantSettings />);
    const user = userEvent.setup();
    await user.clear(perPerson());
    await user.type(perPerson(), '50000');
    await user.click(screen.getByRole('button', { name: /save limits/i }));

    expect(saveLimits).not.toHaveBeenCalled();
    expect(toastError).toHaveBeenCalled();
  });

  it('does not offer the form when the limits could not be read', () => {
    // Empty fields mean "no limit", so a form rendered blank after a failed read looks
    // exactly like a workspace that has none — and one save would clear the real ones.
    setLimits({ data: undefined, isError: true });

    render(<AiAssistantSettings />);

    expect(screen.queryByRole('button', { name: /save limits/i })).not.toBeInTheDocument();
    expect(screen.getByText(/could not be loaded/i)).toBeInTheDocument();
  });

  it('reports a failed save', async () => {
    saveLimits.mockRejectedValue(new Error('nope'));
    render(<AiAssistantSettings />);
    const user = userEvent.setup();
    await user.click(screen.getByRole('button', { name: /save limits/i }));

    expect(toastError).toHaveBeenCalled();
    expect(toastSuccess).not.toHaveBeenCalled();
  });
});
