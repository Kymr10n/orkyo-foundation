import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

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
}));

vi.mock('@foundation/src/hooks/useCanEdit', () => ({ useCanEdit: () => true }));
vi.mock('@foundation/src/hooks/useAiAssistant', () => ({
  useAiStatus: () => ({ data: { enabled: true, remainingTokens: 1000 } }),
}));

import { AssistantPanel } from './AssistantPanel';
import { streamAiChat } from '@foundation/src/lib/api/ai-api';

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

    render(
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
    render(<AssistantPanel open onOpenChange={vi.fn()} onApplyProposal={vi.fn()} />);

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

    render(
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

    render(<AssistantPanel open onOpenChange={vi.fn()} />);
    const user = userEvent.setup();
    await user.type(screen.getByPlaceholderText(/ask about your schedule/i), 'hi');
    await user.keyboard('{Enter}');

    const entry = await screen.findByText(/stopped unexpectedly \(TypeError: network error\)/i);
    expect(entry).toBeInTheDocument();
  });
});
