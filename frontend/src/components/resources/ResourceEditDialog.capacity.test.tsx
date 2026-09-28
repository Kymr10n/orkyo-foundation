import { describe, it, expect, vi, beforeEach } from 'vitest';
import type * as CustomFieldsApi from '@foundation/src/lib/api/resource-custom-fields-api';
import { fireEvent, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { ResourceEditDialog } from './ResourceEditDialog';
import type { ResourceInfo } from '@foundation/src/lib/api/resources-api';
import type { ResourceTypeInfo } from '@foundation/src/lib/api/resource-types-api';
import { machineResourceType } from '@foundation/src/test-utils/resource-fixtures';
import { renderWithQuery } from '@foundation/src/test-utils';

vi.mock('@foundation/src/lib/api/resources-api', () => ({
  createResource: vi.fn(),
  updateResource: vi.fn(),
}));

vi.mock('@foundation/src/lib/api/resource-custom-fields-api', async (importOriginal) => ({
  ...(await importOriginal<typeof CustomFieldsApi>()),
  getResourceCustomFields: vi.fn(),
}));

vi.mock('@foundation/src/hooks/useSites', () => ({
  useSites: () => ({ data: [] }),
  useIsMultiSite: () => false,
}));

import { updateResource } from '@foundation/src/lib/api/resources-api';
import { getResourceCustomFields } from '@foundation/src/lib/api/resource-custom-fields-api';

const spaceType: ResourceTypeInfo = {
  ...machineResourceType,
  id: 'rt-space',
  key: 'space',
  displayName: 'Space',
  displayNamePlural: 'Spaces',
  hasGeometry: true,
};

const hotDesk = {
  id: 'space-1',
  name: 'Hot desks',
  capacity: 3,
  allocationMode: 'Exclusive',
  baseAvailabilityPercent: 100,
} as ResourceInfo;

function renderDialog(resourceType: ResourceTypeInfo, resource: ResourceInfo) {
  return renderWithQuery(
    <ResourceEditDialog resourceType={resourceType} resource={resource} open onOpenChange={() => {}} />,
    { feedback: true },
  );
}

beforeEach(() => {
  vi.mocked(getResourceCustomFields).mockResolvedValue([]);
  vi.mocked(updateResource).mockResolvedValue(hotDesk);
});

// Capacity moved here from the floorplan's own space dialog, which this dialog replaced.
describe('ResourceEditDialog capacity', () => {
  it('edits and sends the capacity of a placeable resource', async () => {
    renderDialog(spaceType, hotDesk);

    const input = screen.getByLabelText('Capacity');
    expect(input).toHaveValue(3);
    fireEvent.change(input, { target: { value: '5' } });
    await userEvent.click(screen.getByRole('button', { name: 'Save' }));

    await waitFor(() => expect(updateResource).toHaveBeenCalled());
    expect(vi.mocked(updateResource).mock.calls[0][1]).toMatchObject({ capacity: 5 });
  });

  it('holds the capacity at one or more', () => {
    renderDialog(spaceType, hotDesk);

    const input = screen.getByLabelText('Capacity');
    fireEvent.change(input, { target: { value: '0' } });
    expect(input).toHaveValue(1);
  });

  it('neither shows nor sends a capacity for a type without geometry', async () => {
    renderDialog(machineResourceType, { ...hotDesk, id: 'mill-1', name: 'Mill' });

    expect(screen.queryByLabelText('Capacity')).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Save' }));

    await waitFor(() => expect(updateResource).toHaveBeenCalled());
    expect(vi.mocked(updateResource).mock.calls[0][1]).not.toHaveProperty('capacity');
  });
});
