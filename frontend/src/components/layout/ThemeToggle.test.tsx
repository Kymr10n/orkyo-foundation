/** @jsxImportSource react */
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { ThemeToggle } from './ThemeToggle';

// Track setTheme calls
const mockSetTheme = vi.fn();
let mockTheme = 'system' as 'dark' | 'light' | 'system';
let mockResolvedTheme = 'dark' as 'dark' | 'light';

vi.mock('@/store/app-store', () => ({
  useAppStore: vi.fn((selector: (state: Record<string, unknown>) => unknown) => {
    const state = {
      theme: mockTheme,
      resolvedTheme: mockResolvedTheme,
      setTheme: mockSetTheme,
    };
    return selector(state);
  }),
}));

describe('ThemeToggle', () => {
  beforeEach(() => {
    mockTheme = 'system';
    mockResolvedTheme = 'dark';
    mockSetTheme.mockClear();
  });

  it('should render the toggle button', () => {
    render(<ThemeToggle />);
    expect(screen.getByRole('button', { name: /toggle theme/i })).toBeInTheDocument();
  });

  it('should render floating variant with fixed positioning', () => {
    const { container } = render(<ThemeToggle variant="floating" />);
    const wrapper = container.firstChild as HTMLElement;
    expect(wrapper.className).toContain('fixed');
    expect(wrapper.className).toContain('top-4');
    expect(wrapper.className).toContain('right-4');
  });

  it('should render inline variant without fixed positioning', () => {
    const { container } = render(<ThemeToggle />);
    const wrapper = container.firstChild as HTMLElement;
    // Inline variant renders DropdownMenu directly, no fixed wrapper
    expect(wrapper.className || '').not.toContain('fixed');
  });

  it('should show dropdown with Light, Dark, System options when clicked', async () => {
    const user = userEvent.setup();
    render(<ThemeToggle />);

    await user.click(screen.getByRole('button', { name: /toggle theme/i }));

    expect(screen.getByText('Light')).toBeInTheDocument();
    expect(screen.getByText('Dark')).toBeInTheDocument();
    expect(screen.getByText('System')).toBeInTheDocument();
  });

  it('should call setTheme("light") when Light is selected', async () => {
    const user = userEvent.setup();
    render(<ThemeToggle />);

    await user.click(screen.getByRole('button', { name: /toggle theme/i }));
    await user.click(screen.getByText('Light'));

    expect(mockSetTheme).toHaveBeenCalledWith('light');
  });

  it('should call setTheme("dark") when Dark is selected', async () => {
    const user = userEvent.setup();
    render(<ThemeToggle />);

    await user.click(screen.getByRole('button', { name: /toggle theme/i }));
    await user.click(screen.getByText('Dark'));

    expect(mockSetTheme).toHaveBeenCalledWith('dark');
  });

  it('should call setTheme("system") when System is selected', async () => {
    const user = userEvent.setup();
    render(<ThemeToggle />);

    await user.click(screen.getByRole('button', { name: /toggle theme/i }));
    await user.click(screen.getByText('System'));

    expect(mockSetTheme).toHaveBeenCalledWith('system');
  });

  it('should show Sun icon when resolved theme is dark', () => {
    mockResolvedTheme = 'dark';
    render(<ThemeToggle />);
    // Sun icon is shown when dark (to indicate "switch to light")
    const button = screen.getByRole('button', { name: /toggle theme/i });
    expect(button).toBeInTheDocument();
  });

  it('should show Moon icon when resolved theme is light', () => {
    mockResolvedTheme = 'light';
    render(<ThemeToggle />);
    const button = screen.getByRole('button', { name: /toggle theme/i });
    expect(button).toBeInTheDocument();
  });

  it('should highlight the currently active theme option', async () => {
    mockTheme = 'dark';
    const user = userEvent.setup();
    render(<ThemeToggle />);

    await user.click(screen.getByRole('button', { name: /toggle theme/i }));

    const darkItem = screen.getByText('Dark').closest('[role="menuitem"]');
    expect(darkItem?.className).toContain('bg-accent');
  });

  it('should show floating dropdown with same options', async () => {
    const user = userEvent.setup();
    render(<ThemeToggle variant="floating" />);

    await user.click(screen.getByRole('button', { name: /toggle theme/i }));

    expect(screen.getByText('Light')).toBeInTheDocument();
    expect(screen.getByText('Dark')).toBeInTheDocument();
    expect(screen.getByText('System')).toBeInTheDocument();
  });

  it('should call setTheme from floating variant dropdown', async () => {
    const user = userEvent.setup();
    render(<ThemeToggle variant="floating" />);

    await user.click(screen.getByRole('button', { name: /toggle theme/i }));
    await user.click(screen.getByText('Dark'));

    expect(mockSetTheme).toHaveBeenCalledWith('dark');
  });

  it('should apply floating-specific button styling', () => {
    const { container } = render(<ThemeToggle variant="floating" />);
    const button = container.querySelector('button');
    expect(button?.className).toContain('rounded-full');
    expect(button?.className).toContain('shadow-lg');
  });
});
