import { useCallback, useEffect, useEffectEvent, useRef, useState } from "react";
import {
  useAiConversations,
  useAiStatus,
  useDeleteAiConversation,
  useFetchAiConversation,
  useInvalidateAiStatus,
  useSaveAiConversation,
} from "@foundation/src/hooks/useAiAssistant";
import {
  streamAiChat,
  type AiEntry,
  type AiMessage,
  type AiProposal,
} from "@foundation/src/lib/api/ai-api";
import { randomId } from "@foundation/src/lib/core/ids";
import { logger } from "@foundation/src/lib/core/logger";
import { useSiteStore } from "@foundation/src/store/site-store";
import {
  proposalToAutoScheduleRequestIds,
  proposalToRequestUpdate,
} from "@foundation/src/domain/ai-proposal";

/** Where the assistant was opened from, when that should shape the first question. */
export interface AssistantContext {
  type: "conflict";
  requestId: string;
  kind?: string;
}

/** The proposal kinds the panel knows how to accept, as the backend names them. */
const PROPOSE_UPDATE_REQUEST = "propose_update_request";
const PROPOSE_AUTO_SCHEDULE = "propose_auto_schedule";

/** The host handlers a proposal can be routed to. */
export interface ProposalAcceptors {
  onApplyProposal?: (requestId: string, changes: Record<string, unknown>) => Promise<void>;
  onApplyAutoSchedule?: (requestIds: string[]) => Promise<void>;
}

/**
 * The action that accepts this proposal, or null when there is none — either the host did
 * not supply a handler, or the proposal's payload is not usable.
 *
 * Returning null is what the Apply button is gated on, so a kind that cannot be accepted
 * never shows a button that does nothing.
 */
function acceptorFor(
  proposal: { kind: string; input: string },
  handlers: ProposalAcceptors,
): (() => Promise<void>) | null {
  if (proposal.kind === PROPOSE_UPDATE_REQUEST) {
    const { requestId, changes } = proposalToRequestUpdate(proposal.input);
    if (!requestId || !handlers.onApplyProposal) return null;
    const apply = handlers.onApplyProposal;
    return () => apply(requestId, changes);
  }

  if (proposal.kind === PROPOSE_AUTO_SCHEDULE) {
    const requestIds = proposalToAutoScheduleRequestIds(proposal.input);
    if (requestIds.length === 0 || !handlers.onApplyAutoSchedule) return null;
    const apply = handlers.onApplyAutoSchedule;
    return () => apply(requestIds);
  }

  return null;
}

export interface UseAssistantConversationOptions extends ProposalAcceptors {
  open: boolean;
  context?: AssistantContext | null;
  /**
   * Takes the person to a view the assistant named. Returns the label to show in the log, or
   * null when this client cannot resolve the view.
   */
  onOpenView?: (view: string, entityId: string | null, siteId: string | null) => string | null;
}

/**
 * The assistant conversation: its entries and transcript, the turn, the proposal and its
 * accept/decline, and the saved-conversation list (open, save, delete).
 *
 * The turn stays stateless: the transcript comes back with every turn and is echoed on
 * the next one, and the server never reads storage while answering. Conversations are
 * saved alongside through their own endpoints, so losing a save costs history, never an
 * answer. Closing the panel aborts the in-flight turn, which stops the server spending
 * tokens on an answer nobody will read.
 *
 * `streamAiChat` is the one direct API call: an SSE stream read event by event, which a
 * query or a mutation cannot hold.
 */
export function useAssistantConversation({
  open,
  context,
  onApplyProposal,
  onApplyAutoSchedule,
  onOpenView,
}: UseAssistantConversationOptions) {
  // The site decides which zone "tomorrow morning" means; the turn is useless at guessing.
  const selectedSiteId = useSiteStore((s) => s.selectedSiteId);
  const { data: status } = useAiStatus(open);

  const [entries, setEntries] = useState<AiEntry[]>([]);
  const [transcript, setTranscript] = useState<AiMessage[]>([]);
  const [proposal, setProposal] = useState<AiProposal | null>(null);
  const [phase, setPhase] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [applying, setApplying] = useState(false);
  /**
   * Which conversation is being written. Generated here so a retry after a failed save
   * rewrites the same row instead of leaving a duplicate behind.
   */
  const [conversationId, setConversationId] = useState(() => randomId());
  /** Set when the server refuses the transcript, so the log can offer a way out. */
  const [tooLong, setTooLong] = useState(false);
  /** A transient panel-level message. Never part of the conversation, so never saved. */
  const [notice, setNotice] = useState<string | null>(null);
  /** Set when the workspace's daily interaction limit stopped this turn. */
  const [dailyLimitReached, setDailyLimitReached] = useState(false);

  /**
   * Whether the limit still holds. The local flag is what a refused turn sets, but the
   * server is the authority: after midnight UTC, or after an administrator raises the
   * ceiling, a status refetch reports headroom again and the composer has to come back.
   * Without this the header could read "remaining: 40" beside an input nobody can type in.
   */
  const outOfInteractions =
    dailyLimitReached &&
    !(status?.dailyTurnLimit != null && status.usedTurnsToday < status.dailyTurnLimit);

  const abortRef = useRef<AbortController | null>(null);
  const seededFor = useRef<string | null>(null);

  const invalidateAiStatus = useInvalidateAiStatus();
  const getAiConversation = useFetchAiConversation();
  const { mutateAsync: saveAiConversation } = useSaveAiConversation();
  const { mutateAsync: deleteAiConversation } = useDeleteAiConversation();

  // Titles only; a body is fetched when a conversation is actually opened.
  const { data: conversations = [] } = useAiConversations(open);

  /**
   * What is already stored. Saving is driven by state changing, so without this a restore
   * would immediately write back what it just read. Compared by reference: entries and
   * transcript are always replaced, never mutated, so identity is exact — where counting
   * lengths would miss a same-length replacement.
   */
  const saved = useRef<{ id: string; entries: AiEntry[]; transcript: AiMessage[] } | null>(null);
  const restoredOnce = useRef(false);

  const startNewConversation = useCallback(() => {
    abortRef.current?.abort();
    const id = randomId();
    setConversationId(id);
    setEntries([]);
    setTranscript([]);
    setProposal(null);
    setTooLong(false);
    setNotice(null);
    // Deliberately NOT clearing dailyLimitReached: a new conversation does not grant new
    // interactions, and hiding the message would invite a send that fails again.
    // A fresh conversation has nothing stored yet, and seeding belongs to the context
    // that opened the panel, not to whatever was on screen before.
    saved.current = { id, entries: [], transcript: [] };
    seededFor.current = null;
  }, []);

  const openConversation = useCallback(async (id: string) => {
    try {
      const stored = await getAiConversation(id);
      abortRef.current?.abort();
      setConversationId(stored.id);
      setEntries(stored.entries);
      setTranscript(stored.transcript);
      // A proposal's toolUseId belongs to a turn the model can no longer see, so applying
      // a restored one would answer a question nobody asked.
      setProposal(null);
      setTooLong(false);
      setNotice(null);
      saved.current = { id: stored.id, entries: stored.entries, transcript: stored.transcript };
      // Restored conflict threads must not be seeded a second time.
      seededFor.current = "restored";
    } catch (err) {
      // Deliberately not an entry: entries are saved, so a transient network failure
      // would be written into the stored history of a conversation it has nothing to do
      // with. This is about the panel, not the conversation.
      logger.error("Could not open the saved conversation", err);
      setNotice("That conversation could not be opened.");
    }
  }, [getAiConversation]);

  const runTurn = useCallback(
    async (
      message: string | undefined,
      opts?: {
        context?: AssistantContext;
        pendingToolResult?: { toolUseId: string; status: "applied" | "declined" | "failed"; detail?: string };
        /**
         * The history to send, when the caller knows it better than this closure does.
         * Seeding clears the transcript and starts a turn in the same tick, so the
         * closure still holds the previous conversation's history at that moment.
         */
        transcript?: AiMessage[];
      }
    ) => {
      abortRef.current?.abort();
      const controller = new AbortController();
      abortRef.current = controller;

      setBusy(true);
      setProposal(null);
      setPhase("thinking");

      try {
        for await (const event of streamAiChat(
          {
            message,
            transcript: opts?.transcript ?? transcript,
            context: opts?.context
              ? { type: "conflict", requestId: opts.context.requestId, kind: opts.context.kind }
              : undefined,
            pendingToolResult: opts?.pendingToolResult,
            siteId: selectedSiteId ?? undefined,
          },
          controller.signal
        )) {
          switch (event.type) {
            case "status":
              setPhase(event.tool ? `Looking up ${event.tool.replace(/_/g, " ")}` : "Thinking");
              break;
            case "message":
              setEntries((prev) => [...prev, { kind: "assistant", text: event.text }]);
              break;
            case "proposal":
              setProposal(event.proposal);
              break;
            case "transcript":
              setTranscript(event.messages);
              break;
            case "ui": {
              // Performed here, once, as the event arrives — not in an effect keyed on
              // state. An effect would re-fire when the router hands back a new identity
              // on a redirecting route, which is the loop the tour had to be rescued from.
              const label = onOpenView?.(event.view, event.entityId, event.siteId) ?? null;
              setEntries((prev) => [
                ...prev,
                label
                  ? { kind: "action", text: `Opened ${label}` }
                  : { kind: "error", text: "The assistant tried to open something this app does not have." },
              ]);
              break;
            }
            case "error":
              // The server tells the person to start a new conversation; without this the
              // panel had no way to offer one, and the oversized transcript stayed in
              // state so every later send failed the same way.
              if (event.code === "conversation_too_long") setTooLong(true);
              if (event.code === "daily_limit_reached" || event.code === "workspace_daily_limit_reached")
                setDailyLimitReached(true);
              setEntries((prev) => [...prev, { kind: "error", text: event.message }]);
              break;
            case "done":
              break;
          }
        }
      } catch (err) {
        // An abort is the person closing the panel, not a failure worth reporting.
        if (!controller.signal.aborted) {
          // The cause matters: a swallowed error here once cost a whole debugging
          // session. Log it in full and name it in the visible entry.
          logger.error("Assistant turn failed", err);
          const reason =
            err instanceof Error ? ` (${err.name}: ${err.message})`.slice(0, 140) : "";
          setEntries((prev) => [
            ...prev,
            { kind: "error", text: `The assistant stopped unexpectedly${reason}. Try again.` },
          ]);
        }
      } finally {
        // Only the turn that is still current may clear these. An aborted turn settles a
        // moment after its replacement started, and would otherwise re-enable the input
        // and let the save effect fire in the middle of the new turn.
        if (abortRef.current === controller) {
          setBusy(false);
          setPhase(null);
        }
      }
    },
    [transcript, onOpenView, selectedSiteId]
  );

  // Saving follows a finished turn rather than each event: mid-turn state is not worth
  // storing, and a turn writes its conversation once. Failures are logged and dropped —
  // storage is a notebook beside the conversation, never a condition of having one.
  // The header counts interactions down, so it has to be re-read once a turn has used one.
  // Nothing needed this before: the token figure it used to show moved too slowly to notice.
  useEffect(() => {
    if (busy || entries.length === 0) return;
    void invalidateAiStatus();
  }, [busy, entries.length, invalidateAiStatus]);

  useEffect(() => {
    if (busy || entries.length === 0) return;

    const last = saved.current;
    if (last?.id === conversationId && last.entries === entries && last.transcript === transcript) return;
    saved.current = { id: conversationId, entries, transcript };

    const firstAsked = entries.find((e) => e.kind === "user")?.text ?? "Conversation";
    void saveAiConversation({
      id: conversationId,
      title: firstAsked.slice(0, 120),
      entries,
      transcript,
    }).catch((err: unknown) => logger.error("Could not save the conversation", err));
  }, [busy, entries, transcript, conversationId, saveAiConversation]);

  // Reopening the panel picks up where the person left off. Only once, and only into an
  // empty panel: a conversation already on screen is the one they want.
  useEffect(() => {
    if (!open || restoredOnce.current) return;
    if (context) return; // opened about a conflict — that seeds its own conversation
    if (entries.length > 0 || conversations.length === 0) return;
    restoredOnce.current = true;
    // openConversation awaits the fetch before it touches state, so nothing is set
    // synchronously here — the rule cannot see through the async call.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void openConversation(conversations[0].id);
  }, [open, context, entries.length, conversations, openConversation]);

  // Opening from a conflict asks the first question on the person's behalf, once. An effect
  // event, because runTurn changes with the transcript and seeding must happen only on a new
  // context.
  const seed = useEffectEvent((seedContext: AssistantContext) => {
    const key = `${seedContext.requestId}:${seedContext.kind ?? ""}`;
    if (seededFor.current === key) return;

    // A conflict opens its own conversation. Clearing the entries alone would leave the
    // previous conversation's id in place, and the save that follows this turn would
    // overwrite that conversation with this one.
    startNewConversation();
    seededFor.current = key;
    void runTurn(undefined, { context: seedContext, transcript: [] });
  });

  useEffect(() => {
    if (open && context) seed(context);
  }, [open, context]);

  // Closing the panel cancels the turn rather than leaving it running unseen.
  useEffect(() => {
    if (!open) abortRef.current?.abort();
  }, [open]);

  const deleteConversation = useCallback(async (id: string) => {
    try {
      await deleteAiConversation(id);
      // Deleting the conversation on screen leaves nothing to write back to.
      if (id === conversationId) startNewConversation();
    } catch (err) {
      logger.error("Could not delete the conversation", err);
    }
  }, [conversationId, deleteAiConversation, startNewConversation]);

  const send = async (text: string) => {
    if (!text || busy) return;
    setEntries((prev) => [...prev, { kind: "user", text }]);
    await runTurn(text);
  };

  const applyProposal = async () => {
    if (!proposal) return;
    // Each proposal kind has its own accept path, and a kind whose host handler is absent
    // is not offered an Apply button at all — see proposalCanApply.
    const accept = acceptorFor(proposal, { onApplyProposal, onApplyAutoSchedule });
    if (!accept) return;

    setApplying(true);
    let outcome: { status: "applied" | "failed"; detail?: string };
    try {
      await accept();
      outcome = { status: "applied" };
    } catch (err) {
      logger.error("Assistant proposal apply failed", err);
      outcome = { status: "failed", detail: err instanceof Error ? err.message : undefined };
    } finally {
      setApplying(false);
    }

    const toolUseId = proposal.toolUseId;
    setProposal(null);
    await runTurn(undefined, { pendingToolResult: { toolUseId, ...outcome } });
  };

  const declineProposal = async () => {
    if (!proposal) return;
    const toolUseId = proposal.toolUseId;
    setProposal(null);
    await runTurn(undefined, { pendingToolResult: { toolUseId, status: "declined" } });
  };


  return {
    status,
    conversations,
    entries,
    proposal,
    /** Whether the current proposal has an accept path — see {@link acceptorFor}. */
    proposalCanApply: proposal ? acceptorFor(proposal, { onApplyProposal, onApplyAutoSchedule }) !== null : false,
    phase,
    busy,
    applying,
    tooLong,
    notice,
    dailyLimitReached,
    outOfInteractions,
    startNewConversation,
    openConversation,
    deleteConversation,
    send,
    applyProposal,
    declineProposal,
  };
}
