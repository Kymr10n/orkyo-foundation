import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import { ScrollableDialogBody } from './dialog';

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
});
