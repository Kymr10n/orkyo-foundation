import { describe, it, expect } from 'vitest';
import { render } from '@testing-library/react';
import { axe, toHaveNoViolations } from 'jest-axe';
import { Button } from './button';
import { LoadingSpinner } from './LoadingSpinner';

expect.extend(toHaveNoViolations);

// Smoke-level accessibility checks for core primitives. This establishes the
// jest-axe harness; extend it as components gain a11y coverage.
describe('a11y smoke', () => {
  it('Button has no detectable a11y violations', async () => {
    const { container } = render(<Button>Save</Button>);
    expect(await axe(container)).toHaveNoViolations();
  });

  it('LoadingSpinner exposes an accessible status with no violations', async () => {
    const { container } = render(
      <LoadingSpinner fullScreen={false} message="Loading requests…" />,
    );
    expect(await axe(container)).toHaveNoViolations();
  });
});
