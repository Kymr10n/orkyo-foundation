import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, waitFor } from '@testing-library/react';

const config = vi.hoisted(() => ({ turnstileSiteKey: '' }));
vi.mock('@foundation/src/config/runtime', () => ({ runtimeConfig: config }));

const { TurnstileWidget } = await import('./TurnstileWidget');

describe('TurnstileWidget', () => {
  beforeEach(() => {
    config.turnstileSiteKey = '';
  });

  afterEach(() => {
    delete window.turnstile;
  });

  it('renders nothing and loads no script without a site key', () => {
    const { container } = render(<TurnstileWidget onToken={vi.fn()} />);
    expect(container).toBeEmptyDOMElement();
    expect(document.querySelector('script[src*="turnstile"]')).toBeNull();
  });

  it('renders the challenge with the site key and forwards its token', async () => {
    config.turnstileSiteKey = 'site-key';
    let options: Record<string, unknown> = {};
    const remove = vi.fn();
    window.turnstile = {
      render: (_el, opts) => {
        options = opts;
        return 'widget-1';
      },
      reset: vi.fn(),
      remove,
    };
    const onToken = vi.fn();

    const { unmount } = render(<TurnstileWidget onToken={onToken} />);

    await waitFor(() => expect(options.sitekey).toBe('site-key'));
    (options.callback as (t: string) => void)('tok');
    expect(onToken).toHaveBeenCalledWith('tok');
    (options['expired-callback'] as () => void)();
    expect(onToken).toHaveBeenLastCalledWith(null);

    unmount();
    expect(remove).toHaveBeenCalledWith('widget-1');
  });
});
