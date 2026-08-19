import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
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

describe('CustomFieldInput — list fields', () => {
  function renderList(resourceId: string | null) {
    const client = new QueryClient({
      defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
    });
    return render(
      <QueryClientProvider client={client}>
        <CustomFieldInput
          field={field({ dataType: 'list', label: 'Maintenance log', listDefinitionId: 'def-1' })}
          value={null}
          onChange={() => {}}
          resourceId={resourceId}
        />
      </QueryClientProvider>,
    );
  }

  it('says rows come later while the resource is still being created', () => {
    renderList(null);

    // Nothing to hang rows off yet, so the editor is not offered at all rather than offered and
    // failing on the first click.
    expect(screen.getByText(/Rows can be added once this has been created/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /add maintenance log/i })).not.toBeInTheDocument();
  });

  it('offers the row editor once the resource exists', async () => {
    renderList('resource-1');

    expect(screen.queryByText(/Rows can be added once/)).not.toBeInTheDocument();
    // The button is named after the field, not "Add row" — a per-resource list says what it holds.
    expect(await screen.findByRole('button', { name: /add maintenance log/i })).toBeInTheDocument();
  });

  it('renders the field label, and no required marker — a list cannot be required', () => {
    renderList('resource-1');

    expect(screen.getByText('Maintenance log')).toBeInTheDocument();
    expect(screen.queryByText('*')).not.toBeInTheDocument();
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
