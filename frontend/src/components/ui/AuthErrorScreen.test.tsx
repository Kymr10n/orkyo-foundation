import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { AuthErrorScreen } from './AuthErrorScreen';
import { AUTH_MESSAGES } from '@/constants/auth';

describe('AuthErrorScreen', () => {
  it('renders backend error variant with correct title and detail', () => {
    render(
      <AuthErrorScreen
        variant="backend"
        title={AUTH_MESSAGES.BACKEND_ERROR_TITLE}
        detail={AUTH_MESSAGES.BACKEND_ERROR_DETAIL}
        onRetry={() => {}}
      />,
    );
    expect(screen.getByText(AUTH_MESSAGES.BACKEND_ERROR_TITLE)).toBeInTheDocument();
    expect(screen.getByText(AUTH_MESSAGES.BACKEND_ERROR_DETAIL)).toBeInTheDocument();
  });

  it('renders network error variant with correct title and detail', () => {
    render(
      <AuthErrorScreen
        variant="network"
        title={AUTH_MESSAGES.NETWORK_ERROR_TITLE}
        detail={AUTH_MESSAGES.NETWORK_ERROR_DETAIL}
        onRetry={() => {}}
      />,
    );
    expect(screen.getByText(AUTH_MESSAGES.NETWORK_ERROR_TITLE)).toBeInTheDocument();
    expect(screen.getByText(AUTH_MESSAGES.NETWORK_ERROR_DETAIL)).toBeInTheDocument();
  });

  it('renders a retry button', () => {
    render(
      <AuthErrorScreen
        variant="backend"
        title="Error"
        detail="Detail"
        onRetry={() => {}}
      />,
    );
    expect(screen.getByRole('button', { name: /try again/i })).toBeInTheDocument();
  });

  it('calls onRetry when the button is clicked', () => {
    const onRetry = vi.fn();
    render(
      <AuthErrorScreen
        variant="backend"
        title="Error"
        detail="Detail"
        onRetry={onRetry}
      />,
    );
    fireEvent.click(screen.getByRole('button', { name: /try again/i }));
    expect(onRetry).toHaveBeenCalledTimes(1);
  });
});
