import { describe, expect, it, vi } from 'vitest';
import type * as CustomFieldsApi from '@foundation/src/lib/api/resource-custom-fields-api';
import { render, screen } from '@testing-library/react';
import { createTestQueryClient } from '@foundation/src/test-utils';
import { machineResourceType } from '@foundation/src/test-utils/resource-fixtures';
import type { ResourceInfo } from '@foundation/src/lib/api/resources-api';
import { ResourceEditDialog } from './ResourceEditDialog';

vi.mock('@foundation/src/lib/api/resources-api', () => ({ createResource: vi.fn(), updateResource: vi.fn() }));
vi.mock('@foundation/src/lib/api/resource-custom-fields-api', async (importOriginal) => ({
  ...(await importOriginal<typeof CustomFieldsApi>()),
  getResourceCustomFields: vi.fn().mockResolvedValue([]),
}));
vi.mock('@foundation/src/hooks/useSites', () => ({ useSites: () => ({ data: [] }), useIsMultiSite: () => false }));
// The section has its own suite; here only whether the dialog mounts it matters.
vi.mock('./ResourceScanCodesSection', () => ({
  ResourceScanCodesSection: ({ resourceId }: { resourceId: string }) => (
    <div data-testid="scan-codes" data-resource-id={resourceId} />
  ),
}));

const drill = { id: 'r-1', name: 'Drill', resourceTypeKey: 'machine', isActive: true } as ResourceInfo;

function renderDialog(scanCodesEnabled: boolean, resource: ResourceInfo | null) {
  const { wrapper: Wrapper } = createTestQueryClient({ feedback: true });
  return render(
    <Wrapper>
      <ResourceEditDialog
        resourceType={{ ...machineResourceType, scanCodesEnabled }}
        resource={resource}
        open
        onOpenChange={() => {}}
      />
    </Wrapper>,
  );
}

describe('ResourceEditDialog QR codes', () => {
  it('shows the QR codes of an existing resource when its type allows them', () => {
    renderDialog(true, drill);

    expect(screen.getByTestId('scan-codes')).toHaveAttribute('data-resource-id', 'r-1');
  });

  it('hides them when the type has QR codes off', () => {
    renderDialog(false, drill);

    expect(screen.queryByTestId('scan-codes')).not.toBeInTheDocument();
  });

  it('hides them while creating, before there is a resource to link to', () => {
    renderDialog(true, null);

    expect(screen.queryByTestId('scan-codes')).not.toBeInTheDocument();
  });
});
