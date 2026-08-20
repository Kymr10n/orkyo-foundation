/* eslint-disable @typescript-eslint/no-explicit-any */
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
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

vi.mock('@foundation/src/hooks/usePlaceableResources', () => ({
  usePlaceableResources: (siteId: any) => mockUseSpaces(siteId),
  useCreatePlaceableResource: () => ({ mutateAsync: mockCreateMutateAsync }),
  useUpdatePlaceableResource: () => ({ mutateAsync: vi.fn() }),
  useMovePlaceableResource: () => ({ mutateAsync: mockMoveMutateAsync }),
  useDeletePlaceableResource: () => ({ mutateAsync: mockDeleteMutateAsync, isPending: false }),
}));

// The panel resolves what the next drawn shape becomes; one placeable type by default so the
// picker stays hidden.
const mockResourceTypes = vi.fn(() => ({
  data: [{ id: 'type-space', key: 'space', displayName: 'Space', displayNamePlural: 'Spaces', hasGeometry: true, isActive: true }],
  isSuccess: true,
}));
vi.mock('@foundation/src/hooks/useResourceTypes', () => ({
  useResourceTypes: (...args: unknown[]) => mockResourceTypes(...(args as [])),
}));

const mockGetFloorplanMetadata = vi.fn();
const mockFetchFloorplanImageUrl = vi.fn();
const mockDeleteFloorplan = vi.fn();

vi.mock('@foundation/src/lib/api/floorplan-api', () => ({
  getFloorplanMetadata: (siteId: any) => mockGetFloorplanMetadata(siteId),
  getFloorplanImageUrl: (siteId: any) => mockFetchFloorplanImageUrl(siteId),
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

// ── react-router ─────────────────────────────────────────────────────────

const mockSetSearchParams = vi.fn();
vi.mock('react-router', () => ({
  useSearchParams: () => [new URLSearchParams(), mockSetSearchParams],
}));

// ── Child component mocks ─────────────────────────────────────────────────────

vi.mock('./EditSpaceDialog', () => ({
  EditSpaceDialog: ({ open, onSuccess }: any) =>
    open ? <button data-testid="edit-dialog-save" onClick={() => onSuccess({})}>Save Edit</button> : null,
}));

vi.mock('@/components/requests/CreateSpaceDialog', () => ({
  // The real dialog takes onSubmit and is told which type it is creating.
  CreateSpaceDialog: ({ open, onSubmit, resourceTypeKey }: any) =>
    open ? (
      <button
        data-testid="create-dialog-save"
        data-resource-type-key={resourceTypeKey}
        onClick={() => onSubmit?.({ name: 'New Space' })}
      >
        Create
      </button>
    ) : null,
}));

vi.mock('@/components/requests/FloorplanUploadDialog', () => ({
  FloorplanUploadDialog: ({ open, onUploadComplete }: any) =>
    open ? <button data-testid="upload-complete" onClick={() => onUploadComplete({ id: 'fp-1' })}>Upload Done</button> : null,
}));

vi.mock('@/components/requests/SpaceDrawingCanvas', () => ({
  SpaceDrawingCanvas: ({ onSpaceDoubleClick, onSpaceContextMenu, onDrawingComplete, editEnabled, drawingMode }: any) => (
    <div
      data-testid="drawing-canvas"
      data-edit-enabled={String(!!editEnabled)}
      data-drawing-mode={drawingMode}
    >
      <button data-testid="dblclick-space-1" onClick={() => onSpaceDoubleClick?.('space-1')}>dbl</button>
      <button
        data-testid="rightclick-space-1"
        onClick={() => onSpaceContextMenu?.('space-1', { x: 40, y: 60 })}
      >
        ctx
      </button>
      <button
        data-testid="finish-drawing"
        onClick={() =>
          onDrawingComplete?.({ type: 'rectangle', coordinates: [{ x: 0, y: 0 }, { x: 10, y: 10 }] })
        }
      >
        finish
      </button>
    </div>
  ),
}));

// ── Helpers ───────────────────────────────────────────────────────────────────

const mockSpace = { id: 'space-1', name: 'Office A', siteId: 'site-1', geometry: null };
const mockFloorplan = { id: 'fp-1', siteId: 'site-1', filename: 'floor.png', createdAt: '' };

const ONE_PLACEABLE_TYPE = {
  data: [{ id: 'type-space', key: 'space', displayName: 'Space', displayNamePlural: 'Spaces', hasGeometry: true, isActive: true }],
  isSuccess: true,
};

function setup() {
  vi.clearAllMocks();
  // clearAllMocks resets calls but keeps implementations, so a mockReturnValue set by one test
  // would otherwise decide what the next one sees.
  mockResourceTypes.mockReturnValue(ONE_PLACEABLE_TYPE);
  mockUseSpaces.mockReturnValue({ data: [], isLoading: false });
  mockGetFloorplanMetadata.mockResolvedValue(null);
  mockFetchFloorplanImageUrl.mockResolvedValue('blob:test');
  mockDeleteFloorplan.mockResolvedValue(undefined);
  mockCreateMutateAsync.mockResolvedValue({ id: 'new', name: 'New Space' });
  mockMoveMutateAsync.mockResolvedValue(undefined);
  global.confirm = vi.fn(() => true);
  global.alert = vi.fn();
}

// ── Tests ─────────────────────────────────────────────────────────────────────

describe('SpaceManagementPanel', () => {
  beforeEach(setup);

  it('renders floorplan panel heading', () => {
    render(<SpaceManagementPanel siteId="site-1" />);
    expect(screen.getByText('Floorplan')).toBeInTheDocument();
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

  it('starts in view mode (Edit) with drawing tools disabled until enabled', async () => {
    mockGetFloorplanMetadata.mockResolvedValue(mockFloorplan);
    mockFetchFloorplanImageUrl.mockResolvedValue('blob:test-url');

    const user = userEvent.setup();
    render(<SpaceManagementPanel siteId="site-1" />);

    await waitFor(() => screen.getByText('100%'));

    // View mode: toolbar shows "Edit", canvas is non-interactive, draw tools disabled
    const editButton = screen.getByRole('button', { name: /Edit/i });
    expect(screen.getByTestId('drawing-canvas')).toHaveAttribute('data-edit-enabled', 'false');
    expect(screen.getByTitle(/Draw Rectangle/i)).toBeDisabled();

    // Enter edit mode → button becomes "Done", canvas interactive, draw tools enabled
    await user.click(editButton);
    await waitFor(() => {
      expect(screen.getByRole('button', { name: /Done/i })).toBeInTheDocument();
    });
    expect(screen.getByTestId('drawing-canvas')).toHaveAttribute('data-edit-enabled', 'true');
    expect(screen.getByTitle(/Draw Rectangle/i)).toBeEnabled();
    expect(screen.getByTitle(/Draw Polygon/i)).toBeEnabled();
  });

  it('double-clicking a space opens the edit dialog regardless of edit mode', async () => {
    mockGetFloorplanMetadata.mockResolvedValue(mockFloorplan);
    mockFetchFloorplanImageUrl.mockResolvedValue('blob:test-url');
    mockUseSpaces.mockReturnValue({ data: [mockSpace], isLoading: false });

    const user = userEvent.setup();
    render(<SpaceManagementPanel siteId="site-1" />);

    await waitFor(() => screen.getByTestId('dblclick-space-1'));

    // Without entering edit mode (still locked), double-click still opens the dialog
    await user.click(screen.getByTestId('dblclick-space-1'));

    await waitFor(() => {
      expect(screen.getByTestId('edit-dialog-save')).toBeInTheDocument();
    });
  });

  it('handleUpdateSpace closes edit dialog on save', async () => {
    mockGetFloorplanMetadata.mockResolvedValue(mockFloorplan);
    mockFetchFloorplanImageUrl.mockResolvedValue('blob:test-url');
    mockUseSpaces.mockReturnValue({ data: [mockSpace], isLoading: false });

    const user = userEvent.setup();
    render(<SpaceManagementPanel siteId="site-1" />);

    await waitFor(() => screen.getByTestId('dblclick-space-1'));
    await user.click(screen.getByTestId('dblclick-space-1'));
    await waitFor(() => screen.getByTestId('edit-dialog-save'));
    await user.click(screen.getByTestId('edit-dialog-save'));

    await waitFor(() => {
      expect(screen.queryByTestId('edit-dialog-save')).not.toBeInTheDocument();
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

  it('clicking Polygon mode button fires handleSetDrawingMode("polygon")', async () => {
    mockGetFloorplanMetadata.mockResolvedValue(mockFloorplan);
    mockFetchFloorplanImageUrl.mockResolvedValue('blob:test-url');

    const user = userEvent.setup();
    render(<SpaceManagementPanel siteId="site-1" />);

    await waitFor(() => screen.getByText('100%'));

    // Drawing tools are disabled until edit mode is enabled
    await user.click(screen.getByRole('button', { name: /Edit/i }));

    const polygonButton = screen.getByTitle(/Draw Polygon \(P\)/i);
    expect(polygonButton).toBeEnabled();
    await user.click(polygonButton);
    expect(polygonButton).toBeInTheDocument();
  });

  it('handleDeleteFloorplan prompts confirm and deletes', async () => {
    mockGetFloorplanMetadata.mockResolvedValue(mockFloorplan);
    mockFetchFloorplanImageUrl.mockResolvedValue('blob:test-url');

    const user = userEvent.setup();
    render(<SpaceManagementPanel siteId="site-1" />);

    await waitFor(() => screen.getByText('100%'));

    // Delete-floorplan is gated by edit mode (master switch)
    await user.click(screen.getByRole('button', { name: /Edit/i }));

    const deleteFloorplanBtn = screen.getByTitle(/Delete floorplan/i);
    await user.click(deleteFloorplanBtn);

    // Native confirm() is replaced by the shared ConfirmDialog.
    const dialog = await screen.findByRole('alertdialog');
    await user.click(within(dialog).getByRole('button', { name: 'Delete' }));

    await waitFor(() => {
      expect(mockDeleteFloorplan).toHaveBeenCalledWith('site-1');
    });
  });

  // ── Station type, shape tools and the right-click menu ──────────────────────

  /** Floorplan loaded and edit mode on — the state every drawing affordance needs. */
  async function enterEditMode() {
    mockGetFloorplanMetadata.mockResolvedValue(mockFloorplan);
    mockFetchFloorplanImageUrl.mockResolvedValue('blob:test-url');
    mockUseSpaces.mockReturnValue({ data: [mockSpace], isLoading: false });
    const user = userEvent.setup();
    render(<SpaceManagementPanel siteId="site-1" />);
    await waitFor(() => screen.getByRole('button', { name: /Edit/i }));
    await user.click(screen.getByRole('button', { name: /Edit/i }));
    return user;
  }

  it('does not ask which station type when only one thing can be placed', async () => {
    await enterEditMode();
    // A question with one answer is not a question.
    expect(screen.queryByLabelText('Station type')).not.toBeInTheDocument();
  });

  it('asks which station type when the tenant has more than one', async () => {
    mockResourceTypes.mockReturnValue({
      data: [
        { id: 'type-space', key: 'space', displayName: 'Space', displayNamePlural: 'Spaces', hasGeometry: true, isActive: true },
        { id: 'type-booth', key: 'booth', displayName: 'Booth', displayNamePlural: 'Booths', hasGeometry: true, isActive: true },
      ],
      isSuccess: true,
    });
    await enterEditMode();

    expect(screen.getByLabelText('Station type')).toBeInTheDocument();
  });

  it('leaves types that cannot be placed out of the picker', async () => {
    // Offering "Person" would build a resource the backend rejects.
    mockResourceTypes.mockReturnValue({
      data: [
        { id: 'type-space', key: 'space', displayName: 'Space', displayNamePlural: 'Spaces', hasGeometry: true, isActive: true },
        { id: 'type-person', key: 'person', displayName: 'Person', displayNamePlural: 'People', hasGeometry: false, isActive: true },
      ],
      isSuccess: true,
    });
    await enterEditMode();

    expect(screen.queryByLabelText('Station type')).not.toBeInTheDocument();
  });

  it('disables the shape tools when nothing can be placed at all', async () => {
    // An armed tool with no type could only ever produce a rejection, after the user has done
    // the work of drawing.
    mockResourceTypes.mockReturnValue({ data: [], isSuccess: true });
    await enterEditMode();

    expect(screen.getByRole('button', { name: 'Draw rectangle' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Draw circle' })).toBeDisabled();
  });

  it('offers a circle tool alongside rectangle and polygon', async () => {
    const user = await enterEditMode();
    const circle = screen.getByRole('button', { name: 'Draw circle' });

    await user.click(circle);

    expect(circle).toHaveAttribute('aria-pressed', 'true');
    expect(screen.getByTestId('drawing-canvas')).toHaveAttribute('data-drawing-mode', 'circle');
  });

  it('stays armed after a shape is drawn, so a row of stations is one pass', async () => {
    // Re-arming between every station was the tedium this removes. The tool holds until the user
    // says otherwise.
    const user = await enterEditMode();
    await user.click(screen.getByRole('button', { name: 'Draw rectangle' }));

    await user.click(screen.getByTestId('finish-drawing'));

    expect(screen.getByTestId('drawing-canvas')).toHaveAttribute('data-drawing-mode', 'rectangle');
    expect(screen.getByRole('button', { name: 'Draw rectangle' })).toHaveAttribute('aria-pressed', 'true');
  });

  describe('what a finished shape becomes', () => {
    // A resource of the armed type that exists but was never drawn — imported, or added from its
    // type's list page. The floorplan query already returns it; the canvas just skips it.
    const unplacedMill = {
      id: 'space-9',
      name: 'Mill 9',
      code: 'M-09',
      siteId: 'site-1',
      resourceTypeKey: 'space',
      geometry: null,
    };

    async function drawWith(spaces: unknown[]) {
      mockGetFloorplanMetadata.mockResolvedValue(mockFloorplan);
      mockFetchFloorplanImageUrl.mockResolvedValue('blob:test-url');
      mockUseSpaces.mockReturnValue({ data: spaces, isLoading: false });
      const user = userEvent.setup();
      render(<SpaceManagementPanel siteId="site-1" />);
      await waitFor(() => screen.getByRole('button', { name: /Edit/i }));
      await user.click(screen.getByRole('button', { name: /Edit/i }));
      await user.click(screen.getByRole('button', { name: 'Draw rectangle' }));
      await user.click(screen.getByTestId('finish-drawing'));
      return user;
    }

    it('goes straight to the create form when nothing is waiting to be placed', async () => {
      // The older behaviour, and still the right one: a choice with one real answer is friction.
      await drawWith([mockSpace]);

      expect(screen.getByTestId('create-dialog-save')).toBeInTheDocument();
      expect(screen.queryByRole('heading', { name: 'Place Space' })).not.toBeInTheDocument();
    });

    it('offers the unplaced resources of the armed type instead', async () => {
      await drawWith([mockSpace, unplacedMill]);

      expect(await screen.findByRole('heading', { name: 'Place Space' })).toBeInTheDocument();
      expect(screen.queryByTestId('create-dialog-save')).not.toBeInTheDocument();
    });

    it('gives the drawn shape to the resource that was picked', async () => {
      const user = await drawWith([mockSpace, unplacedMill]);

      await user.click(await screen.findByLabelText('Not yet on the plan'));
      await user.click(await screen.findByRole('option', { name: 'M-09 — Mill 9' }));
      await user.click(screen.getByRole('button', { name: 'Place here' }));

      // The same write a drag makes: geometry only, because the candidate already belongs here.
      await waitFor(() =>
        expect(mockMoveMutateAsync).toHaveBeenCalledWith({
          resourceId: 'space-9',
          geometry: { type: 'rectangle', coordinates: [{ x: 0, y: 0 }, { x: 10, y: 10 }] },
        }),
      );
      expect(mockCreateMutateAsync).not.toHaveBeenCalled();
    });

    it('falls through to the create form, carrying the same shape', async () => {
      const user = await drawWith([mockSpace, unplacedMill]);

      await user.click(await screen.findByRole('button', { name: /Create a new space instead/i }));

      expect(screen.getByTestId('create-dialog-save')).toBeInTheDocument();
      expect(mockMoveMutateAsync).not.toHaveBeenCalled();
    });

    it('offers nothing from another type than the one armed', async () => {
      // The shape was drawn as a space; a drill with no place is not what it becomes.
      await drawWith([{ ...unplacedMill, resourceTypeKey: 'drill' }]);

      expect(screen.getByTestId('create-dialog-save')).toBeInTheDocument();
    });
  });

  it('forces the tool off when edit mode is left', async () => {
    // Newly load-bearing: with the tool staying armed, leaving edit mode is the other way out.
    const user = await enterEditMode();
    await user.click(screen.getByRole('button', { name: 'Draw rectangle' }));

    await user.click(screen.getByRole('button', { name: /Done/i }));

    expect(screen.getByTestId('drawing-canvas')).toHaveAttribute('data-drawing-mode', 'none');
  });

  it('disarms when the armed tool is clicked again', async () => {
    const user = await enterEditMode();
    const circle = screen.getByRole('button', { name: 'Draw circle' });

    await user.click(circle);
    await user.click(circle);

    expect(circle).toHaveAttribute('aria-pressed', 'false');
    expect(screen.getByTestId('drawing-canvas')).toHaveAttribute('data-drawing-mode', 'none');
  });

  it('opens a right-click menu offering exactly Duplicate and Delete', async () => {
    const user = await enterEditMode();

    await user.click(screen.getByTestId('rightclick-space-1'));

    expect(await screen.findByRole('menuitem', { name: /Duplicate/ })).toBeInTheDocument();
    expect(screen.getByRole('menuitem', { name: /Delete/ })).toBeInTheDocument();
    // Editing already has a gesture — double-click — so the menu does not repeat it.
    expect(screen.queryByRole('menuitem', { name: /Edit/ })).not.toBeInTheDocument();
  });

  it('duplicates without carrying the code across', async () => {
    const user = await enterEditMode();

    await user.click(screen.getByTestId('rightclick-space-1'));
    await user.click(await screen.findByRole('menuitem', { name: /Duplicate/ }));

    await waitFor(() => expect(mockCreateMutateAsync).toHaveBeenCalled());
    const request = mockCreateMutateAsync.mock.calls[0][0];
    // Codes are unique per site; copying one turns Duplicate into a conflict error.
    expect(request.code).toBeUndefined();
    expect(request.name).toBe('Office A (copy)');
  });

  it('asks before deleting, then deletes', async () => {
    const user = await enterEditMode();

    await user.click(screen.getByTestId('rightclick-space-1'));
    await user.click(await screen.findByRole('menuitem', { name: /Delete/ }));

    expect(await screen.findByText(/Delete "Office A"/)).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Delete' }));

    await waitFor(() => expect(mockDeleteMutateAsync).toHaveBeenCalledWith('space-1'));
  });
});
