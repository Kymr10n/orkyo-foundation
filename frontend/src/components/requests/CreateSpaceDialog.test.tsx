import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { CreateSpaceDialog } from './CreateSpaceDialog';
import { createTestQueryWrapper } from '@foundation/src/test-utils';

// The chosen type's custom fields — none by default, so submit stays enabled. The required-field
// gate has its own case below.
const mockGetResourceCustomFields = vi.fn();
vi.mock('@foundation/src/lib/api/resource-custom-fields-api', async (importOriginal) => {
  const actual = await importOriginal<Record<string, unknown>>();
  return {
    ...actual,
    getResourceCustomFields: (...args: unknown[]) => mockGetResourceCustomFields(...args),
  };
});
import type { ResourceGeometry } from '@foundation/src/types/geometry';


const mockGeometry: ResourceGeometry = {
  type: 'rectangle',
  coordinates: [
    { x: 100, y: 100 },
    { x: 300, y: 100 },
    { x: 300, y: 250 },
    { x: 100, y: 250 },
  ],
};

const defaultProps = {
  open: true,
  onOpenChange: vi.fn(),
  geometry: mockGeometry,
  onSubmit: vi.fn().mockResolvedValue(undefined),
  siteId: 'site-1',
  // The toolbar decides the type before the shape is drawn; the dialog is only told.
  resourceTypeKey: 'space',
  resourceTypeId: 'type-space',
  resourceTypeLabel: 'Space',
};

function renderDialog(props: Partial<React.ComponentProps<typeof CreateSpaceDialog>> = {}) {
  // The dialog asks for the chosen type's custom fields, so it needs a query client in scope.
  return render(<CreateSpaceDialog {...defaultProps} {...props} />, {
    wrapper: createTestQueryWrapper(),
  });
}

describe('CreateSpaceDialog', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockGetResourceCustomFields.mockResolvedValue([]);
  });

  it('renders dialog title and description', () => {
    renderDialog();
    expect(screen.getByText('Create New Space')).toBeInTheDocument();
    expect(screen.getByText(/area you've drawn/)).toBeInTheDocument();
  });

  it('shows geometry info', () => {
    renderDialog();
    expect(screen.getByText(/rectangle with 4 points/)).toBeInTheDocument();
  });

  it('renders name, code, and description fields', () => {
    renderDialog();
    expect(screen.getByLabelText(/Name/)).toBeInTheDocument();
    expect(screen.getByLabelText('Code')).toBeInTheDocument();
    expect(screen.getByLabelText('Description')).toBeInTheDocument();
  });

  it('shows validation error when submitting with whitespace-only name', async () => {
    renderDialog();
    fireEvent.change(screen.getByPlaceholderText(/Assembly Zone/), {
      target: { value: '   ' },
    });
    await waitFor(() => expect(screen.getByRole('button', { name: 'Create Space' })).toBeEnabled());
    fireEvent.click(screen.getByRole('button', { name: 'Create Space' }));
    expect(screen.getByText('Name is required')).toBeInTheDocument();
  });

  it('calls onSubmit with form data', async () => {
    renderDialog();
    fireEvent.change(screen.getByPlaceholderText(/Assembly Zone/), {
      target: { value: 'Zone A' },
    });
    fireEvent.change(screen.getByPlaceholderText(/A-01/), {
      target: { value: 'ZA' },
    });
    fireEvent.change(screen.getByPlaceholderText(/Optional description/), {
      target: { value: 'My zone' },
    });
    await waitFor(() => expect(screen.getByRole('button', { name: 'Create Space' })).toBeEnabled());
    fireEvent.click(screen.getByRole('button', { name: 'Create Space' }));

    await waitFor(() => {
      // The dialog now supplies what the site-scoped space route used to hardcode server-side:
      // the type, exclusive allocation, the home site, and no travelling off it.
      expect(defaultProps.onSubmit).toHaveBeenCalledWith({
        resourceTypeKey: 'space',
        name: 'Zone A',
        code: 'ZA',
        description: 'My zone',
        allocationMode: 'Exclusive',
        homeSiteId: 'site-1',
        crossSiteAllowed: false,
        isPhysical: true,
        geometry: mockGeometry,
        customFields: {},
      });
    });
  });

  it('omits optional fields when empty', async () => {
    renderDialog();
    fireEvent.change(screen.getByPlaceholderText(/Assembly Zone/), {
      target: { value: 'Zone B' },
    });
    await waitFor(() => expect(screen.getByRole('button', { name: 'Create Space' })).toBeEnabled());
    fireEvent.click(screen.getByRole('button', { name: 'Create Space' }));

    await waitFor(() => {
      expect(defaultProps.onSubmit).toHaveBeenCalledWith(
        expect.objectContaining({
          name: 'Zone B',
          code: undefined,
          description: undefined,
        }),
      );
    });
  });

  it('shows error message when submission fails', async () => {
    const failSubmit = vi.fn().mockRejectedValue(new Error('Duplicate name'));
    renderDialog({ onSubmit: failSubmit });
    fireEvent.change(screen.getByPlaceholderText(/Assembly Zone/), {
      target: { value: 'Zone A' },
    });
    await waitFor(() => expect(screen.getByRole('button', { name: 'Create Space' })).toBeEnabled());
    fireEvent.click(screen.getByRole('button', { name: 'Create Space' }));

    await waitFor(() => {
      expect(screen.getByText('Duplicate name')).toBeInTheDocument();
    });
  });

  it('prompts to discard changes when cancelling a dirty form', async () => {
    renderDialog();
    fireEvent.change(screen.getByPlaceholderText(/Assembly Zone/), {
      target: { value: 'Zone A' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));

    // The dirty-guard confirm dialog appears; the dialog is not yet closed.
    expect(defaultProps.onOpenChange).not.toHaveBeenCalled();
    const discardBtn = await screen.findByRole('button', { name: /Discard changes/i });
    fireEvent.click(discardBtn);
    expect(defaultProps.onOpenChange).toHaveBeenCalledWith(false);
  });

  it('closes immediately on cancel when form is clean', () => {
    renderDialog();
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    expect(defaultProps.onOpenChange).toHaveBeenCalledWith(false);
  });

  it('closes dialog after successful submission', async () => {
    renderDialog();
    fireEvent.change(screen.getByPlaceholderText(/Assembly Zone/), {
      target: { value: 'Zone A' },
    });
    await waitFor(() => expect(screen.getByRole('button', { name: 'Create Space' })).toBeEnabled());
    fireEvent.click(screen.getByRole('button', { name: 'Create Space' }));

    await waitFor(() => {
      expect(defaultProps.onOpenChange).toHaveBeenCalledWith(false);
    });
  });

  it('shows the type it was told to create, as context rather than a choice', () => {
    renderDialog({ resourceTypeLabel: 'Booth' });

    expect(screen.getByText('Type: Booth')).toBeInTheDocument();
    // The choice belongs to the toolbar now — the shape already means something when it is drawn.
    expect(screen.queryByLabelText('Station type')).not.toBeInTheDocument();
  });

  it('submits the type it was given', async () => {
    renderDialog({ resourceTypeKey: 'booth' });
    fireEvent.change(screen.getByPlaceholderText(/Assembly Zone/), {
      target: { value: 'Booth 1' },
    });
    await waitFor(() => expect(screen.getByRole('button', { name: 'Create Space' })).toBeEnabled());
    fireEvent.click(screen.getByRole('button', { name: 'Create Space' }));

    await waitFor(() =>
      expect(defaultProps.onSubmit).toHaveBeenCalledWith(
        expect.objectContaining({ resourceTypeKey: 'booth' }),
      ),
    );
  });

  it('holds the create until required custom fields are answered', async () => {
    // The reason this dialog asks at all: it used to send no custom fields, which forced the API
    // to refuse required fields on the built-in placeable type — the only create path could never
    // satisfy them. With the form asking, that guard could be deleted.
    mockGetResourceCustomFields.mockResolvedValue([
      {
        id: 'f-1', resourceTypeId: 'type-space', key: 'fire_rating', label: 'Fire rating',
        dataType: 'text', isRequired: true, sortOrder: 0, isActive: true,
      },
    ]);
    renderDialog();
    fireEvent.change(screen.getByPlaceholderText(/Assembly Zone/), {
      target: { value: 'Zone A' },
    });

    await screen.findByLabelText(/Fire rating/);
    expect(screen.getByRole('button', { name: 'Create Space' })).toBeDisabled();

    fireEvent.change(screen.getByLabelText(/Fire rating/), { target: { value: 'F30' } });

    await waitFor(() => expect(screen.getByRole('button', { name: 'Create Space' })).toBeEnabled());
    fireEvent.click(screen.getByRole('button', { name: 'Create Space' }));

    await waitFor(() =>
      expect(defaultProps.onSubmit).toHaveBeenCalledWith(
        expect.objectContaining({ customFields: { fire_rating: 'F30' } }),
      ),
    );
  });
});
