import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';

// Mock session-api
const mockAcceptTos = vi.fn();

vi.mock('@foundation/src/lib/api/session-api', () => ({
  acceptTos: (...args: unknown[]) => mockAcceptTos(...args),
}));

import { TosPage, DEFAULT_TOS_TEXT } from '@foundation/src/pages/TosPage';

const defaultProps = {
  onAccept: vi.fn().mockResolvedValue(undefined),
  onCancel: vi.fn(),
  tosVersion: '2026-02',
};

describe('TosPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockAcceptTos.mockResolvedValue(undefined);
    defaultProps.onAccept.mockResolvedValue(undefined);
  });

  it('renders Terms of Service heading', () => {
    render(<TosPage {...defaultProps} />);
    expect(screen.getByText('Terms of Service')).toBeInTheDocument();
  });

  it('renders the built-in default text when no tosText is provided', () => {
    render(<TosPage {...defaultProps} />);
    expect(screen.getByText(/1\. Acceptance of Terms/)).toBeInTheDocument();
    expect(screen.getByText(/2\. Use of Service/)).toBeInTheDocument();
  });

  it('renders the provided tosText instead of the default', () => {
    render(<TosPage {...defaultProps} tosText={'Custom Terms\n\nBe excellent to each other.'} />);
    expect(screen.getByText(/Be excellent to each other\./)).toBeInTheDocument();
    expect(screen.queryByText(/1\. Acceptance of Terms/)).not.toBeInTheDocument();
  });

  it('does not render the legacy Public Demo banner', () => {
    render(<TosPage {...defaultProps} />);
    expect(screen.queryByText(/Public Demo/i)).not.toBeInTheDocument();
  });

  it('exports a default text matching the backend compiled default shape', () => {
    // Non-empty, plain text, no JSX/HTML — mirrors TenantSettings.DefaultTosText.
    expect(DEFAULT_TOS_TEXT).toMatch(/^1\. Acceptance of Terms/);
    expect(DEFAULT_TOS_TEXT).not.toMatch(/</);
  });

  it('renders checkbox for agreement', () => {
    render(<TosPage {...defaultProps} />);
    const checkbox = screen.getByRole('checkbox');
    expect(checkbox).toBeInTheDocument();
    expect(checkbox).not.toBeChecked();
  });

  it('shows agreement label text', () => {
    render(<TosPage {...defaultProps} />);
    expect(screen.getByText(/I have read and agree to the Terms of Service/i)).toBeInTheDocument();
  });

  it('disables accept button until checkbox is checked', () => {
    render(<TosPage {...defaultProps} />);

    const acceptButton = screen.getByRole('button', { name: /accept and continue/i });
    expect(acceptButton).toBeDisabled();

    const checkbox = screen.getByRole('checkbox');
    fireEvent.click(checkbox);

    expect(acceptButton).not.toBeDisabled();
  });

  it('calls acceptTos then onAccept on acceptance', async () => {
    render(<TosPage {...defaultProps} />);

    fireEvent.click(screen.getByRole('checkbox'));
    fireEvent.click(screen.getByRole('button', { name: /accept and continue/i }));

    await waitFor(() => {
      expect(mockAcceptTos).toHaveBeenCalledWith('2026-02');
    });

    await waitFor(() => {
      expect(defaultProps.onAccept).toHaveBeenCalled();
    });
  });

  it('shows error when acceptance fails', async () => {
    const consoleSpy = vi.spyOn(console, 'error').mockImplementation(() => {});
    mockAcceptTos.mockRejectedValue(new Error('Server error'));

    render(<TosPage {...defaultProps} />);

    fireEvent.click(screen.getByRole('checkbox'));
    fireEvent.click(screen.getByRole('button', { name: /accept and continue/i }));

    await waitFor(() => {
      expect(screen.getByText(/Server error/i)).toBeInTheDocument();
    });
    consoleSpy.mockRestore();
  });

  it('has cancel button that calls onCancel', () => {
    render(<TosPage {...defaultProps} />);

    const cancelButton = screen.getByRole('button', { name: /cancel/i });
    expect(cancelButton).toBeInTheDocument();
    fireEvent.click(cancelButton);

    expect(defaultProps.onCancel).toHaveBeenCalled();
  });

  it('shows version number in description', () => {
    render(<TosPage {...defaultProps} />);
    expect(screen.getByText(/Version 2026-02/)).toBeInTheDocument();
  });

  it('disables button while submitting', async () => {
    mockAcceptTos.mockImplementation(() => new Promise(() => {}));

    render(<TosPage {...defaultProps} />);

    fireEvent.click(screen.getByRole('checkbox'));

    const acceptButton = screen.getByRole('button', { name: /accept and continue/i });
    fireEvent.click(acceptButton);

    await waitFor(() => {
      expect(acceptButton).toBeDisabled();
    });
  });
});
