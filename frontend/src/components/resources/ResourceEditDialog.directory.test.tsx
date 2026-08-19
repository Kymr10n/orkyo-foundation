import { describe, it, expect, vi, beforeEach } from 'vitest';
import type * as CustomFieldsApi from '@foundation/src/lib/api/resource-custom-fields-api';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClientProvider } from '@tanstack/react-query';
import { ResourceEditDialog } from './ResourceEditDialog';
import type { ResourceInfo } from '@foundation/src/lib/api/resources-api';
import type { ResourceTypeInfo } from '@foundation/src/lib/api/resource-types-api';

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

// The directory block owns its own lookups and is covered by its own suite; stub it down to the
// two inputs this suite needs to drive.
vi.mock('./ResourceDirectoryFields', () => ({
  ResourceDirectoryFields: ({
    value,
    onChange,
  }: {
    value: { email: string };
    onChange: (p: { email: string }) => void;
  }) => (
    <input
      aria-label="Email"
      value={value.email}
      onChange={(e) => onChange({ email: e.target.value })}
    />
  ),
  flattenDepartments: () => [],
}));

import { createResource, updateResource } from '@foundation/src/lib/api/resources-api';
import { getResourceCustomFields } from '@foundation/src/lib/api/resource-custom-fields-api';
import { createFeedbackTestQueryClientWithSpy } from '@foundation/src/test-utils';

const baseType: ResourceTypeInfo = {
  id: 'type-machine',
  key: 'machine',
  displayName: 'Machine',
  displayNamePlural: 'Machines',
  hasGeometry: false,
  hasDirectoryProfile: false,
  singleGroupMembership: false,
  isSystem: false,
  isActive: true,
  createdAt: '2026-01-01T00:00:00Z',
  updatedAt: '2026-01-01T00:00:00Z',
};

const personType: ResourceTypeInfo = {
  ...baseType,
  id: 'type-person',
  key: 'person',
  displayName: 'Person',
  displayNamePlural: 'People',
  hasDirectoryProfile: true,
};

function renderDialog(resourceType: ResourceTypeInfo, resource: ResourceInfo | null = null) {
  const { queryClient } = createFeedbackTestQueryClientWithSpy();
  return render(
    <QueryClientProvider client={queryClient}>
      <ResourceEditDialog
        resourceType={resourceType}
        resource={resource}
        open
        onOpenChange={() => {}}
      />
    </QueryClientProvider>,
  );
}

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(getResourceCustomFields).mockResolvedValue([]);
  vi.mocked(createResource).mockResolvedValue({ id: 'new' } as ResourceInfo);
  vi.mocked(updateResource).mockResolvedValue({ id: 'r-1' } as ResourceInfo);
});

describe('ResourceEditDialog directory fields', () => {
  it('shows no directory block for a type without a directory profile', () => {
    renderDialog(baseType);

    expect(screen.queryByLabelText('Email')).not.toBeInTheDocument();
  });

  it('shows the directory block for a directory type', () => {
    renderDialog(personType);

    expect(screen.getByLabelText('Email')).toBeInTheDocument();
  });

  it('omits the directory fields from the payload for a non-directory type', async () => {
    renderDialog(baseType);

    await userEvent.type(screen.getByLabelText('Name'), 'Mill 1');
    await userEvent.click(screen.getByRole('button', { name: 'Save' }));

    // The backend rejects these fields for a type with no directory, so sending an empty string
    // would turn every save of an ordinary type into a 400.
    await waitFor(() => expect(createResource).toHaveBeenCalled());
    const payload = vi.mocked(createResource).mock.calls[0][0];
    expect(payload).not.toHaveProperty('email');
    expect(payload).not.toHaveProperty('notes');
  });

  it('sends the directory fields for a directory type', async () => {
    renderDialog(personType);

    await userEvent.type(screen.getByLabelText('Name'), 'Ada');
    await userEvent.type(screen.getByLabelText('Email'), 'ada@example.com');
    await userEvent.click(screen.getByRole('button', { name: 'Save' }));

    await waitFor(() => expect(createResource).toHaveBeenCalled());
    expect(vi.mocked(createResource).mock.calls[0][0]).toMatchObject({
      email: 'ada@example.com',
      notes: null,
    });
  });

  it('sends null rather than an empty string when a directory field is cleared', async () => {
    renderDialog(personType, {
      id: 'r-1',
      name: 'Ada',
      email: 'ada@example.com',
      resourceTypeKey: 'person',
    } as ResourceInfo);

    await userEvent.clear(screen.getByLabelText('Email'));
    await userEvent.click(screen.getByRole('button', { name: 'Save' }));

    await waitFor(() => expect(updateResource).toHaveBeenCalled());
    expect(vi.mocked(updateResource).mock.calls[0][1]).toMatchObject({ email: null });
  });

  it('blocks the save while the email is not valid', async () => {
    renderDialog(personType);

    await userEvent.type(screen.getByLabelText('Name'), 'Ada');
    await userEvent.type(screen.getByLabelText('Email'), 'not-an-email');

    expect(screen.getByRole('button', { name: 'Save' })).toBeDisabled();
    expect(screen.getByText('Email address is not valid.')).toBeInTheDocument();
  });

  it('does not judge the email of a non-directory type', async () => {
    // The field is not on screen, so a stale value can never block the save.
    renderDialog(baseType);

    await userEvent.type(screen.getByLabelText('Name'), 'Mill 1');
    expect(screen.getByRole('button', { name: 'Save' })).toBeEnabled();
  });
});
