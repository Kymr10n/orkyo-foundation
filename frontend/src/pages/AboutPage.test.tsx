import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { BrowserRouter } from 'react-router-dom';
import { AboutPage } from './AboutPage';
import { formatBuildTime } from '@foundation/src/lib/utils/formatBuildTime';

// Mock navigate
const mockNavigate = vi.fn();
vi.mock('react-router-dom', async () => {
  const actual = await vi.importActual('react-router-dom');
  return {
    ...actual,
    useNavigate: () => mockNavigate,
  };
});

// Support email is derived from environment at runtime; mock it so the page's
// dynamic mailto can be asserted (and toggled off to verify the no-domain fallback).
const { configMock } = vi.hoisted(() => ({ configMock: { supportEmail: 'support@example.test' } }));
vi.mock('@foundation/src/config/runtime', () => ({ runtimeConfig: configMock }));

const renderAboutPage = () => {
  return render(
    <BrowserRouter>
      <AboutPage />
    </BrowserRouter>
  );
};

describe('AboutPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    configMock.supportEmail = 'support@example.test';
  });

  it('renders the page title', () => {
    renderAboutPage();
    expect(screen.getByText('Orkyo')).toBeInTheDocument();
  });

  it('displays the app description', () => {
    renderAboutPage();
    expect(screen.getByText('Space and resource utilization management')).toBeInTheDocument();
  });

  it('does not show the commit label or hash', () => {
    renderAboutPage();
    expect(screen.queryByText('Commit')).not.toBeInTheDocument();
  });

  it('shows version or fallback dash (no Deployed label)', () => {
    renderAboutPage();
    // "Deployed" label removed — only a version string or "—" fallback is shown.
    expect(screen.queryByText('Deployed')).not.toBeInTheDocument();
    // __APP_VERSION__ is undefined in tests → fallback "—" is rendered.
    expect(screen.getByText('—')).toBeInTheDocument();
  });

  it('displays current features list', () => {
    renderAboutPage();
    expect(screen.getByText(/Spaces —/)).toBeInTheDocument();
    expect(screen.getByText(/Utilization —/)).toBeInTheDocument();
    expect(screen.getByText(/Requests —/)).toBeInTheDocument();
    expect(screen.getByText(/Conflict detection —/)).toBeInTheDocument();
    expect(screen.getByText(/People —/)).toBeInTheDocument();
    expect(screen.getByText(/Reporting —/)).toBeInTheDocument();
  });

  it('shows support link', () => {
    renderAboutPage();
    expect(screen.getByText('Contact Support')).toBeInTheDocument();
  });

  it('navigates back when back button is clicked', () => {
    renderAboutPage();
    const backButton = screen.getByRole('button', { name: /back/i });
    fireEvent.click(backButton);
    expect(mockNavigate).toHaveBeenCalledWith(-1);
  });

  it('opens the env-derived support email link', () => {
    const windowOpenSpy = vi.spyOn(window, 'open').mockImplementation(() => null);
    renderAboutPage();

    const supportButton = screen.getByText('Contact Support');
    fireEvent.click(supportButton);

    expect(windowOpenSpy).toHaveBeenCalledWith(
      'mailto:support@example.test',
      '_blank'
    );
    windowOpenSpy.mockRestore();
  });

  it('hides the support link when no support email is configured', () => {
    configMock.supportEmail = '';
    renderAboutPage();
    expect(screen.queryByText('Contact Support')).not.toBeInTheDocument();
  });

  it('displays copyright with current year', () => {
    renderAboutPage();
    const currentYear = new Date().getFullYear();
    expect(screen.getByText(new RegExp(`© ${currentYear}`))).toBeInTheDocument();
  });

  it('shows privacy note in footer', () => {
    renderAboutPage();
    expect(screen.getByText(/not shared with third parties/)).toBeInTheDocument();
  });
});

describe('formatBuildTime', () => {
  it('formats a UTC ISO string into a readable locale string', () => {
    const iso = '2026-03-08T14:30:00.000Z';
    const result = formatBuildTime(iso);
    // Should include the year and not be empty
    expect(result).toMatch(/2026/);
    expect(result.length).toBeGreaterThan(0);
  });

  it('includes hour and minute components', () => {
    const iso = '2026-03-08T09:05:00.000Z';
    const result = formatBuildTime(iso);
    // toLocaleString with hour/minute options always produces a colon-separated time
    expect(result).toMatch(/\d+:\d+/);
  });

  it('returns a different string for a different timestamp', () => {
    const a = formatBuildTime('2026-01-01T00:00:00.000Z');
    const b = formatBuildTime('2026-06-15T12:00:00.000Z');
    expect(a).not.toBe(b);
  });
});
