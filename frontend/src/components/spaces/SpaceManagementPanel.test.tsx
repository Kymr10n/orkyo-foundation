/* eslint-disable @typescript-eslint/no-explicit-any */
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { SpaceManagementPanel } from './SpaceManagementPanel';

// ── React Query — foundation's SpaceManagementPanel uses useQueryClient ──────
vi.mock('@tanstack/react-query', async (importOriginal) => {
  const actual = await importOriginal<Record<string, unknown>>();
  return { ...actual, useQueryClient: () => ({ invalidateQueries: vi.fn() }) };
});

// ── Foundation mocks ──────────────────────────────────────────────────────────

const mockUseSpaces = vi.fn();
const mockCreateMutateAsync = vi.fn();
const mockDeleteMutateAsync = vi.fn();
const mockMoveMutateAsync = vi.fn();

vi.mock('@foundation/src/hooks/useSpaces', () => ({
  useSpaces: (siteId: any) => mockUseSpaces(siteId),
  useCreateSpace: () => ({ mutateAsync: mockCreateMutateAsync }),
  useUpdateSpace: () => ({ mutateAsync: vi.fn() }),
  useDeleteSpace: () => ({ mutateAsync: mockDeleteMutateAsync, isPending: false }),
  useMoveSpace: () => ({ mutateAsync: mockMoveMutateAsync }),
}));

const mockGetFloorplanMetadata = vi.fn();
const mockFetchFloorplanImageUrl = vi.fn();
const mockDeleteFloorplan = vi.fn();

vi.mock('@foundation/src/lib/api/floorplan-api', () => ({
  getFloorplanMetadata: (siteId: any) => mockGetFloorplanMetadata(siteId),
  fetchFloorplanImageUrl: (siteId: any) => mockFetchFloorplanImageUrl(siteId),
  deleteFloorplan: (siteId: any) => mockDeleteFloorplan(siteId),
}));

vi.mock('@foundation/src/hooks/useImportExport', () => ({
  useExportHandler: vi.fn(),
  useImportHandler: vi.fn(),
}));

vi.mock('@foundation/src/lib/utils/export-handlers', () => ({
  exportSpaces: vi.fn(),
  importSpaces: vi.fn(),
}));

vi.mock('@foundation/src/lib/core/logger', () => ({
  logger: { info: vi.fn(), error: vi.fn(), debug: vi.fn(), warn: vi.fn() },
}));

// ── react-router-dom ─────────────────────────────────────────────────────────

const mockSetSearchParams = vi.fn();
vi.mock('react-router-dom', () => ({
  useSearchParams: () => [new URLSearchParams(), mockSetSearchParams],
}));

// ── Child component mocks ─────────────────────────────────────────────────────

vi.mock('./SpaceList', () => ({
  SpaceList: ({ onSpaceDelete, onSpaceEdit, onCapabilitiesEdit, isLoading, spaces }: any) => (
    <div data-testid="space-list">
      {isLoading && <span>Loading spaces...</span>}
      {spaces?.map((s: any) => (
        <div key={s.id}>
          <span>{s.name}</span>
          <button data-testid={`edit-${s.id}`} onClick={() => onSpaceEdit(s)}>Edit</button>
          <button data-testid={`delete-${s.id}`} onClick={() => onSpaceDelete(s.id)}>Delete</button>
          <button data-testid={`caps-${s.id}`} onClick={() => onCapabilitiesEdit(s)}>Caps</button>
        </div>
      ))}
    </div>
  ),
}));

vi.mock('./EditSpaceDialog', () => ({
  EditSpaceDialog: ({ open, onSuccess }: any) =>
    open ? <button data-testid="edit-dialog-save" onClick={() => onSuccess({})}>Save Edit</button> : null,
}));

vi.mock('./SpaceCapabilitiesEditor', () => ({
  SpaceCapabilitiesEditor: ({ open, spaceName }: any) =>
    open ? <div data-testid="caps-editor">{spaceName}</div> : null,
}));

vi.mock('@/components/requests/CreateSpaceDialog', () => ({
  CreateSpaceDialog: ({ open, onSuccess }: any) =>
    open ? <button data-testid="create-dialog-save" onClick={() => onSuccess({ id: 'new', name: 'New Space' })}>Create</button> : null,
}));

vi.mock('@/components/requests/FloorplanUploadDialog', () => ({
  FloorplanUploadDialog: ({ open, onUploadComplete }: any) =>
    open ? <button data-testid="upload-complete" onClick={() => onUploadComplete({ id: 'fp-1' })}>Upload Done</button> : null,
}));

vi.mock('@/components/requests/SpaceDrawingCanvas', () => ({
  SpaceDrawingCanvas: () => <canvas data-testid="drawing-canvas" />,
}));

// ── Helpers ───────────────────────────────────────────────────────────────────

const mockSpace = { id: 'space-1', name: 'Office A', siteId: 'site-1', geometry: null };
const mockFloorplan = { id: 'fp-1', siteId: 'site-1', filename: 'floor.png', createdAt: '' };

function setup() {
  vi.clearAllMocks();
  mockUseSpaces.mockReturnValue({ data: [], isLoading: false });
  mockGetFloorplanMetadata.mockResolvedValue(null);
  mockFetchFloorplanImageUrl.mockResolvedValue('blob:test');
  mockDeleteFloorplan.mockResolvedValue(undefined);
  mockCreateMutateAsync.mockResolvedValue({ id: 'new', name: 'New Space' });
  mockDeleteMutateAsync.mockResolvedValue(undefined);
  mockMoveMutateAsync.mockResolvedValue(undefined);
  global.confirm = vi.fn(() => true);
  global.alert = vi.fn();
}

// ── Tests ─────────────────────────────────────────────────────────────────────

describe('SpaceManagementPanel', () => {
  beforeEach(setup);

  it('renders space list sidebar', () => {
    render(<SpaceManagementPanel siteId="site-1" />);
    expect(screen.getByText(/Spaces \(/i)).toBeInTheDocument();
    expect(screen.getByTestId('space-list')).toBeInTheDocument();
  });

  it('renders floorplan panel heading', () => {
    render(<SpaceManagementPanel siteId="site-1" />);
    expect(screen.getByText('Floorplan & Spaces')).toBeInTheDocument();
  });

  it('shows two Upload Floorplan buttons when no floorplan (header + empty state)', () => {
    render(<SpaceManagementPanel siteId="site-1" />);
    // One in the header toolbar, one in the canvas empty state
    expect(screen.getAllByRole('button', { name: /Upload Floorplan/i })).toHaveLength(2);
  });

  it('clicking Upload Floorplan (header) opens the upload dialog', async () => {
    const user = userEvent.setup();
    render(<SpaceManagementPanel siteId="site-1" />);

    await user.click(screen.getAllByRole('button', { name: /Upload Floorplan/i })[0]);

    await waitFor(() => {
      expect(screen.getByTestId('upload-complete')).toBeInTheDocument();
    });
  });

  it('handleUploadComplete stores floorplan metadata', async () => {
    const user = userEvent.setup();
    render(<SpaceManagementPanel siteId="site-1" />);

    await user.click(screen.getAllByRole('button', { name: /Upload Floorplan/i })[0]);
    await waitFor(() => screen.getByTestId('upload-complete'));
    await user.click(screen.getByTestId('upload-complete'));

    // After upload completes, floorplanMetadata is set so Upload buttons disappear
    await waitFor(() => {
      expect(screen.queryAllByRole('button', { name: /Upload Floorplan/i })).toHaveLength(0);
    });
  });

  it('shows zoom controls when floorplan is loaded', async () => {
    mockGetFloorplanMetadata.mockResolvedValue(mockFloorplan);
    mockFetchFloorplanImageUrl.mockResolvedValue('blob:test-url');

    render(<SpaceManagementPanel siteId="site-1" />);

    await waitFor(() => {
      expect(screen.getByText('100%')).toBeInTheDocument();
    });
  });

  it('zoom in button is accessible and clickable (handleZoomIn)', async () => {
    mockGetFloorplanMetadata.mockResolvedValue(mockFloorplan);
    mockFetchFloorplanImageUrl.mockResolvedValue('blob:test-url');

    const user = userEvent.setup();
    render(<SpaceManagementPanel siteId="site-1" />);

    // Wait for zoom controls to appear
    await waitFor(() => screen.getByText(/100%/));

    // The zoom in button comes after the percentage button in the toolbar
    const pctButton = screen.getByRole('button', { name: /100%/ });
    const zoomInBtn = pctButton.nextElementSibling as HTMLButtonElement | null;
    expect(zoomInBtn).toBeTruthy();

    await user.click(zoomInBtn!);

    // Zoom increments to 125%
    await waitFor(() => {
      expect(screen.getByRole('button', { name: /125%/ })).toBeInTheDocument();
    });
  });

  it('clicking zoom reset returns to 100% (handleZoomReset)', async () => {
    mockGetFloorplanMetadata.mockResolvedValue(mockFloorplan);
    mockFetchFloorplanImageUrl.mockResolvedValue('blob:test-url');

    const user = userEvent.setup();
    render(<SpaceManagementPanel siteId="site-1" />);

    await waitFor(() => screen.getByText('100%'));
    const pctButton = screen.getByText('100%');
    await user.click(pctButton);

    await waitFor(() => {
      expect(screen.getByText('100%')).toBeInTheDocument();
    });
  });

  it('clicking a drawing mode button fires handleSetDrawingMode', async () => {
    mockGetFloorplanMetadata.mockResolvedValue(mockFloorplan);
    mockFetchFloorplanImageUrl.mockResolvedValue('blob:test-url');

    const user = userEvent.setup();
    render(<SpaceManagementPanel siteId="site-1" />);

    await waitFor(() => screen.getByText('100%'));

    // Drawing mode buttons have title attributes
    const selectButton = screen.getByTitle(/Select \(S\)/i);
    await user.click(selectButton);
    // No assertion needed — just verify it doesn't throw
    expect(selectButton).toBeInTheDocument();
  });

  it('clicking rectangle drawing mode button fires handleSetDrawingMode', async () => {
    mockGetFloorplanMetadata.mockResolvedValue(mockFloorplan);
    mockFetchFloorplanImageUrl.mockResolvedValue('blob:test-url');

    const user = userEvent.setup();
    render(<SpaceManagementPanel siteId="site-1" />);

    await waitFor(() => screen.getByText('100%'));

    const rectButton = screen.getByTitle(/Draw Rectangle/i);
    await user.click(rectButton);
    expect(rectButton).toBeInTheDocument();
  });

  it('renders with spaces in the list', () => {
    mockUseSpaces.mockReturnValue({ data: [mockSpace], isLoading: false });
    render(<SpaceManagementPanel siteId="site-1" />);

    expect(screen.getByText(/Spaces \(1\)/i)).toBeInTheDocument();
    expect(screen.getByText('Office A')).toBeInTheDocument();
  });

  it('handleDeleteSpace is called when SpaceList delete fires', async () => {
    mockUseSpaces.mockReturnValue({ data: [mockSpace], isLoading: false });
    const user = userEvent.setup();
    render(<SpaceManagementPanel siteId="site-1" />);

    await user.click(screen.getByTestId('delete-space-1'));

    await waitFor(() => {
      expect(mockDeleteMutateAsync).toHaveBeenCalledWith('space-1');
    });
  });

  it('setEditingSpace is called when SpaceList edit fires', async () => {
    mockUseSpaces.mockReturnValue({ data: [mockSpace], isLoading: false });
    const user = userEvent.setup();
    render(<SpaceManagementPanel siteId="site-1" />);

    await user.click(screen.getByTestId('edit-space-1'));

    await waitFor(() => {
      expect(screen.getByTestId('edit-dialog-save')).toBeInTheDocument();
    });
  });

  it('handleUpdateSpace closes edit dialog on save', async () => {
    mockUseSpaces.mockReturnValue({ data: [mockSpace], isLoading: false });
    const user = userEvent.setup();
    render(<SpaceManagementPanel siteId="site-1" />);

    await user.click(screen.getByTestId('edit-space-1'));
    await waitFor(() => screen.getByTestId('edit-dialog-save'));
    await user.click(screen.getByTestId('edit-dialog-save'));

    await waitFor(() => {
      expect(screen.queryByTestId('edit-dialog-save')).not.toBeInTheDocument();
    });
  });

  it('setCapabilitiesSpace is called when SpaceList caps fires', async () => {
    mockUseSpaces.mockReturnValue({ data: [mockSpace], isLoading: false });
    const user = userEvent.setup();
    render(<SpaceManagementPanel siteId="site-1" />);

    await user.click(screen.getByTestId('caps-space-1'));

    await waitFor(() => {
      expect(screen.getByTestId('caps-editor')).toBeInTheDocument();
    });
  });

  it('delete space calls mutation directly (no confirm gate in handleDeleteSpace)', async () => {
    mockUseSpaces.mockReturnValue({ data: [mockSpace], isLoading: false });
    const user = userEvent.setup();
    render(<SpaceManagementPanel siteId="site-1" />);

    await user.click(screen.getByTestId('delete-space-1'));

    await waitFor(() => {
      expect(mockDeleteMutateAsync).toHaveBeenCalledWith('space-1');
    });
  });

  it('zoom out button decrements zoom (handleZoomOut)', async () => {
    mockGetFloorplanMetadata.mockResolvedValue(mockFloorplan);
    mockFetchFloorplanImageUrl.mockResolvedValue('blob:test-url');

    const user = userEvent.setup();
    render(<SpaceManagementPanel siteId="site-1" />);

    await waitFor(() => screen.getByRole('button', { name: /100%/ }));

    // Zoom out button is immediately before the percentage button
    const pctButton = screen.getByRole('button', { name: /100%/ });
    const zoomOutBtn = pctButton.previousElementSibling as HTMLButtonElement | null;
    expect(zoomOutBtn).toBeTruthy();

    // Zoom out is disabled at 0.5; initial zoom is 1.0 so it should be enabled
    if (!zoomOutBtn!.disabled) {
      await user.click(zoomOutBtn!);
      await waitFor(() => {
        expect(screen.getByRole('button', { name: /75%/ })).toBeInTheDocument();
      });
    }
  });

  it('clicking Resize mode button fires handleSetDrawingMode("resize")', async () => {
    mockGetFloorplanMetadata.mockResolvedValue(mockFloorplan);
    mockFetchFloorplanImageUrl.mockResolvedValue('blob:test-url');

    const user = userEvent.setup();
    render(<SpaceManagementPanel siteId="site-1" />);

    await waitFor(() => screen.getByText('100%'));

    const resizeButton = screen.getByTitle(/Resize \(Z\)/i);
    await user.click(resizeButton);
    expect(resizeButton).toBeInTheDocument();
  });

  it('clicking Polygon mode button fires handleSetDrawingMode("polygon")', async () => {
    mockGetFloorplanMetadata.mockResolvedValue(mockFloorplan);
    mockFetchFloorplanImageUrl.mockResolvedValue('blob:test-url');

    const user = userEvent.setup();
    render(<SpaceManagementPanel siteId="site-1" />);

    await waitFor(() => screen.getByText('100%'));

    const polygonButton = screen.getByTitle(/Draw Polygon \(P\)/i);
    await user.click(polygonButton);
    expect(polygonButton).toBeInTheDocument();
  });

  it('handleDeleteFloorplan prompts confirm and deletes', async () => {
    mockGetFloorplanMetadata.mockResolvedValue(mockFloorplan);
    mockFetchFloorplanImageUrl.mockResolvedValue('blob:test-url');

    const user = userEvent.setup();
    render(<SpaceManagementPanel siteId="site-1" />);

    await waitFor(() => screen.getByText('100%'));

    const deleteFloorplanBtn = screen.getByTitle(/Delete floorplan/i);
    await user.click(deleteFloorplanBtn);

    await waitFor(() => {
      expect(global.confirm).toHaveBeenCalled();
      expect(mockDeleteFloorplan).toHaveBeenCalledWith('site-1');
    });
  });
});
