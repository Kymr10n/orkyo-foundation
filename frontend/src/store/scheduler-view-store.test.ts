import { describe, it, expect, beforeEach } from 'vitest';
import { useSchedulerViewStore } from '@foundation/src/store/scheduler-view-store';

describe('useSchedulerViewStore', () => {
  beforeEach(() => {
    localStorage.clear();
    useSchedulerViewStore.setState({
      scale: 'week',
      anchorTs: new Date(),
      timeCursorTs: new Date(),
      spaceOrder: [],
    });
  });

  it('sets the scale', () => {
    useSchedulerViewStore.getState().setScale('day');
    expect(useSchedulerViewStore.getState().scale).toBe('day');

    useSchedulerViewStore.getState().setScale('month');
    expect(useSchedulerViewStore.getState().scale).toBe('month');
  });

  it('sets the anchor timestamp', () => {
    const testDate = new Date('2026-01-15');
    useSchedulerViewStore.getState().setAnchorTs(testDate);
    expect(useSchedulerViewStore.getState().anchorTs).toEqual(testDate);
  });

  it('sets the time cursor timestamp', () => {
    const testDate = new Date('2026-02-20');
    useSchedulerViewStore.getState().setTimeCursorTs(testDate);
    expect(useSchedulerViewStore.getState().timeCursorTs).toEqual(testDate);
  });

  it('replaces the space order wholesale on each drag', () => {
    useSchedulerViewStore.getState().setSpaceOrder(['space-1', 'space-2']);
    useSchedulerViewStore.getState().setSpaceOrder(['space-2', 'space-1', 'space-3']);
    expect(useSchedulerViewStore.getState().spaceOrder).toEqual([
      'space-2',
      'space-1',
      'space-3',
    ]);
  });

  it('persists nothing: the view is for this visit only', () => {
    useSchedulerViewStore.getState().setScale('hour');
    useSchedulerViewStore.getState().setSpaceOrder(['space-1']);
    expect(localStorage.length).toBe(0);
  });
});
