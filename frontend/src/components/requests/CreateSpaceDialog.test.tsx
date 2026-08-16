import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { CreateSpaceDialog } from './CreateSpaceDialog';
import type { ResourceGeometry } from '@foundation/src/types/geometry';

// The dialog reads the tenant's placeable types to decide whether to ask which one is being
// drawn. One type by default, so the picker stays hidden and the key is implied.
const mockResourceTypes = vi.fn(() => ({
  data: [{ id: 'type-space', key: 'space', displayName: 'Space', displayNamePlural: 'Spaces', hasGeometry: true, isActive: true }],
  isSuccess: true,
}));
vi.mock('@foundation/src/hooks/useResourceTypes', () => ({
  useResourceTypes: (...args: unknown[]) => mockResourceTypes(...(args as [])),
}));

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
};

function renderDialog(props: Partial<React.ComponentProps<typeof CreateSpaceDialog>> = {}) {
  return render(<CreateSpaceDialog {...defaultProps} {...props} />);
}

describe('CreateSpaceDialog', () => {
  beforeEach(() => {
    vi.clearAllMocks();
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
      });
    });
  });

  it('omits optional fields when empty', async () => {
    renderDialog();
    fireEvent.change(screen.getByPlaceholderText(/Assembly Zone/), {
      target: { value: 'Zone B' },
    });
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
    fireEvent.click(screen.getByRole('button', { name: 'Create Space' }));

    await waitFor(() => {
      expect(defaultProps.onOpenChange).toHaveBeenCalledWith(false);
    });
  });

  it('does not ask which type when only one thing can be placed', () => {
    // A tenant with only the built-in space type is never asked a question with one answer.
    renderDialog();
    expect(screen.queryByLabelText('Type')).not.toBeInTheDocument();
  });

  it('asks which type is being drawn when the tenant has more than one', async () => {
    mockResourceTypes.mockReturnValue({
      data: [
        { id: 'type-space', key: 'space', displayName: 'Space', displayNamePlural: 'Spaces', hasGeometry: true, isActive: true },
        { id: 'type-booth', key: 'booth', displayName: 'Booth', displayNamePlural: 'Booths', hasGeometry: true, isActive: true },
      ],
      isSuccess: true,
    });
    renderDialog();

    expect(screen.getByLabelText('Type')).toBeInTheDocument();
  });

  it('leaves non-placeable types out of the picker', () => {
    // A person cannot be drawn on a floorplan, so offering the type would create a resource the
    // backend rejects.
    mockResourceTypes.mockReturnValue({
      data: [
        { id: 'type-space', key: 'space', displayName: 'Space', displayNamePlural: 'Spaces', hasGeometry: true, isActive: true },
        { id: 'type-person', key: 'person', displayName: 'Person', displayNamePlural: 'People', hasGeometry: false, isActive: true },
      ],
      isSuccess: true,
    });
    renderDialog();

    // Only one placeable type remains, so there is still nothing to ask.
    expect(screen.queryByLabelText('Type')).not.toBeInTheDocument();
  });
});
