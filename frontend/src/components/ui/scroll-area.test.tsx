import { describe, it, expect } from 'vitest';
import { createRef } from 'react';
import { render } from '@testing-library/react';
import { ScrollArea } from './scroll-area';

describe('ScrollArea', () => {
  it('forwards viewportRef to the scrolling Viewport, not the Root', () => {
    const rootRef = createRef<HTMLDivElement>();
    const viewportRef = createRef<HTMLDivElement>();

    render(
      <ScrollArea ref={rootRef} viewportRef={viewportRef}>
        <div>content</div>
      </ScrollArea>,
    );

    // The viewport is the element @tanstack/react-virtual must scroll. Radix
    // marks it with this data attribute; the Root carries no such marker.
    expect(viewportRef.current).not.toBeNull();
    expect(viewportRef.current).toHaveAttribute(
      'data-radix-scroll-area-viewport',
    );

    // Regression guard for the original footgun: the two refs must be distinct
    // elements, and the Root (the default `ref`) must NOT be the viewport.
    expect(rootRef.current).not.toBeNull();
    expect(rootRef.current).not.toBe(viewportRef.current);
    expect(rootRef.current).not.toHaveAttribute(
      'data-radix-scroll-area-viewport',
    );
  });
});
