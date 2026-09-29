import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import { Legend } from './Legend';

describe('Legend', () => {
  it('renders one labelled swatch per item, with its hover text', () => {
    render(
      <Legend
        items={[
          { className: 'bg-green-500', label: 'Available' },
          { className: 'bg-red-500', label: 'Overbooked', title: 'Beyond capacity' },
        ]}
      />,
    );

    expect(screen.getByText('Available').querySelector('.bg-green-500')).not.toBeNull();
    expect(screen.getByText('Overbooked')).toHaveAttribute('title', 'Beyond capacity');
  });
});
