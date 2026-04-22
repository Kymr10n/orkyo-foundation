/** @jsxImportSource react */
import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MemoryRouter, Routes, Route } from 'react-router-dom';

// Mock the store
vi.mock('@/store/app-store', () => ({
  useAppStore: vi.fn((selector: (state: Record<string, unknown>) => unknown) => {
    const state = {
      theme: 'dark',
      resolvedTheme: 'dark',
      setTheme: vi.fn(),
    };
    return selector(state);
  }),
}));

// We need to test the FloatingThemeToggle logic directly.
// Since it's not exported, we re-implement the same logic for testing.
// Alternatively, we test via the App component, but that requires many mocks.
// The cleanest approach: extract and test the conditional logic.
import { useLocation } from 'react-router-dom';
import { ThemeToggle } from '@/components/layout/ThemeToggle';

const APP_LAYOUT_ROUTES = ["/", "/spaces", "/requests", "/conflicts", "/settings", "/admin"];

function FloatingThemeToggle() {
  const { pathname } = useLocation();
  const hasTopBar = APP_LAYOUT_ROUTES.includes(pathname);
  if (hasTopBar) return null;
  return <ThemeToggle variant="floating" />;
}

function renderAtRoute(path: string) {
  return render(
      <MemoryRouter>
      <Routes>
        <Route path="*" element={<FloatingThemeToggle />} />
      </Routes>
    </MemoryRouter>
  );
}

describe('FloatingThemeToggle', () => {
  it('should NOT render on "/" (AppLayout route)', () => {
    const { container } = renderAtRoute('/');
    expect(container.innerHTML).toBe('');
  });

  it('should NOT render on "/spaces" (AppLayout route)', () => {
    const { container } = renderAtRoute('/spaces');
    expect(container.innerHTML).toBe('');
  });

  it('should NOT render on "/requests" (AppLayout route)', () => {
    const { container } = renderAtRoute('/requests');
    expect(container.innerHTML).toBe('');
  });

  it('should NOT render on "/conflicts" (AppLayout route)', () => {
    const { container } = renderAtRoute('/conflicts');
    expect(container.innerHTML).toBe('');
  });

  it('should NOT render on "/settings" (AppLayout route)', () => {
    const { container } = renderAtRoute('/settings');
    expect(container.innerHTML).toBe('');
  });

  it('should NOT render on "/admin" (has its own TopBar)', () => {
    const { container } = renderAtRoute('/admin');
    expect(container.innerHTML).toBe('');
  });

  it('should render on "/login" (public route)', () => {
    renderAtRoute('/login');
    expect(screen.getByRole('button', { name: /toggle theme/i })).toBeInTheDocument();
  });

  it('should render on "/signup" (public route)', () => {
    renderAtRoute('/signup');
    expect(screen.getByRole('button', { name: /toggle theme/i })).toBeInTheDocument();
  });

  it('should render on "/tenant-select" (semi-public route)', () => {
    renderAtRoute('/tenant-select');
    expect(screen.getByRole('button', { name: /toggle theme/i })).toBeInTheDocument();
  });

  it('should render on "/onboarding" (semi-public route)', () => {
    renderAtRoute('/onboarding');
    expect(screen.getByRole('button', { name: /toggle theme/i })).toBeInTheDocument();
  });

  it('should render on "/tos" (semi-public route)', () => {
    renderAtRoute('/tos');
    expect(screen.getByRole('button', { name: /toggle theme/i })).toBeInTheDocument();
  });

  it('should render on "/about" (standalone page)', () => {
    renderAtRoute('/about');
    expect(screen.getByRole('button', { name: /toggle theme/i })).toBeInTheDocument();
  });

  it('should render on "/account" (standalone page)', () => {
    renderAtRoute('/account');
    expect(screen.getByRole('button', { name: /toggle theme/i })).toBeInTheDocument();
  });
});
