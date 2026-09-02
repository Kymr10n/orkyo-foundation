import { describe, it, expect } from 'vitest';
import { cva } from './variants';

/**
 * These pin the cva behaviours the four call sites rely on. They were written against
 * class-variance-authority's semantics before it was removed, so a divergence shows up
 * here rather than as a silently wrong class in the UI.
 */
describe('cva', () => {
  const button = cva('base', {
    variants: {
      variant: { default: 'bg-primary', outline: 'border' },
      size: { sm: 'h-8', lg: 'h-10' },
    },
    defaultVariants: { variant: 'default', size: 'sm' },
  });

  it('applies the defaults when called with no arguments', () => {
    // alert-dialog.tsx calls buttonVariants() bare and expects the default look.
    expect(button()).toBe('base bg-primary h-8');
  });

  it('lets an explicit selection override a default', () => {
    expect(button({ variant: 'outline' })).toBe('base border h-8');
    expect(button({ variant: 'outline', size: 'lg' })).toBe('base border h-10');
  });

  it('falls back to the default when a variant is undefined', () => {
    // Components forward optional props straight through, so undefined is the common case.
    expect(button({ variant: undefined })).toBe('base bg-primary h-8');
  });

  it('selects no class when a variant is explicitly null', () => {
    expect(button({ variant: null })).toBe('base h-8');
  });

  it('appends className last so it wins under twMerge', () => {
    // button.tsx passes className INTO the builder rather than alongside it.
    expect(button({ className: 'w-full' })).toBe('base bg-primary h-8 w-full');
  });

  it('ignores an unknown variant value rather than emitting undefined', () => {
    expect(button({ size: 'xl' as unknown as 'sm' })).toBe('base bg-primary');
  });

  it('supports a base-only builder with no config', () => {
    expect(cva('only-base')()).toBe('only-base');
  });
});
