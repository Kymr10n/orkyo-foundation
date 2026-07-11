/* eslint-disable @typescript-eslint/no-explicit-any */
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { useRequestEditor } from '@foundation/src/components/requests/useRequestEditor';
import type { Request } from '@foundation/src/types/requests';
import type { RequestFormData } from '@foundation/src/components/requests/RequestFormDialog';

// ---------------------------------------------------------------------------
// Mocks
// ---------------------------------------------------------------------------

let mockRole: string | undefined = 'admin';

vi.mock('@foundation/src/contexts/AuthContext', () => ({
  useAuth: () => ({
    membership: mockRole ? { role: mockRole } : null,
  }),
}));

const mockUpdateRequest = vi.fn();
vi.mock('@foundation/src/lib/api/request-api', () => ({
  updateRequest: (...args: unknown[]) => mockUpdateRequest(...args),
}));

vi.mock('@foundation/src/components/requests/RequestFormDialog', () => ({
  RequestFormDialog: ({ open, onSave, onOpenChange, canEdit }: any) =>
    open ? (
      <div data-testid="form-dialog" data-can-edit={String(canEdit)}>
        <button data-testid="save-btn" onClick={() => onSave(mockFormData)}>Save</button>
        <button data-testid="close-edit-btn" onClick={() => onOpenChange(false)}>Cancel</button>
      </div>
    ) : null,
}));

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

const mockRequest = {
  id: 'req-1',
  name: 'Test Request',
  durationMin: 60,
  createdAt: '2026-01-01T00:00Z',
  updatedAt: '2026-01-01T00:00Z',
} as Request;

const mockFormData: RequestFormData = {
  name: 'Updated Name',
  planningMode: 'leaf',
  duration: { value: 60, unit: 'minutes' },
  schedulingSettingsApply: false,
  requirements: [],
};

// ---------------------------------------------------------------------------
// Test component
// ---------------------------------------------------------------------------

function TestHookComponent() {
  const { open, dialogs } = useRequestEditor();
  return (
    <div>
      <button data-testid="open-btn" onClick={() => open(mockRequest)}>Open</button>
      {dialogs}
    </div>
  );
}

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function renderWithClient(queryClient: QueryClient) {
  return render(
    <QueryClientProvider client={queryClient}>
      <TestHookComponent />
    </QueryClientProvider>
  );
}

function makeQueryClient() {
  return new QueryClient({ defaultOptions: { queries: { retry: false } } });
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('useRequestEditor', () => {
  beforeEach(() => {
    mockRole = 'admin';
    mockUpdateRequest.mockResolvedValue(undefined);
  });

  describe('role gate', () => {
    it('opens the form dialog in edit mode for admin role', () => {
      mockRole = 'admin';
      renderWithClient(makeQueryClient());
      fireEvent.click(screen.getByTestId('open-btn'));
      expect(screen.getByTestId('form-dialog')).toHaveAttribute('data-can-edit', 'true');
    });

    it('opens the form dialog in edit mode for editor role', () => {
      mockRole = 'editor';
      renderWithClient(makeQueryClient());
      fireEvent.click(screen.getByTestId('open-btn'));
      expect(screen.getByTestId('form-dialog')).toHaveAttribute('data-can-edit', 'true');
    });

    it('opens the form dialog in view mode for member role', () => {
      mockRole = 'member';
      renderWithClient(makeQueryClient());
      fireEvent.click(screen.getByTestId('open-btn'));
      expect(screen.getByTestId('form-dialog')).toHaveAttribute('data-can-edit', 'false');
    });

    it('opens the form dialog in view mode when membership is null', () => {
      mockRole = undefined;
      renderWithClient(makeQueryClient());
      fireEvent.click(screen.getByTestId('open-btn'));
      expect(screen.getByTestId('form-dialog')).toHaveAttribute('data-can-edit', 'false');
    });
  });

  describe('save handler', () => {
    it('calls updateRequest with the request id on save', async () => {
      renderWithClient(makeQueryClient());
      fireEvent.click(screen.getByTestId('open-btn'));
      fireEvent.click(screen.getByTestId('save-btn'));
      await waitFor(() => {
        expect(mockUpdateRequest).toHaveBeenCalledWith('req-1', expect.anything());
      });
    });

    it('invalidates the requests AND conflicts queries on save', async () => {
      // Conflicts are derived from request state, so an edit must refresh both — otherwise the
      // grid's conflict badges go stale (e.g. a newly-recorded below_min_duration conflict).
      const queryClient = makeQueryClient();
      const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries');
      render(
        <QueryClientProvider client={queryClient}>
          <TestHookComponent />
        </QueryClientProvider>
      );
      fireEvent.click(screen.getByTestId('open-btn'));
      fireEvent.click(screen.getByTestId('save-btn'));
      await waitFor(() => {
        expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: ['requests'] });
        expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: ['conflicts'] });
      });
    });

    it('closes the edit dialog after save', async () => {
      renderWithClient(makeQueryClient());
      fireEvent.click(screen.getByTestId('open-btn'));
      expect(screen.getByTestId('form-dialog')).toBeInTheDocument();
      fireEvent.click(screen.getByTestId('save-btn'));
      await waitFor(() => {
        expect(screen.queryByTestId('form-dialog')).not.toBeInTheDocument();
      });
    });

    it('can re-open the dialog after a save', async () => {
      renderWithClient(makeQueryClient());
      fireEvent.click(screen.getByTestId('open-btn'));
      fireEvent.click(screen.getByTestId('save-btn'));
      await waitFor(() => {
        expect(screen.queryByTestId('form-dialog')).not.toBeInTheDocument();
      });
      fireEvent.click(screen.getByTestId('open-btn'));
      expect(screen.getByTestId('form-dialog')).toBeInTheDocument();
    });
  });

  describe('dialog close via onOpenChange', () => {
    it('closes edit dialog when onOpenChange fires false', () => {
      renderWithClient(makeQueryClient());
      fireEvent.click(screen.getByTestId('open-btn'));
      expect(screen.getByTestId('form-dialog')).toBeInTheDocument();
      fireEvent.click(screen.getByTestId('close-edit-btn'));
      expect(screen.queryByTestId('form-dialog')).not.toBeInTheDocument();
    });

    it('closes the view-mode form dialog when onOpenChange fires false', () => {
      mockRole = 'member';
      renderWithClient(makeQueryClient());
      fireEvent.click(screen.getByTestId('open-btn'));
      expect(screen.getByTestId('form-dialog')).toHaveAttribute('data-can-edit', 'false');
      fireEvent.click(screen.getByTestId('close-edit-btn'));
      expect(screen.queryByTestId('form-dialog')).not.toBeInTheDocument();
    });
  });
});
