import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import { Dialog, DialogContent, DialogTitle, DIALOG_SIZE, ScrollableDialogBody } from './dialog';

describe('ScrollableDialogBody', () => {
  it('renders children', () => {
    render(<ScrollableDialogBody>body content</ScrollableDialogBody>);
    expect(screen.getByText('body content')).toBeInTheDocument();
  });

  it('is a bounded, scrolling region and merges a caller className', () => {
    render(
      <ScrollableDialogBody className="py-4" data-testid="body">
        x
      </ScrollableDialogBody>,
    );
    const el = screen.getByTestId('body');
    // The flex-1 / min-h-0 / overflow-y-auto trio is what makes the body scroll
    // instead of pushing the dialog past the viewport.
    expect(el).toHaveClass('flex-1', 'min-h-0', 'overflow-y-auto', 'py-4');
  });

  it('scrolls vertically only', () => {
    render(<ScrollableDialogBody data-testid="body">x</ScrollableDialogBody>);
    // CSS computes a `visible` axis to `auto` whenever the other axis is not
    // visible, so `overflow-y-auto` on its own would let one over-wide child turn
    // the form body into a sideways scroller and push the labels out of view.
    expect(screen.getByTestId('body')).toHaveClass('overflow-x-hidden');
  });
});

describe('DIALOG_SIZE', () => {
  it('prefixes every token with sm:', () => {
    // Below `sm` the phone gutter on DialogContent owns the width. An unprefixed token would
    // out-specify it — twMerge only collapses max-w-* against another max-w-* in the same
    // modifier group — and put the dialog back against both screen edges.
    for (const [size, token] of Object.entries(DIALOG_SIZE)) {
      expect(token, `DIALOG_SIZE.${size}`).toMatch(/^sm:max-w-/);
    }
  });
});

describe('DialogContent', () => {
  it('keeps a gutter on narrow screens and takes the form width above sm', () => {
    render(
      <Dialog open>
        <DialogContent>
          <DialogTitle>t</DialogTitle>
        </DialogContent>
      </Dialog>,
    );
    const content = screen.getByRole('dialog');
    // A card pinned to both screen edges reads as a rendering fault, not a choice.
    expect(content).toHaveClass('max-w-[calc(100%-2rem)]', 'sm:max-w-lg');
    // Rounded at every width now that the card never reaches the edges.
    expect(content).toHaveClass('rounded-lg');
  });
});
