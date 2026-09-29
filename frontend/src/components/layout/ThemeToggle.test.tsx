/** @jsxImportSource react */
import { describe, it, expect, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { ThemeToggle } from './ThemeToggle';
import { useLayoutStore } from '@foundation/src/store/layout-store';

const initialLayoutState = useLayoutStore.getState();
const theme = () => useLayoutStore.getState().theme;

describe('ThemeToggle', () => {
  beforeEach(() => {
    useLayoutStore.setState({ ...initialLayoutState, theme: 'system', resolvedTheme: 'dark' }, true);
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

    expect(theme()).toBe('light');
    expect(useLayoutStore.getState().resolvedTheme).toBe('light');
  });

  it('should call setTheme("dark") when Dark is selected', async () => {
    const user = userEvent.setup();
    render(<ThemeToggle />);

    await user.click(screen.getByRole('button', { name: /toggle theme/i }));
    await user.click(screen.getByText('Dark'));

    expect(theme()).toBe('dark');
  });

  it('should call setTheme("system") when System is selected', async () => {
    useLayoutStore.setState({ theme: 'light', resolvedTheme: 'light' });
    const user = userEvent.setup();
    render(<ThemeToggle />);

    await user.click(screen.getByRole('button', { name: /toggle theme/i }));
    await user.click(screen.getByText('System'));

    expect(theme()).toBe('system');
  });

  it('should show Sun icon when resolved theme is dark', () => {
    useLayoutStore.setState({ resolvedTheme: 'dark' });
    render(<ThemeToggle />);
    // Sun icon is shown when dark (to indicate "switch to light")
    const button = screen.getByRole('button', { name: /toggle theme/i });
    expect(button.querySelector('.lucide-sun')).not.toBeNull();
    expect(button.querySelector('.lucide-moon')).toBeNull();
  });

  it('should show Moon icon when resolved theme is light', () => {
    useLayoutStore.setState({ resolvedTheme: 'light' });
    render(<ThemeToggle />);
    const button = screen.getByRole('button', { name: /toggle theme/i });
    expect(button.querySelector('.lucide-moon')).not.toBeNull();
    expect(button.querySelector('.lucide-sun')).toBeNull();
  });

  it('should highlight the currently active theme option', async () => {
    useLayoutStore.setState({ theme: 'dark' });
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

    expect(theme()).toBe('dark');
  });

  it('should apply floating-specific button styling', () => {
    const { container } = render(<ThemeToggle variant="floating" />);
    const button = container.querySelector('button');
    expect(button?.className).toContain('rounded-full');
    expect(button?.className).toContain('shadow-lg');
  });
});
