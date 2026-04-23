import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { FloorplanUploadDialog } from './FloorplanUploadDialog';

vi.mock('@foundation/src/lib/api/floorplan-api', () => ({
  uploadFloorplan: vi.fn(),
}));

import { uploadFloorplan } from '@foundation/src/lib/api/floorplan-api';

const defaultProps = {
  siteId: 'site-1',
  open: true,
  onOpenChange: vi.fn(),
  onUploadComplete: vi.fn(),
};

function renderDialog(props: Partial<React.ComponentProps<typeof FloorplanUploadDialog>> = {}) {
  return render(<FloorplanUploadDialog {...defaultProps} {...props} />);
}

function createMockFile(name: string, type: string, size: number): File {
  const content = new Array(size).fill('a').join('');
  return new File([content], name, { type });
}

describe('FloorplanUploadDialog', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('renders dialog title and description', () => {
    renderDialog();
    expect(screen.getByText('Upload Floorplan Image')).toBeInTheDocument();
    expect(screen.getByText(/PNG, JPEG/)).toBeInTheDocument();
  });

  it('shows drop zone initially', () => {
    renderDialog();
    expect(screen.getByText(/Drop your floorplan here/)).toBeInTheDocument();
  });

  it('rejects invalid file types', () => {
    renderDialog();
    const input = document.getElementById('file-input') as HTMLInputElement;
    const invalidFile = createMockFile('plan.gif', 'image/gif', 1024);
    fireEvent.change(input, { target: { files: [invalidFile] } });
    expect(screen.getByText('Only PNG and JPEG images are allowed')).toBeInTheDocument();
  });

  it('rejects files over 10MB', () => {
    renderDialog();
    const input = document.getElementById('file-input') as HTMLInputElement;
    const largeFile = createMockFile('huge.png', 'image/png', 11 * 1024 * 1024);
    fireEvent.change(input, { target: { files: [largeFile] } });
    expect(screen.getByText('File size must be less than 10MB')).toBeInTheDocument();
  });

  it('accepts valid PNG file', () => {
    renderDialog();
    const input = document.getElementById('file-input') as HTMLInputElement;
    const validFile = createMockFile('floorplan.png', 'image/png', 5000);
    fireEvent.change(input, { target: { files: [validFile] } });
    expect(screen.getByText(/floorplan.png/)).toBeInTheDocument();
    expect(screen.getByText(/image\/png/)).toBeInTheDocument();
  });

  it('shows Upload button when file is selected', () => {
    renderDialog();
    const input = document.getElementById('file-input') as HTMLInputElement;
    fireEvent.change(input, { target: { files: [createMockFile('plan.png', 'image/png', 1024)] } });
    expect(screen.getByRole('button', { name: 'Upload' })).not.toBeDisabled();
  });

  it('Upload button is disabled without file', () => {
    renderDialog();
    expect(screen.getByRole('button', { name: 'Upload' })).toBeDisabled();
  });

  it('calls uploadFloorplan on upload', async () => {
    const metadata = { imagePath: '/uploads/plan.png', mimeType: 'image/png', fileSizeBytes: 1024, widthPx: 800, heightPx: 600, uploadedAt: new Date().toISOString() };
    vi.mocked(uploadFloorplan).mockResolvedValue(metadata);
    renderDialog();
    const input = document.getElementById('file-input') as HTMLInputElement;
    fireEvent.change(input, { target: { files: [createMockFile('plan.png', 'image/png', 1024)] } });
    fireEvent.click(screen.getByRole('button', { name: 'Upload' }));

    await waitFor(() => {
      expect(uploadFloorplan).toHaveBeenCalledWith('site-1', expect.any(File), expect.any(Function));
    });
  });

  it('calls onUploadComplete after successful upload', async () => {
    const metadata = { imagePath: '/uploads/plan.png', mimeType: 'image/png', fileSizeBytes: 1024, widthPx: 800, heightPx: 600, uploadedAt: new Date().toISOString() };
    vi.mocked(uploadFloorplan).mockResolvedValue(metadata);
    renderDialog();
    const input = document.getElementById('file-input') as HTMLInputElement;
    fireEvent.change(input, { target: { files: [createMockFile('plan.png', 'image/png', 1024)] } });
    fireEvent.click(screen.getByRole('button', { name: 'Upload' }));

    await waitFor(() => {
      expect(defaultProps.onUploadComplete).toHaveBeenCalledWith(metadata);
    });
  });

  it('shows error on upload failure', async () => {
    vi.mocked(uploadFloorplan).mockRejectedValue(new Error('Network error'));
    renderDialog();
    const input = document.getElementById('file-input') as HTMLInputElement;
    fireEvent.change(input, { target: { files: [createMockFile('plan.png', 'image/png', 1024)] } });
    fireEvent.click(screen.getByRole('button', { name: 'Upload' }));

    await waitFor(() => {
      expect(screen.getByText('Network error')).toBeInTheDocument();
    });
  });

  it('resets state on cancel', () => {
    renderDialog();
    const input = document.getElementById('file-input') as HTMLInputElement;
    fireEvent.change(input, { target: { files: [createMockFile('plan.png', 'image/png', 1024)] } });
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    expect(defaultProps.onOpenChange).toHaveBeenCalledWith(false);
  });
});
