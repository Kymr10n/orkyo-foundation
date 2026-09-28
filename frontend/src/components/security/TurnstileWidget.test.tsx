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

  it('loads Cloudflare\'s script when it is not on the page yet', async () => {
    config.turnstileSiteKey = 'site-key';
    const render_ = vi.fn(() => 'widget-2');
    // Captured rather than appended: jsdom refuses to fetch a remote script.
    const appended: Node[] = [];
    const append = vi.spyOn(document.head, 'appendChild').mockImplementation((node) => {
      appended.push(node);
      return node;
    });

    render(<TurnstileWidget onToken={vi.fn()} />);

    expect((appended[0] as HTMLScriptElement).src).toContain('challenges.cloudflare.com/turnstile/v0/api.js');
    window.turnstile = { render: render_, reset: vi.fn(), remove: vi.fn() };
    window.__orkyoTurnstileOnload?.();

    await waitFor(() => expect(render_).toHaveBeenCalled());
    append.mockRestore();
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
