import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import { Tabs, TabsList, TabsTrigger, TabsContent } from './tabs';

/**
 * These assert the overflow contract from UI-GUIDELINES §1: a tab strip that is wider
 * than its container scrolls *itself*, and never clips a tab off the left edge where it
 * would be unreachable. jsdom does no layout, so we assert the classes that encode the
 * contract; `e2e/mobile.spec.ts` proves the resulting scroll behaviour in a real viewport.
 */
function renderTabs(listClassName?: string) {
  return render(
    <Tabs defaultValue="a">
      <TabsList className={listClassName}>
        <TabsTrigger value="a">Alpha</TabsTrigger>
        <TabsTrigger value="b">Beta</TabsTrigger>
      </TabsList>
      <TabsContent value="a">Alpha content</TabsContent>
    </Tabs>,
  );
}

describe('TabsList', () => {
  it('wraps the tablist in its own horizontal scroll container', () => {
    renderTabs();
    const scroller = screen.getByRole('tablist').parentElement as HTMLElement;
    expect(scroller.dataset.slot).toBe('tabs-list-scroller');
    expect(scroller.className).toContain('overflow-x-auto');
  });

  it('sizes the tablist to its content so a centred strip cannot clip the first tab', () => {
    renderTabs();
    const list = screen.getByRole('tablist');
    // w-max: once the tabs overflow, the list is exactly as wide as they are, so
    // justify-center has no free space to split across both edges.
    expect(list.className).toContain('w-max');
    // min-w-full: when they do fit, the bar still spans the full width and stays centred.
    expect(list.className).toContain('min-w-full');
    expect(list.className).toContain('justify-center');
  });

  it('applies a caller className to the scroll wrapper, not the tablist', () => {
    renderTabs('mb-4');
    const list = screen.getByRole('tablist');
    const scroller = list.parentElement as HTMLElement;
    expect(scroller.className).toContain('mb-4');
    expect(list.className).not.toContain('mb-4');
  });

  it('keeps the pill styling on the tablist itself', () => {
    renderTabs();
    const list = screen.getByRole('tablist');
    expect(list.className).toContain('bg-muted');
    expect(list.className).toContain('rounded-lg');
  });
});

describe('TabsTrigger', () => {
  it('never shrinks, so labels stay readable in an overflowing strip', () => {
    renderTabs();
    for (const name of ['Alpha', 'Beta']) {
      const tab = screen.getByRole('tab', { name });
      expect(tab.className).toContain('shrink-0');
      expect(tab.className).toContain('whitespace-nowrap');
    }
  });

  it('still merges a caller className', () => {
    render(
      <Tabs defaultValue="a">
        <TabsList>
          <TabsTrigger value="a" className="relative">
            Alpha
          </TabsTrigger>
        </TabsList>
      </Tabs>,
    );
    const tab = screen.getByRole('tab', { name: 'Alpha' });
    expect(tab.className).toContain('relative');
    expect(tab.className).toContain('shrink-0');
  });
});
