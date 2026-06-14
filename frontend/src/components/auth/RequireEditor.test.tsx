import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MemoryRouter, Routes, Route } from 'react-router-dom';
import { RequireEditor } from './RequireEditor';
import { useCanEdit } from '@foundation/src/hooks/usePermissions';
import type * as UsePermissions from '@foundation/src/hooks/usePermissions';

// useCanEdit is globally mocked to true in src/test/setup.ts; override per-test below.
vi.mock('@foundation/src/hooks/usePermissions', async (importOriginal) => {
  const actual = await importOriginal<typeof UsePermissions>();
  return { ...actual, useCanEdit: vi.fn(() => true) };
});

const toastError = vi.fn();
vi.mock('sonner', () => ({ toast: { error: (...a: unknown[]) => toastError(...a) } }));

function renderGuard() {
  return render(
    <MemoryRouter initialEntries={['/settings']}>
      <Routes>
        <Route path="/" element={<div data-testid="home" />} />
        <Route
          path="/settings"
          element={
            <RequireEditor>
              <div data-testid="settings-content" />
            </RequireEditor>
          }
        />
      </Routes>
    </MemoryRouter>,
  );
}

describe('RequireEditor', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(useCanEdit).mockReturnValue(true);
  });

  it('renders children for editors', () => {
    vi.mocked(useCanEdit).mockReturnValue(true);
    renderGuard();
    expect(screen.getByTestId('settings-content')).toBeInTheDocument();
  });

  it('redirects viewers to the app root', () => {
    vi.mocked(useCanEdit).mockReturnValue(false);
    renderGuard();
    expect(screen.queryByTestId('settings-content')).not.toBeInTheDocument();
    expect(screen.getByTestId('home')).toBeInTheDocument();
    expect(toastError).toHaveBeenCalledWith('Settings are available to editors and administrators only.');
  });
});
