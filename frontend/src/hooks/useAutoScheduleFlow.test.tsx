import { describe, it, expect, vi, beforeEach } from 'vitest';
import { act, renderHook, waitFor } from '@testing-library/react';
import { toast } from 'sonner';
import { useAutoScheduleFlow, STALE_PREVIEW_MESSAGE } from './useAutoScheduleFlow';
import { previewAutoSchedule, applyAutoSchedule } from '@foundation/src/lib/api/auto-schedule-api';
import { ApiError } from '@foundation/src/lib/core/api-utils';
import { useUiActionsStore } from '@foundation/src/store/ui-actions-store';
import { createTestQueryWrapper } from '@foundation/src/test-utils';
import { mockAuth } from '@foundation/src/test-utils/auth';

vi.mock('@foundation/src/lib/api/auto-schedule-api', () => ({
  previewAutoSchedule: vi.fn(),
  applyAutoSchedule: vi.fn(),
}));

vi.mock('@foundation/src/contexts/AuthContext', () => ({
  useAuth: () => mockAuth({ membership: { tier: 'professional' } }),
}));
vi.mock('@foundation/src/hooks/useTenantSettings', () => ({
  useTenantSettings: () => ({
    data: { settings: [{ key: 'scheduling.auto_schedule_enabled', currentValue: 'True' }] },
  }),
}));

const PREVIEW = { fingerprint: 'fp-1', assignments: [{}, {}] };
const ANCHOR = new Date(2026, 0, 15);

function renderFlow(scope: Partial<Parameters<typeof useAutoScheduleFlow>[0]> = {}) {
  return renderHook(
    () => useAutoScheduleFlow({ siteId: 'site-1', anchorTs: ANCHOR, resourceTypeKeys: undefined, ...scope }),
    { wrapper: createTestQueryWrapper({ feedback: true }) },
  );
}

async function openPreview(result: ReturnType<typeof renderFlow>['result']) {
  await act(() => result.current.start());
  expect(result.current.dialog.open).toBe(true);
}

beforeEach(() => {
  vi.clearAllMocks();
  useUiActionsStore.setState({ autoScheduleRequestIds: null });
  vi.mocked(previewAutoSchedule).mockResolvedValue(PREVIEW as never);
  vi.mocked(applyAutoSchedule).mockResolvedValue({ createdAssignments: 2, unscheduledCount: 0 });
});

describe('useAutoScheduleFlow', () => {
  it('reports availability from the plan and setting', () => {
    const { result } = renderFlow();
    expect(result.current.available).toBe(true);
  });

  it('previews a three-month horizon from the anchor for the filtered types', async () => {
    const { result } = renderFlow({ resourceTypeKeys: ['tool'] });
    await openPreview(result);

    expect(vi.mocked(previewAutoSchedule).mock.calls[0][0]).toEqual({
      siteId: 'site-1',
      horizonStart: '2026-01-15',
      horizonEnd: '2026-04-15',
      resourceTypeKeys: ['tool'],
    });
    expect(result.current.dialog.preview).toEqual(PREVIEW);
  });

  it('does nothing without a site', async () => {
    const { result } = renderFlow({ siteId: null });
    await act(() => result.current.start());
    await act(() => result.current.dialog.onApply());
    expect(previewAutoSchedule).not.toHaveBeenCalled();
    expect(applyAutoSchedule).not.toHaveBeenCalled();
  });

  it('keeps the dialog closed when the preview fails', async () => {
    vi.mocked(previewAutoSchedule).mockRejectedValueOnce(new Error('boom'));
    const { result } = renderFlow();
    await act(() => result.current.start());
    expect(result.current.dialog.open).toBe(false);
  });

  it('applies with the preview fingerprint, toasts the count and closes', async () => {
    const { result } = renderFlow();
    await openPreview(result);

    await act(() => result.current.dialog.onApply());

    expect(vi.mocked(applyAutoSchedule).mock.calls[0][0]).toMatchObject({
      siteId: 'site-1',
      previewFingerprint: 'fp-1',
      requestIds: undefined,
    });
    expect(toast.success).toHaveBeenCalledWith('Scheduled 2 requests');
    expect(result.current.dialog.open).toBe(false);
    expect(result.current.dialog.preview).toBeNull();
  });

  it('shows 409 conflict error on apply', async () => {
    vi.mocked(applyAutoSchedule).mockRejectedValueOnce(new ApiError('Conflict', 409));
    const { result } = renderFlow();
    await openPreview(result);

    await act(() => result.current.dialog.onApply());

    expect(result.current.dialog.applyError).toBe(STALE_PREVIEW_MESSAGE);
    expect(result.current.dialog.open).toBe(true);
    expect(toast.error).not.toHaveBeenCalled();
  });

  it('shows generic error on apply failure', async () => {
    vi.mocked(applyAutoSchedule).mockRejectedValueOnce(new Error('Server error'));
    const { result } = renderFlow();
    await openPreview(result);

    await act(() => result.current.dialog.onApply());

    expect(result.current.dialog.applyError).toBe('Server error');
  });

  it('stringifies a non-Error rejection via the shared errorMessage normalizer', async () => {
    vi.mocked(applyAutoSchedule).mockRejectedValueOnce('something');
    const { result } = renderFlow();
    await openPreview(result);

    await act(() => result.current.dialog.onApply());

    expect(result.current.dialog.applyError).toBe('something');
  });

  it('close clears the preview and the error', async () => {
    vi.mocked(applyAutoSchedule).mockRejectedValueOnce(new Error('Server error'));
    const { result } = renderFlow();
    await openPreview(result);
    await act(() => result.current.dialog.onApply());

    act(() => result.current.dialog.onClose());

    expect(result.current.dialog).toMatchObject({ open: false, preview: null, applyError: null });
  });

  it('previews an accepted proposal once and applies over exactly its requests', async () => {
    const { result } = renderFlow();

    act(() => useUiActionsStore.getState().requestAutoSchedule(['r1', 'r2']));

    await waitFor(() => expect(result.current.dialog.open).toBe(true));
    expect(vi.mocked(previewAutoSchedule).mock.calls[0][0]).toMatchObject({ requestIds: ['r1', 'r2'] });
    // Consumed: coming back later must not re-open it.
    expect(useUiActionsStore.getState().autoScheduleRequestIds).toBeNull();

    await act(() => result.current.dialog.onApply());
    expect(vi.mocked(applyAutoSchedule).mock.calls[0][0]).toMatchObject({ requestIds: ['r1', 'r2'] });
    expect(previewAutoSchedule).toHaveBeenCalledTimes(1);
  });
});
