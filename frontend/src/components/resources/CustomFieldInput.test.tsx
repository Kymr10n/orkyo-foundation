import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { CustomFieldInput, hasCustomFieldValue } from './CustomFieldInput';
import type { ResourceCustomField } from '@foundation/src/lib/api/resource-custom-fields-api';

function field(overrides: Partial<ResourceCustomField> = {}): ResourceCustomField {
  return {
    id: 'field-1',
    resourceTypeId: 'type-1',
    key: 'serial_number',
    label: 'Serial number',
    dataType: 'text',
    isRequired: false,
    sortOrder: 0,
    isActive: true,
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
    ...overrides,
  };
}

describe('CustomFieldInput', () => {
  it.each([
    ['text', 'text'],
    ['date', 'date'],
    ['url', 'url'],
    ['number', 'number'],
  ] as const)('renders a %s field as an %s input', (dataType, inputType) => {
    render(<CustomFieldInput field={field({ dataType })} value={null} onChange={() => {}} />);

    expect(screen.getByLabelText(/Serial number/)).toHaveAttribute('type', inputType);
  });

  it('renders a boolean field as a checkbox', async () => {
    const onChange = vi.fn();
    render(
      <CustomFieldInput field={field({ dataType: 'boolean' })} value={false} onChange={onChange} />,
    );

    await userEvent.click(screen.getByRole('checkbox', { name: 'Serial number' }));
    expect(onChange).toHaveBeenCalledWith(true);
  });

  it('reports an emptied number box as unfilled rather than zero', async () => {
    const onChange = vi.fn();
    render(
      <CustomFieldInput field={field({ dataType: 'number' })} value={7} onChange={onChange} />,
    );

    await userEvent.clear(screen.getByLabelText(/Serial number/));
    expect(onChange).toHaveBeenLastCalledWith(null);
  });

  it('marks a required field', () => {
    render(<CustomFieldInput field={field({ isRequired: true })} value={null} onChange={() => {}} />);

    expect(screen.getByText('*')).toBeInTheDocument();
  });
});

describe('hasCustomFieldValue', () => {
  it('treats null, undefined and whitespace as unfilled', () => {
    expect(hasCustomFieldValue(null)).toBe(false);
    expect(hasCustomFieldValue(undefined)).toBe(false);
    expect(hasCustomFieldValue('   ')).toBe(false);
  });

  it('treats zero and false as filled in — they are answers', () => {
    expect(hasCustomFieldValue(0)).toBe(true);
    expect(hasCustomFieldValue(false)).toBe(true);
  });
});
