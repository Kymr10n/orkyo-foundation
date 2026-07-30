import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { DynamicFieldsForm, hasRequiredValues, toInputValue } from './DynamicFieldsForm';
import type { ResourceTypeFieldInfo } from '@foundation/src/lib/api/resource-types-api';

function field(overrides: Partial<ResourceTypeFieldInfo> = {}): ResourceTypeFieldInfo {
  return {
    id: overrides.key ?? 'field-id',
    resourceTypeId: 'type-id',
    key: 'mileage',
    label: 'Mileage',
    dataType: 'number',
    isRequired: false,
    sortOrder: 0,
    isActive: true,
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
    ...overrides,
  };
}

describe('DynamicFieldsForm', () => {
  it('renders nothing when the type has no custom fields', () => {
    const { container } = render(
      <DynamicFieldsForm fields={[]} values={{}} onChange={vi.fn()} />,
    );
    expect(container).toBeEmptyDOMElement();
  });

  it('renders an input per field, labelled and pre-filled', () => {
    render(
      <DynamicFieldsForm
        fields={[
          field({ key: 'mileage', label: 'Mileage', dataType: 'number' }),
          field({ key: 'plate', label: 'Plate', dataType: 'text' }),
        ]}
        values={{ mileage: 4200, plate: 'AB-123' }}
        onChange={vi.fn()}
      />,
    );

    expect(screen.getByLabelText(/Mileage/)).toHaveValue(4200);
    expect(screen.getByLabelText(/Plate/)).toHaveValue('AB-123');
  });

  it('reports typed numbers as numbers, not strings', async () => {
    const onChange = vi.fn();
    render(
      <DynamicFieldsForm
        fields={[field({ key: 'mileage', dataType: 'number' })]}
        values={{}}
        onChange={onChange}
      />,
    );

    await userEvent.type(screen.getByLabelText(/Mileage/), '7');

    expect(onChange).toHaveBeenLastCalledWith({ mileage: 7 });
  });

  it('drops the key when a value is cleared', async () => {
    const onChange = vi.fn();
    render(
      <DynamicFieldsForm
        fields={[field({ key: 'plate', label: 'Plate', dataType: 'text' })]}
        values={{ plate: 'A' }}
        onChange={onChange}
      />,
    );

    await userEvent.clear(screen.getByLabelText(/Plate/));

    expect(onChange).toHaveBeenLastCalledWith({});
  });

  it('renders a choice list from the field options', () => {
    render(
      <DynamicFieldsForm
        fields={[
          field({
            key: 'fuel',
            label: 'Fuel',
            dataType: 'select',
            options: { values: ['petrol', 'diesel'] },
          }),
        ]}
        values={{ fuel: 'diesel' }}
        onChange={vi.fn()}
      />,
    );

    expect(screen.getByText('diesel')).toBeInTheDocument();
  });

  it('applies validation constraints as input hints', () => {
    render(
      <DynamicFieldsForm
        fields={[field({ key: 'mileage', dataType: 'number', validation: { min: 0, max: 500 } })]}
        values={{}}
        onChange={vi.fn()}
      />,
    );

    const input = screen.getByLabelText(/Mileage/);
    expect(input).toHaveAttribute('min', '0');
    expect(input).toHaveAttribute('max', '500');
  });
});

describe('hasRequiredValues', () => {
  it('is false while a required field is empty', () => {
    const fields = [field({ key: 'mileage', isRequired: true })];
    expect(hasRequiredValues(fields, {})).toBe(false);
    expect(hasRequiredValues(fields, { mileage: 10 })).toBe(true);
  });

  it('treats a blank string as missing', () => {
    const fields = [field({ key: 'plate', dataType: 'text', isRequired: true })];
    expect(hasRequiredValues(fields, { plate: '   ' })).toBe(false);
  });

  it('ignores optional fields', () => {
    expect(hasRequiredValues([field({ key: 'mileage' })], {})).toBe(true);
  });

  it('accepts false as a present value for a required boolean', () => {
    const fields = [field({ key: 'electric', dataType: 'boolean', isRequired: true })];
    expect(hasRequiredValues(fields, { electric: false })).toBe(true);
  });
});

describe('toInputValue', () => {
  it('renders primitives and blanks anything else', () => {
    expect(toInputValue('a')).toBe('a');
    expect(toInputValue(42)).toBe('42');
    expect(toInputValue(true)).toBe('true');
    expect(toInputValue(null)).toBe('');
    expect(toInputValue(undefined)).toBe('');
    expect(toInputValue({ nested: 1 })).toBe('');
  });
});
