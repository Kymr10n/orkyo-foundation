import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import { Tabs, TabsList, TabsTrigger, TabsContent } from './tabs';

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
  it('wraps the tablist in its own scroll container', () => {
    renderTabs();
    const scroller = screen.getByRole('tablist').parentElement as HTMLElement;
    expect(scroller.dataset.slot).toBe('tabs-list-scroller');
  });

  it('applies a caller className to the scroll wrapper, not the tablist', () => {
    renderTabs('mb-4');
    const list = screen.getByRole('tablist');
    const scroller = list.parentElement as HTMLElement;
    expect(scroller.className).toContain('mb-4');
    expect(list.className).not.toContain('mb-4');
  });
});

describe('TabsTrigger', () => {
  it('applies a caller className', () => {
    render(
      <Tabs defaultValue="a">
        <TabsList>
          <TabsTrigger value="a" className="relative">
            Alpha
          </TabsTrigger>
        </TabsList>
      </Tabs>,
    );
    expect(screen.getByRole('tab', { name: 'Alpha' }).className).toContain('relative');
  });

  it('shows the selected tab content', () => {
    renderTabs();
    expect(screen.getByRole('tab', { name: 'Alpha' })).toHaveAttribute('aria-selected', 'true');
    expect(screen.getByText('Alpha content')).toBeInTheDocument();
  });
});
