import { describe, it, expect, vi, beforeEach } from 'vitest';
import { act, renderHook, waitFor } from '@testing-library/react';
import { useAssistantConversation, type AssistantContext } from './useAssistantConversation';
import {
  deleteAiConversation,
  getAiConversation,
  saveAiConversation,
  streamAiChat,
} from '@foundation/src/lib/api/ai-api';
import { createTestQueryWrapper } from '@foundation/src/test-utils';
import { useSiteStore } from '@foundation/src/store/site-store';

vi.mock('@foundation/src/lib/api/ai-api', () => ({
  streamAiChat: vi.fn(async function* () {
    yield { type: 'message', text: 'Two conflicts this week.' };
    yield { type: 'transcript', messages: [{ role: 'user', content: 'q' }] };
    yield { type: 'done' };
  }),
  getAiStatus: vi.fn(async () => ({ available: true, dailyTurnLimit: null, usedTurnsToday: 0, privateChat: false })),
  listAiConversations: vi.fn(async () => []),
  getAiConversation: vi.fn(),
  saveAiConversation: vi.fn(async () => undefined),
  deleteAiConversation: vi.fn(async () => undefined),
}));

beforeEach(() => {
  useSiteStore.setState({ selectedSiteId: 'site-1' });
});

function renderConversation(initial: { open: boolean; context?: AssistantContext | null }) {
  return renderHook((props: { open: boolean; context?: AssistantContext | null }) => useAssistantConversation(props), {
    initialProps: initial,
    wrapper: createTestQueryWrapper(),
  });
}

describe('useAssistantConversation', () => {
  it('runs a turn for a sent message and saves the finished conversation under the first question', async () => {
    const { result } = renderConversation({ open: true });

    await act(() => result.current.send('Any conflicts?'));

    expect(result.current.entries).toEqual([
      { kind: 'user', text: 'Any conflicts?' },
      { kind: 'assistant', text: 'Two conflicts this week.' },
    ]);
    expect(vi.mocked(streamAiChat).mock.calls[0][0]).toMatchObject({ message: 'Any conflicts?', siteId: 'site-1' });
    await waitFor(() => expect(saveAiConversation).toHaveBeenCalledTimes(1));
    expect(vi.mocked(saveAiConversation).mock.calls[0][1]).toMatchObject({ title: 'Any conflicts?' });
  });

  it('ignores an empty message', async () => {
    const { result } = renderConversation({ open: true });
    await act(() => result.current.send(''));
    expect(streamAiChat).not.toHaveBeenCalled();
  });

  it('seeds a conflict context once, however often it re-renders', async () => {
    const context: AssistantContext = { type: 'conflict', requestId: 'req-1', kind: 'overlap' };
    const { rerender } = renderConversation({ open: true, context });

    await waitFor(() => expect(streamAiChat).toHaveBeenCalledTimes(1));
    expect(vi.mocked(streamAiChat).mock.calls[0][0]).toMatchObject({
      context: { type: 'conflict', requestId: 'req-1', kind: 'overlap' },
      transcript: [],
    });

    rerender({ open: true, context: { ...context } });
    rerender({ open: true, context: { type: 'conflict', requestId: 'req-2' } });
    await waitFor(() => expect(streamAiChat).toHaveBeenCalledTimes(2));
  });

  it('opens a saved conversation, and says so in a notice when it cannot', async () => {
    vi.mocked(getAiConversation).mockResolvedValueOnce({
      id: 'conv-1',
      title: 'Old',
      entries: [{ kind: 'user', text: 'old question' }],
      transcript: [],
    } as never);
    const { result } = renderConversation({ open: true });

    await act(() => result.current.openConversation('conv-1'));
    expect(result.current.entries).toEqual([{ kind: 'user', text: 'old question' }]);
    // A restore is not written straight back.
    expect(saveAiConversation).not.toHaveBeenCalled();

    vi.mocked(getAiConversation).mockRejectedValueOnce(new Error('404'));
    await act(() => result.current.openConversation('conv-2'));
    expect(result.current.notice).toBe('That conversation could not be opened.');
  });

  it('starts afresh when the conversation on screen is deleted', async () => {
    vi.mocked(getAiConversation).mockResolvedValueOnce({
      id: 'conv-1', title: 'Old', entries: [{ kind: 'user', text: 'q' }], transcript: [],
    } as never);
    const { result } = renderConversation({ open: true });
    await act(() => result.current.openConversation('conv-1'));

    await act(() => result.current.deleteConversation('conv-1'));

    expect(deleteAiConversation).toHaveBeenCalledWith('conv-1');
    expect(result.current.entries).toEqual([]);
  });

  it('records a daily-limit error and shows it in the log', async () => {
    vi.mocked(streamAiChat).mockImplementationOnce(async function* () {
      yield { type: 'error', code: 'daily_limit_reached', message: 'Limit reached' };
    } as never);
    const { result } = renderConversation({ open: true });

    await act(() => result.current.send('hi'));

    expect(result.current.dailyLimitReached).toBe(true);
    expect(result.current.entries.at(-1)).toEqual({ kind: 'error', text: 'Limit reached' });
  });
});
