import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { EditSpaceDialog } from './EditSpaceDialog';
import { createTestQueryWrapper } from '@/test-utils';
import type { Space } from '@/types/space';

const mockMutateAsync = vi.fn();

vi.mock('@/hooks/useSpaces', () => ({
  useUpdateSpace: vi.fn(() => ({
    mutateAsync: mockMutateAsync,
    isPending: false,
  })),
}));

const wrapper = createTestQueryWrapper();

function makeSpace(overrides: Partial<Space> = {}): Space {
  return {
    id: 'space-1',
    siteId: 'site-1',
    name: 'Room A',
    code: 'RM-A',
    description: 'A meeting room',
    isPhysical: true,
    geometry: undefined,
    properties: {},
    groupId: undefined,
    capacity: 2,
    createdAt: new Date().toISOString(),
    updatedAt: new Date().toISOString(),
    ...overrides,
  };
}

const space = makeSpace();

const defaultProps = {
  space,
  siteId: 'site-1',
  open: true,
  onOpenChange: vi.fn(),
  onSuccess: vi.fn(),
};

describe('EditSpaceDialog', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('renders dialog title', () => {
    render(<EditSpaceDialog {...defaultProps} />, { wrapper });
    expect(screen.getByText('Edit Space')).toBeInTheDocument();
  });

  it('shows code as read-only', () => {
    render(<EditSpaceDialog {...defaultProps} />, { wrapper });
    const codeInput = screen.getByLabelText('Code');
    expect(codeInput).toBeDisabled();
    expect(codeInput).toHaveValue('RM-A');
  });

  it('pre-fills name from space', () => {
    render(<EditSpaceDialog {...defaultProps} />, { wrapper });
    expect(screen.getByLabelText(/Name/)).toHaveValue('Room A');
  });

  it('pre-fills description from space', () => {
    render(<EditSpaceDialog {...defaultProps} />, { wrapper });
    expect(screen.getByLabelText('Description')).toHaveValue('A meeting room');
  });

  it('pre-fills capacity from space', () => {
    render(<EditSpaceDialog {...defaultProps} />, { wrapper });
    expect(screen.getByLabelText('Capacity')).toHaveValue(2);
  });

  it('shows error when name is empty', async () => {
    render(<EditSpaceDialog {...defaultProps} />, { wrapper });
    const nameInput = screen.getByLabelText(/Name/);
    fireEvent.change(nameInput, { target: { value: '' } });
    fireEvent.submit(nameInput.closest('form')!);
    await waitFor(() => {
      expect(screen.getByText('Name is required')).toBeInTheDocument();
    });
    expect(mockMutateAsync).not.toHaveBeenCalled();
  });

  it('submits form with updated values', async () => {
    mockMutateAsync.mockResolvedValue({});
    render(<EditSpaceDialog {...defaultProps} />, { wrapper });

    fireEvent.change(screen.getByLabelText(/Name/), { target: { value: 'Room B' } });
    fireEvent.change(screen.getByLabelText('Description'), { target: { value: 'Updated' } });
    fireEvent.change(screen.getByLabelText('Capacity'), { target: { value: '5' } });
    fireEvent.submit(screen.getByLabelText(/Name/).closest('form')!);

    await waitFor(() => {
      expect(mockMutateAsync).toHaveBeenCalledWith(
        expect.objectContaining({
          spaceId: 'space-1',
          data: expect.objectContaining({
            name: 'Room B',
            description: 'Updated',
            capacity: 5,
          }),
        }),
      );
    });
  });

  it('calls onSuccess and onOpenChange after successful submit', async () => {
    mockMutateAsync.mockResolvedValue({});
    render(<EditSpaceDialog {...defaultProps} />, { wrapper });
    fireEvent.submit(screen.getByLabelText(/Name/).closest('form')!);
    await waitFor(() => {
      expect(defaultProps.onSuccess).toHaveBeenCalled();
      expect(defaultProps.onOpenChange).toHaveBeenCalledWith(false);
    });
  });

  it('shows error on submission failure', async () => {
    mockMutateAsync.mockRejectedValue(new Error('Server error'));
    render(<EditSpaceDialog {...defaultProps} />, { wrapper });
    fireEvent.submit(screen.getByLabelText(/Name/).closest('form')!);
    await waitFor(() => {
      expect(screen.getByText('Server error')).toBeInTheDocument();
    });
  });

  it('closes dialog on cancel', () => {
    render(<EditSpaceDialog {...defaultProps} />, { wrapper });
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    expect(defaultProps.onOpenChange).toHaveBeenCalledWith(false);
  });

  it('shows help text for code and capacity', () => {
    render(<EditSpaceDialog {...defaultProps} />, { wrapper });
    expect(screen.getByText('Code cannot be changed after creation')).toBeInTheDocument();
    expect(screen.getByText(/concurrent allocations/)).toBeInTheDocument();
  });
});
