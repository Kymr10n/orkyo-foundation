import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { RouteErrorBoundary } from './RouteErrorBoundary';

vi.mock('@foundation/src/lib/core/logger', () => ({
  logger: { error: vi.fn() },
}));

import { logger } from '@foundation/src/lib/core/logger';

// Suppress React's noisy console.error for caught errors in tests
beforeEach(() => {
  vi.spyOn(console, 'error').mockImplementation(() => {});
});

function Bomb({ shouldThrow }: { shouldThrow: boolean }) {
  if (shouldThrow) throw new Error('boom');
  return <div>ok</div>;
}

describe('RouteErrorBoundary', () => {
  it('renders children when no error', () => {
    render(
      <RouteErrorBoundary>
        <Bomb shouldThrow={false} />
      </RouteErrorBoundary>,
    );
    expect(screen.getByText('ok')).toBeInTheDocument();
  });

  it('renders fallback UI when child throws', () => {
    render(
      <RouteErrorBoundary>
        <Bomb shouldThrow />
      </RouteErrorBoundary>,
    );
    expect(screen.getByText('Something went wrong')).toBeInTheDocument();
    expect(screen.getByText('boom')).toBeInTheDocument();
  });

  it('includes label in fallback text when provided', () => {
    render(
      <RouteErrorBoundary label="Tenants tab">
        <Bomb shouldThrow />
      </RouteErrorBoundary>,
    );
    expect(screen.getByText(/The Tenants tab view/)).toBeInTheDocument();
  });

  it('uses generic text when no label provided', () => {
    render(
      <RouteErrorBoundary>
        <Bomb shouldThrow />
      </RouteErrorBoundary>,
    );
    expect(screen.getByText(/This view/)).toBeInTheDocument();
  });

  it('calls logger.error with label and error on catch', () => {
    render(
      <RouteErrorBoundary label="Admin">
        <Bomb shouldThrow />
      </RouteErrorBoundary>,
    );
    expect(logger.error).toHaveBeenCalledWith(
      expect.stringContaining('[Admin]'),
      expect.any(Error),
      expect.anything(),
    );
  });

  it('resets error state when Try again is clicked', async () => {
    const user = userEvent.setup();
    // Use a closure variable so the child stops throwing before the reset re-render
    let shouldThrow = true;
    function ControlledBomb() {
      if (shouldThrow) throw new Error('boom');
      return <div>ok</div>;
    }

    render(
      <RouteErrorBoundary>
        <ControlledBomb />
      </RouteErrorBoundary>,
    );
    expect(screen.getByText('Something went wrong')).toBeInTheDocument();

    shouldThrow = false;
    await user.click(screen.getByRole('button', { name: /try again/i }));

    expect(screen.getByText('ok')).toBeInTheDocument();
    expect(screen.queryByText('Something went wrong')).not.toBeInTheDocument();
  });

  it('calls custom fallback renderer when provided', () => {
    const customFallback = vi.fn().mockReturnValue(<div>custom error</div>);
    render(
      <RouteErrorBoundary fallback={customFallback}>
        <Bomb shouldThrow />
      </RouteErrorBoundary>,
    );
    expect(screen.getByText('custom error')).toBeInTheDocument();
    expect(customFallback).toHaveBeenCalledWith(expect.any(Error), expect.any(Function));
  });

  it('does not render default fallback when custom fallback is provided', () => {
    render(
      <RouteErrorBoundary fallback={() => <div>custom</div>}>
        <Bomb shouldThrow />
      </RouteErrorBoundary>,
    );
    expect(screen.queryByText('Something went wrong')).not.toBeInTheDocument();
  });
});
