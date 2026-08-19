import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import { AssetGridLegend } from './AssetGridLegend';

/** The swatch is the span nested inside the labelled legend item. */
function dot(label: string): HTMLElement {
  const item = screen.getByText(label).closest('span');
  return item!.firstElementChild as HTMLElement;
}

describe('AssetGridLegend', () => {
  it('names every state a row can be in', () => {
    render(<AssetGridLegend />);

    for (const label of ['Available', 'Booked', 'Assigned', 'Overbooked', 'Off']) {
      expect(screen.getByText(label)).toBeInTheDocument();
    }
  });

  it('draws Available from the emerald palette', () => {
    render(<AssetGridLegend />);

    expect(dot('Available').className).toMatch(/bg-emerald-100/);
    expect(dot('Available').className).toMatch(/dark:bg-emerald-950/);
  });

  it('draws Assigned from the blue palette', () => {
    render(<AssetGridLegend />);

    expect(dot('Assigned').className).toMatch(/bg-blue-100/);
  });

  it('hatches Overbooked, so the conflict state is not colour alone', () => {
    render(<AssetGridLegend />);

    // WCAG 1.4.1 — the grid rows carry the same hatch.
    expect(dot('Overbooked').className).toMatch(/repeating-linear-gradient/);
  });
});
