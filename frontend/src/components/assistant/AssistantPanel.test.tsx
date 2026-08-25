import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import userEvent from '@testing-library/user-event';
import type { ReactElement } from 'react';

const proposals = vi.hoisted(() => ({ kind: 'propose_auto_schedule', input: '{}' }));

vi.mock('@foundation/src/lib/api/ai-api', () => ({
  // One turn that emits a proposal and stops — the shape the panel renders a card for.
  streamAiChat: vi.fn(async function* () {
    yield {
      type: 'proposal',
      proposal: { toolUseId: 'tool-1', kind: proposals.kind, input: proposals.input },
    };
    yield { type: 'done' };
  }),
  listAiConversations: vi.fn(async () => []),
  getAiConversation: vi.fn(),
  saveAiConversation: vi.fn(async () => undefined),
  deleteAiConversation: vi.fn(async () => undefined),
}));

vi.mock('@foundation/src/hooks/useAiAssistant', () => ({
  useAiStatus: () => ({ data: { enabled: true, remainingTokens: 1000 } }),
}));

import { AssistantPanel } from './AssistantPanel';
import {
  deleteAiConversation,
  getAiConversation,
  listAiConversations,
  saveAiConversation,
  streamAiChat,
} from '@foundation/src/lib/api/ai-api';

/** The panel reads its conversation list through react-query. */
function renderPanel(ui: ReactElement) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(<QueryClientProvider client={queryClient}>{ui}</QueryClientProvider>);
}

async function openPanelAndPropose() {
  const user = userEvent.setup();
  await user.type(screen.getByPlaceholderText(/ask about your schedule/i), 'fix it');
  await user.keyboard('{Enter}');
  return user;
}

describe('AssistantPanel proposal acceptance', () => {
  beforeEach(() => {
    proposals.kind = 'propose_auto_schedule';
    proposals.input = JSON.stringify({ requestIds: ['req-1', 'req-2'], rationale: 'solver' });
  });

  it('routes an auto-scheduling proposal to its own handler', async () => {
    // The regression: Apply only ever read `requestId`, so an auto-schedule proposal —
    // which carries `requestIds` — fell through and the button did nothing at all.
    const onApplyAutoSchedule = vi.fn().mockResolvedValue(undefined);

    renderPanel(
      <AssistantPanel
        open
        onOpenChange={vi.fn()}
        onApplyProposal={vi.fn()}
        onApplyAutoSchedule={onApplyAutoSchedule}
      />,
    );

    const user = await openPanelAndPropose();
    const apply = await screen.findByRole('button', { name: /apply/i });
    await user.click(apply);

    await waitFor(() => {
      expect(onApplyAutoSchedule).toHaveBeenCalledWith(['req-1', 'req-2']);
    });
  });

  it('offers no Apply button when the host cannot accept that kind', async () => {
    // Better to show no button than one that silently does nothing.
    renderPanel(<AssistantPanel open onOpenChange={vi.fn()} onApplyProposal={vi.fn()} />);

    await openPanelAndPropose();
    await screen.findByText(/solver/i);

    expect(screen.queryByRole('button', { name: /apply/i })).not.toBeInTheDocument();
  });

  it('still routes an update proposal to the update handler', async () => {
    proposals.kind = 'propose_update_request';
    proposals.input = JSON.stringify({
      requestId: 'req-9',
      changes: { startTs: '2026-03-02T09:00:00Z' },
      rationale: 'move it',
    });
    const onApplyProposal = vi.fn().mockResolvedValue(undefined);

    renderPanel(
      <AssistantPanel
        open
        onOpenChange={vi.fn()}
        onApplyProposal={onApplyProposal}
        onApplyAutoSchedule={vi.fn()}
      />,
    );

    const user = await openPanelAndPropose();
    await user.click(await screen.findByRole('button', { name: /apply/i }));

    await waitFor(() => {
      expect(onApplyProposal).toHaveBeenCalledWith('req-9', { startTs: '2026-03-02T09:00:00Z' });
    });
  });
});

describe('AssistantPanel failure reporting', () => {
  it('names the underlying error instead of swallowing it', async () => {
    // A turn once died with a bare "stopped unexpectedly" and no trace anywhere —
    // the catch discarded the exception. The entry must carry the cause.
    vi.mocked(streamAiChat).mockImplementationOnce(async function* () {
      yield { type: 'status' as const, phase: 'thinking', tool: null };
      throw new TypeError('network error');
    });

    renderPanel(<AssistantPanel open onOpenChange={vi.fn()} />);
    const user = userEvent.setup();
    await user.type(screen.getByPlaceholderText(/ask about your schedule/i), 'hi');
    await user.keyboard('{Enter}');

    const entry = await screen.findByText(/stopped unexpectedly \(TypeError: network error\)/i);
    expect(entry).toBeInTheDocument();
  });
});

describe('AssistantPanel view opening', () => {
  it('performs the navigation and records it in the log', async () => {
    vi.mocked(streamAiChat).mockImplementationOnce(async function* () {
      yield { type: 'ui' as const, view: 'insights_conflicts', entityId: null, siteId: null };
      yield { type: 'message' as const, text: 'The red bars are the overbooked weeks.' };
      yield { type: 'done' as const };
    });
    const onOpenView = vi.fn().mockReturnValue('Insights → Conflicts');

    renderPanel(<AssistantPanel open onOpenChange={vi.fn()} onOpenView={onOpenView} />);
    const user = userEvent.setup();
    await user.type(screen.getByPlaceholderText(/ask about your schedule/i), 'where are my conflicts');
    await user.keyboard('{Enter}');

    await waitFor(() => {
      expect(onOpenView).toHaveBeenCalledWith('insights_conflicts', null, null);
    });
    // Nothing moves silently: the person can see what the assistant did.
    expect(await screen.findByText(/Opened Insights → Conflicts/)).toBeInTheDocument();
  });

  it('passes the record id through for a single-record view', async () => {
    vi.mocked(streamAiChat).mockImplementationOnce(async function* () {
      yield { type: 'ui' as const, view: 'request', entityId: 'req-7', siteId: 'site-2' };
      yield { type: 'done' as const };
    });
    const onOpenView = vi.fn().mockReturnValue('request details');

    renderPanel(<AssistantPanel open onOpenChange={vi.fn()} onOpenView={onOpenView} />);
    const user = userEvent.setup();
    await user.type(screen.getByPlaceholderText(/ask about your schedule/i), 'open it');
    await user.keyboard('{Enter}');

    await waitFor(() => {
      expect(onOpenView).toHaveBeenCalledWith('request', 'req-7', 'site-2');
    });
  });

  it('says so rather than moving when the app cannot resolve the view', async () => {
    vi.mocked(streamAiChat).mockImplementationOnce(async function* () {
      yield { type: 'ui' as const, view: 'not_a_view', entityId: null, siteId: null };
      yield { type: 'done' as const };
    });
    const onOpenView = vi.fn().mockReturnValue(null);

    renderPanel(<AssistantPanel open onOpenChange={vi.fn()} onOpenView={onOpenView} />);
    const user = userEvent.setup();
    await user.type(screen.getByPlaceholderText(/ask about your schedule/i), 'go somewhere');
    await user.keyboard('{Enter}');

    expect(await screen.findByText(/does not have/i)).toBeInTheDocument();
  });

  it('ignores view events when the host supplies no handler', async () => {
    vi.mocked(streamAiChat).mockImplementationOnce(async function* () {
      yield { type: 'ui' as const, view: 'requests', entityId: null, siteId: null };
      yield { type: 'message' as const, text: 'here you go' };
      yield { type: 'done' as const };
    });

    renderPanel(<AssistantPanel open onOpenChange={vi.fn()} />);
    const user = userEvent.setup();
    await user.type(screen.getByPlaceholderText(/ask about your schedule/i), 'go');
    await user.keyboard('{Enter}');

    // The turn still completes; only the navigation is absent.
    expect(await screen.findByText('here you go')).toBeInTheDocument();
  });
});

describe('AssistantPanel conversation persistence', () => {
  beforeEach(() => {
    // Call history has to be cleared, not just implementations: an assertion that a
    // conversation was never opened would otherwise see the previous test's call.
    vi.clearAllMocks();
    vi.mocked(listAiConversations).mockResolvedValue([]);
    vi.mocked(saveAiConversation).mockResolvedValue(undefined);
    vi.mocked(deleteAiConversation).mockResolvedValue(undefined);
    vi.mocked(streamAiChat).mockImplementation(async function* () {
      yield { type: 'message' as const, text: 'Two requests overlap.' };
      yield { type: 'done' as const };
    });
  });

  it('saves the conversation once the turn is done', async () => {
    renderPanel(<AssistantPanel open onOpenChange={vi.fn()} />);
    const user = userEvent.setup();
    await user.type(screen.getByPlaceholderText(/ask about your schedule/i), 'any conflicts?');
    await user.keyboard('{Enter}');

    await waitFor(() => expect(saveAiConversation).toHaveBeenCalled());
    const [id, body] = vi.mocked(saveAiConversation).mock.calls[0];
    expect(id).toBeTruthy();
    // The title comes from what was asked, so the list reads like the conversation.
    expect(body.title).toBe('any conflicts?');
    expect(body.entries.some((e) => e.text === 'Two requests overlap.')).toBe(true);
  });

  it('restores the newest conversation when the panel opens empty', async () => {
    vi.mocked(listAiConversations).mockResolvedValue([
      { id: 'conv-1', title: 'Yesterday', updatedAt: '2026-08-24T10:00:00Z' },
    ]);
    vi.mocked(getAiConversation).mockResolvedValue({
      id: 'conv-1',
      title: 'Yesterday',
      updatedAt: '2026-08-24T10:00:00Z',
      entries: [{ kind: 'assistant', text: 'Where we left off.' }],
      transcript: [],
    });

    renderPanel(<AssistantPanel open onOpenChange={vi.fn()} />);

    expect(await screen.findByText('Where we left off.')).toBeInTheDocument();
  });

  it('a late-arriving list does not overwrite a conversation already started', async () => {
    // The list is fetched when the panel opens, but it can land after someone has already
    // asked something. Restoring then would replace what they are reading mid-thought.
    let releaseList: (v: { id: string; title: string; updatedAt: string }[]) => void = () => {};
    vi.mocked(listAiConversations).mockReturnValue(
      new Promise((resolve) => {
        releaseList = resolve;
      }),
    );

    renderPanel(<AssistantPanel open onOpenChange={vi.fn()} />);
    const user = userEvent.setup();
    await user.type(screen.getByPlaceholderText(/ask about your schedule/i), 'live question');
    await user.keyboard('{Enter}');
    await screen.findByText('Two requests overlap.');

    releaseList([{ id: 'conv-1', title: 'Older', updatedAt: '2026-08-24T10:00:00Z' }]);

    await waitFor(() => expect(listAiConversations).toHaveBeenCalled());
    expect(getAiConversation).not.toHaveBeenCalled();
    expect(screen.getByText('Two requests overlap.')).toBeInTheDocument();
  });

  it('starts a fresh conversation on request', async () => {
    renderPanel(<AssistantPanel open onOpenChange={vi.fn()} />);
    const user = userEvent.setup();
    await user.type(screen.getByPlaceholderText(/ask about your schedule/i), 'first');
    await user.keyboard('{Enter}');
    await screen.findByText('Two requests overlap.');

    await user.click(screen.getByRole('button', { name: /new conversation/i }));

    expect(screen.queryByText('Two requests overlap.')).not.toBeInTheDocument();
  });

  it('offers a way out when the server says the conversation is too long', async () => {
    // Without this the person is told to start a new conversation with no way to do it,
    // and the oversized transcript stays in state so every later send fails identically.
    vi.mocked(streamAiChat).mockImplementationOnce(async function* () {
      yield {
        type: 'error' as const,
        code: 'conversation_too_long',
        message: 'This conversation has grown too long. Start a new one to continue.',
      };
      yield { type: 'done' as const };
    });

    renderPanel(<AssistantPanel open onOpenChange={vi.fn()} />);
    const user = userEvent.setup();
    await user.type(screen.getByPlaceholderText(/ask about your schedule/i), 'and then?');
    await user.keyboard('{Enter}');

    const recover = await screen.findByRole('button', { name: /start a new conversation/i });
    await user.click(recover);

    expect(screen.queryByText(/grown too long/i)).not.toBeInTheDocument();
  });

  it('a save failure costs history, never the conversation', async () => {
    vi.mocked(saveAiConversation).mockRejectedValue(new Error('offline'));

    renderPanel(<AssistantPanel open onOpenChange={vi.fn()} />);
    const user = userEvent.setup();
    await user.type(screen.getByPlaceholderText(/ask about your schedule/i), 'still works?');
    await user.keyboard('{Enter}');

    expect(await screen.findByText('Two requests overlap.')).toBeInTheDocument();
  });

  it('deleting the open conversation clears the panel', async () => {
    vi.mocked(listAiConversations).mockResolvedValue([
      { id: 'conv-1', title: 'Yesterday', updatedAt: '2026-08-24T10:00:00Z' },
    ]);
    vi.mocked(getAiConversation).mockResolvedValue({
      id: 'conv-1',
      title: 'Yesterday',
      updatedAt: '2026-08-24T10:00:00Z',
      entries: [{ kind: 'assistant', text: 'Restored text.' }],
      transcript: [],
    });

    renderPanel(<AssistantPanel open onOpenChange={vi.fn()} />);
    const user = userEvent.setup();
    await screen.findByText('Restored text.');

    await user.click(screen.getByRole('button', { name: /saved conversations/i }));
    // Delete is its own menu item — a menuitem may not contain a focusable button, and a
    // nested one was unreachable by keyboard.
    await user.click(await screen.findByRole('menuitem', { name: /delete yesterday/i }));

    await waitFor(() => expect(deleteAiConversation).toHaveBeenCalledWith('conv-1'));
    await waitFor(() => expect(screen.queryByText('Restored text.')).not.toBeInTheDocument());
  });
});
