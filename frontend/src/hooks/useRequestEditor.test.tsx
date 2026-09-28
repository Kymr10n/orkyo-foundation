/* eslint-disable @typescript-eslint/no-explicit-any */
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { screen, fireEvent, waitFor } from '@testing-library/react';
import { useRequestEditor } from '@foundation/src/hooks/useRequestEditor';
import type { Request } from '@foundation/src/types/requests';
import type { RequestFormData } from '@foundation/src/components/requests/RequestFormDialog';
import { renderWithQuery } from '@foundation/src/test-utils';
import { toast } from 'sonner';

vi.mock('sonner', () => ({ toast: { success: vi.fn(), error: vi.fn(), info: vi.fn() } }));

// ---------------------------------------------------------------------------
// Mocks
// ---------------------------------------------------------------------------

const mockUpdateRequest = vi.fn();
vi.mock('@foundation/src/lib/api/request-api', () => ({
  updateRequest: (...args: unknown[]) => mockUpdateRequest(...args),
}));

vi.mock('@foundation/src/components/requests/RequestFormDialog', () => ({
  RequestFormDialog: ({ open, onSave, onOpenChange }: any) =>
    open ? (
      <div data-testid="form-dialog">
        {/* The real dialog catches a rejected save and shows it inline. */}
        <button data-testid="save-btn" onClick={() => Promise.resolve(onSave(mockFormData)).catch(() => {})}>Save</button>
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
  targetResourceTypeKeys: ['space'],
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

// The production feedback cache: the save's toast and invalidation come from its meta.
const renderEditor = () => renderWithQuery(<TestHookComponent />, { feedback: true });

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('useRequestEditor', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockUpdateRequest.mockResolvedValue(undefined);
  });

  describe('save handler', () => {
    it('calls updateRequest with the request id on save', async () => {
      renderEditor();
      fireEvent.click(screen.getByTestId('open-btn'));
      fireEvent.click(screen.getByTestId('save-btn'));
      await waitFor(() => {
        expect(mockUpdateRequest).toHaveBeenCalledWith('req-1', expect.anything());
      });
    });

    it('invalidates the requests AND conflicts queries on save', async () => {
      // Conflicts are derived from request state, so an edit must refresh both — otherwise the
      // grid's conflict badges go stale (e.g. a newly-recorded below_min_duration conflict).
      const { invalidateSpy } = renderEditor();
      fireEvent.click(screen.getByTestId('open-btn'));
      fireEvent.click(screen.getByTestId('save-btn'));
      await waitFor(() => {
        expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: ['requests'], exact: false });
        expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: ['conflicts'], exact: false });
      });
    });

    it('toasts the save as the Requests page does, and leaves a failure to the dialog', async () => {
      renderEditor();
      fireEvent.click(screen.getByTestId('open-btn'));
      fireEvent.click(screen.getByTestId('save-btn'));
      await waitFor(() => expect(toast.success).toHaveBeenCalledWith('Request updated'));

      // The dialog shows a rejected save inline, so the cache must not toast it as well.
      mockUpdateRequest.mockRejectedValueOnce(new Error('Conflict'));
      fireEvent.click(screen.getByTestId('open-btn'));
      fireEvent.click(screen.getByTestId('save-btn'));
      await waitFor(() => expect(mockUpdateRequest).toHaveBeenCalledTimes(2));
      expect(toast.error).not.toHaveBeenCalled();
    });

    it('closes the edit dialog after save', async () => {
      renderEditor();
      fireEvent.click(screen.getByTestId('open-btn'));
      expect(screen.getByTestId('form-dialog')).toBeInTheDocument();
      fireEvent.click(screen.getByTestId('save-btn'));
      await waitFor(() => {
        expect(screen.queryByTestId('form-dialog')).not.toBeInTheDocument();
      });
    });

    it('can re-open the dialog after a save', async () => {
      renderEditor();
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
      renderEditor();
      fireEvent.click(screen.getByTestId('open-btn'));
      expect(screen.getByTestId('form-dialog')).toBeInTheDocument();
      fireEvent.click(screen.getByTestId('close-edit-btn'));
      expect(screen.queryByTestId('form-dialog')).not.toBeInTheDocument();
    });
  });
});
