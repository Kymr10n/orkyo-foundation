import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { CreateSpaceDialog } from './CreateSpaceDialog';
import type { SpaceGeometry } from '@/types/space';

const mockGeometry: SpaceGeometry = {
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
      expect(defaultProps.onSubmit).toHaveBeenCalledWith({
        name: 'Zone A',
        code: 'ZA',
        description: 'My zone',
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

  it('resets form on cancel', () => {
    renderDialog();
    fireEvent.change(screen.getByPlaceholderText(/Assembly Zone/), {
      target: { value: 'Zone A' },
    });
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
});
