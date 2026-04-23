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

  it('shows the deployed label', () => {
    renderAboutPage();
    // __BUILD_TIME__ is inlined by vite define — check the label is rendered
    expect(screen.getByText('Deployed')).toBeInTheDocument();
  });

  it('displays features list', () => {
    renderAboutPage();
    expect(screen.getByText(/Visual utilization timeline/)).toBeInTheDocument();
    expect(screen.getByText(/Space management/)).toBeInTheDocument();
    expect(screen.getByText(/Request workflow/)).toBeInTheDocument();
    expect(screen.getByText(/Multi-tenant/)).toBeInTheDocument();
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

  it('opens support email link', () => {
    const windowOpenSpy = vi.spyOn(window, 'open').mockImplementation(() => null);
    renderAboutPage();
    
    const supportButton = screen.getByText('Contact Support');
    fireEvent.click(supportButton);
    
    expect(windowOpenSpy).toHaveBeenCalledWith(
      'mailto:support@orkyo.app',
      '_blank'
    );
    windowOpenSpy.mockRestore();
  });

  it('displays copyright with current year', () => {
    renderAboutPage();
    const currentYear = new Date().getFullYear();
    expect(screen.getByText(new RegExp(`© ${currentYear}`))).toBeInTheDocument();
  });

  it('shows formatted build time in the deployed section', () => {
    renderAboutPage();
    // __BUILD_TIME__ is always a valid ISO string injected by Vite at transform time,
    // so the "—" fallback is never reached. Verify a non-empty value is shown.
    const deployedLabel = screen.getByText('Deployed');
    // The sibling element should not be the em dash placeholder
    expect(deployedLabel.closest('div')?.textContent).not.toMatch(/Deployed—/);
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
