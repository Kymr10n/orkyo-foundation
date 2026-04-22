import { describe, it, expect, beforeEach } from 'vitest';
import { useSchedulerStore, MIN_DURATION_FLOOR_MS, RESIZE_MOVE_THRESHOLD_PX } from './scheduler-store';

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

const T = (iso: string) => new Date(iso).getTime();

const START_MS = T('2024-06-01T08:00:00.000Z');
const END_MS   = T('2024-06-01T10:00:00.000Z');

function resetStore() {
  useSchedulerStore.setState({ draft: null });
}

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

describe('exported constants', () => {
  it('RESIZE_MOVE_THRESHOLD_PX is a positive number', () => {
    expect(RESIZE_MOVE_THRESHOLD_PX).toBeGreaterThan(0);
  });

  it('MIN_DURATION_FLOOR_MS is 60 seconds (1 minute)', () => {
    expect(MIN_DURATION_FLOOR_MS).toBe(60_000);
  });
});

// ---------------------------------------------------------------------------
// Initial state
// ---------------------------------------------------------------------------

describe('initial state', () => {
  beforeEach(resetStore);

  it('draft is null', () => {
    expect(useSchedulerStore.getState().draft).toBeNull();
  });
});

// ---------------------------------------------------------------------------
// startResize
// ---------------------------------------------------------------------------

describe('startResize', () => {
  beforeEach(resetStore);

  it('sets draft with correct fields', () => {
    useSchedulerStore.getState().startResize({
      requestId: 'req-1',
      spaceId: 's1',
      edge: 'right',
      committedStartMs: START_MS,
      committedEndMs: END_MS,
    });
    const { draft } = useSchedulerStore.getState();
    expect(draft).not.toBeNull();
    expect(draft!.kind).toBe('resize');
    expect(draft!.phase).toBe('active');
    expect(draft!.requestId).toBe('req-1');
    expect(draft!.spaceId).toBe('s1');
    expect(draft!.edge).toBe('right');
    expect(draft!.committedStartMs).toBe(START_MS);
    expect(draft!.committedEndMs).toBe(END_MS);
  });

  it('seeds previewStartMs and previewEndMs from committed bounds', () => {
    useSchedulerStore.getState().startResize({
      requestId: 'req-1',
      spaceId: 's1',
      edge: 'right',
      committedStartMs: START_MS,
      committedEndMs: END_MS,
    });
    const { draft } = useSchedulerStore.getState();
    expect(draft!.previewStartMs).toBe(START_MS);
    expect(draft!.previewEndMs).toBe(END_MS);
  });

  it('works for a left-edge resize', () => {
    useSchedulerStore.getState().startResize({
      requestId: 'req-1',
      spaceId: 's1',
      edge: 'left',
      committedStartMs: START_MS,
      committedEndMs: END_MS,
    });
    expect(useSchedulerStore.getState().draft!.edge).toBe('left');
  });
});

// ---------------------------------------------------------------------------
// updateResize
// ---------------------------------------------------------------------------

describe('updateResize', () => {
  beforeEach(() => {
    resetStore();
    useSchedulerStore.getState().startResize({
      requestId: 'req-1',
      spaceId: 's1',
      edge: 'right',
      committedStartMs: START_MS,
      committedEndMs: END_MS,
    });
  });

  it('updates previewStartMs and previewEndMs', () => {
    const newEnd = T('2024-06-01T12:00:00.000Z');
    useSchedulerStore.getState().updateResize(START_MS, newEnd);
    const { draft } = useSchedulerStore.getState();
    expect(draft!.previewStartMs).toBe(START_MS);
    expect(draft!.previewEndMs).toBe(newEnd);
  });

  it('does not change committedStartMs / committedEndMs', () => {
    const newEnd = T('2024-06-01T12:00:00.000Z');
    useSchedulerStore.getState().updateResize(START_MS, newEnd);
    const { draft } = useSchedulerStore.getState();
    expect(draft!.committedStartMs).toBe(START_MS);
    expect(draft!.committedEndMs).toBe(END_MS);
  });

  it('is a no-op when draft is null', () => {
    resetStore();
    useSchedulerStore.getState().updateResize(START_MS, END_MS); // should not throw
    expect(useSchedulerStore.getState().draft).toBeNull();
  });

  it('is a no-op when draft is in committing phase', () => {
    useSchedulerStore.getState().commitResize(); // phase → "committing"
    const before = useSchedulerStore.getState().draft;
    useSchedulerStore.getState().updateResize(START_MS, T('2024-06-01T18:00:00.000Z'));
    expect(useSchedulerStore.getState().draft).toBe(before);
  });
});

// ---------------------------------------------------------------------------
// commitResize
// ---------------------------------------------------------------------------

describe('commitResize', () => {
  beforeEach(() => {
    resetStore();
    useSchedulerStore.getState().startResize({
      requestId: 'req-1',
      spaceId: 's1',
      edge: 'right',
      committedStartMs: START_MS,
      committedEndMs: END_MS,
    });
  });

  it('returns the current preview bounds', () => {
    const newEnd = T('2024-06-01T12:00:00.000Z');
    useSchedulerStore.getState().updateResize(START_MS, newEnd);
    const result = useSchedulerStore.getState().commitResize();
    expect(result).toEqual({ startMs: START_MS, endMs: newEnd });
  });

  it('transitions draft to "committing" phase (does not clear)', () => {
    useSchedulerStore.getState().commitResize();
    const { draft } = useSchedulerStore.getState();
    expect(draft).not.toBeNull();
    expect(draft!.phase).toBe('committing');
  });

  it('returns the initial bounds when updateResize was never called', () => {
    const result = useSchedulerStore.getState().commitResize();
    expect(result).toEqual({ startMs: START_MS, endMs: END_MS });
  });

  it('returns null when no resize is active', () => {
    resetStore();
    const result = useSchedulerStore.getState().commitResize();
    expect(result).toBeNull();
  });

  it('returns null when already in committing phase', () => {
    useSchedulerStore.getState().commitResize();
    const second = useSchedulerStore.getState().commitResize();
    expect(second).toBeNull();
  });
});

// ---------------------------------------------------------------------------
// finalizeDraft
// ---------------------------------------------------------------------------

describe('finalizeDraft', () => {
  beforeEach(() => {
    resetStore();
    useSchedulerStore.getState().startResize({
      requestId: 'req-1',
      spaceId: 's1',
      edge: 'right',
      committedStartMs: START_MS,
      committedEndMs: END_MS,
    });
    useSchedulerStore.getState().commitResize(); // phase → "committing"
  });

  it('clears draft when requestId matches', () => {
    useSchedulerStore.getState().finalizeDraft('req-1');
    expect(useSchedulerStore.getState().draft).toBeNull();
  });

  it('is a no-op when requestId does not match', () => {
    useSchedulerStore.getState().finalizeDraft('req-other');
    expect(useSchedulerStore.getState().draft).not.toBeNull();
  });

  it('is a no-op when draft is already null', () => {
    resetStore();
    useSchedulerStore.getState().finalizeDraft('req-1'); // should not throw
    expect(useSchedulerStore.getState().draft).toBeNull();
  });
});

// ---------------------------------------------------------------------------
// cancelResize
// ---------------------------------------------------------------------------

describe('cancelResize', () => {
  beforeEach(() => {
    resetStore();
    useSchedulerStore.getState().startResize({
      requestId: 'req-1',
      spaceId: 's1',
      edge: 'right',
      committedStartMs: START_MS,
      committedEndMs: END_MS,
    });
  });

  it('clears the draft', () => {
    useSchedulerStore.getState().cancelResize();
    expect(useSchedulerStore.getState().draft).toBeNull();
  });

  it('is a no-op when draft is already null', () => {
    resetStore();
    useSchedulerStore.getState().cancelResize(); // should not throw
    expect(useSchedulerStore.getState().draft).toBeNull();
  });
});

// ---------------------------------------------------------------------------
// Full interaction lifecycle
// ---------------------------------------------------------------------------

describe('full resize lifecycle', () => {
  beforeEach(resetStore);

  it('start → update → commit → finalize produces correct final bounds', () => {
    const { startResize, updateResize } = useSchedulerStore.getState();

    startResize({ requestId: 'r1', spaceId: 's1', edge: 'right', committedStartMs: START_MS, committedEndMs: END_MS });

    const step1 = T('2024-06-01T11:00:00.000Z');
    updateResize(START_MS, step1);

    const step2 = T('2024-06-01T12:00:00.000Z');
    useSchedulerStore.getState().updateResize(START_MS, step2);

    const result = useSchedulerStore.getState().commitResize();
    expect(result).toEqual({ startMs: START_MS, endMs: step2 });
    // Draft still alive in "committing" phase
    expect(useSchedulerStore.getState().draft).not.toBeNull();
    expect(useSchedulerStore.getState().draft!.phase).toBe('committing');

    // Finalize clears it
    useSchedulerStore.getState().finalizeDraft('r1');
    expect(useSchedulerStore.getState().draft).toBeNull();
  });

  it('start → update → cancel leaves draft null without committing', () => {
    useSchedulerStore.getState().startResize({
      requestId: 'r1', spaceId: 's1', edge: 'left', committedStartMs: START_MS, committedEndMs: END_MS,
    });
    useSchedulerStore.getState().updateResize(T('2024-06-01T07:00:00.000Z'), END_MS);
    useSchedulerStore.getState().cancelResize();

    expect(useSchedulerStore.getState().draft).toBeNull();
  });
});
