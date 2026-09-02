import { render, screen, fireEvent } from '@testing-library/react';
import { describe, it, expect, vi } from 'vitest';
import { Label } from './label';

/**
 * Label replaced @radix-ui/react-label. The package contributed exactly one behaviour over a
 * native element — suppressing the text selection a double-click produces — so these tests
 * cover that behaviour plus the native association it must not have lost.
 */
describe('Label', () => {
  it('associates with its control through htmlFor', () => {
    render(
      <>
        <Label htmlFor="site">Site</Label>
        <input id="site" />
      </>
    );

    // Native association: the accessible name comes from the label.
    expect(screen.getByLabelText('Site')).toBe(document.getElementById('site'));
  });

  it('suppresses the text selection a double-click would make', () => {
    render(<Label htmlFor="x">Site</Label>);

    // detail > 1 is the second click of a multi-click; without this the label's text
    // highlights when a user clicks it repeatedly to toggle a checkbox.
    const event = new MouseEvent('mousedown', { bubbles: true, cancelable: true, detail: 2 });
    fireEvent(screen.getByText('Site'), event);

    expect(event.defaultPrevented).toBe(true);
  });

  it('leaves a single click alone', () => {
    render(<Label htmlFor="x">Site</Label>);

    const event = new MouseEvent('mousedown', { bubbles: true, cancelable: true, detail: 1 });
    fireEvent(screen.getByText('Site'), event);

    expect(event.defaultPrevented).toBe(false);
  });

  it('does not interfere with a control nested inside it', () => {
    render(
      <Label>
        <input type="text" defaultValue="typed" />
      </Label>
    );

    // A double-click inside a text input selects a word — that belongs to the input,
    // not to the label, so the suppression must not reach it.
    const event = new MouseEvent('mousedown', { bubbles: true, cancelable: true, detail: 2 });
    fireEvent(screen.getByRole('textbox'), event);

    expect(event.defaultPrevented).toBe(false);
  });

  it("runs the caller's own onMouseDown", () => {
    const onMouseDown = vi.fn();
    render(<Label onMouseDown={onMouseDown}>Site</Label>);

    fireEvent.mouseDown(screen.getByText('Site'));

    expect(onMouseDown).toHaveBeenCalledTimes(1);
  });

  it('merges a caller className with its own', () => {
    render(<Label className="mt-2">Site</Label>);

    const label = screen.getByText('Site');
    expect(label.className).toContain('mt-2');
    expect(label.className).toContain('font-medium');
  });
});
