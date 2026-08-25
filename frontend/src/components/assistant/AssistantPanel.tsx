import { useCallback, useEffect, useRef, useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { Bot, GripVertical, History, Loader2, Plus, Send, Trash2 } from "lucide-react";
import { Button } from "@foundation/src/components/ui/button";
import { Input } from "@foundation/src/components/ui/input";
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
} from "@foundation/src/components/ui/sheet";
import { useCanEdit } from "@foundation/src/hooks/usePermissions";
import { useAiStatus } from "@foundation/src/hooks/useAiAssistant";
import {
  deleteAiConversation,
  getAiConversation,
  listAiConversations,
  saveAiConversation,
  streamAiChat,
  type AiEntry,
  type AiMessage,
  type AiProposal,
} from "@foundation/src/lib/api/ai-api";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@foundation/src/components/ui/dropdown-menu";
import { qk } from "@foundation/src/lib/api/query-keys";
import { randomId } from "@foundation/src/lib/core/ids";
import { ProposalCard, proposalToAutoScheduleRequestIds, proposalToRequestUpdate } from "./ProposalCard";
import { logger } from "@foundation/src/lib/core/logger";
import { useAppStore } from "@foundation/src/store/app-store";
import { useBreakpoint } from "@foundation/src/hooks/useBreakpoint";
import {
  usePanelWidth,
  MIN_PANEL_WIDTH,
  MAX_PANEL_WIDTH,
} from "@foundation/src/hooks/usePanelWidth";

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
interface ProposalAcceptors {
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

/** Whether this proposal has a usable accept path — see {@link acceptorFor}. */
function canApplyProposal(proposal: { kind: string; input: string }, handlers: ProposalAcceptors): boolean {
  return acceptorFor(proposal, handlers) !== null;
}

export interface AssistantPanelProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  context?: AssistantContext | null;
  /**
   * Applies a proposed change through the normal write path. Injected rather than called
   * directly so the panel stays unaware of request-editing specifics — and so the write
   * keeps going through the endpoint the person's own role already governs.
   */
  onApplyProposal?: (requestId: string, changes: Record<string, unknown>) => Promise<void>;
  /**
   * Accepts an auto-scheduling proposal. Injected for the same reason as
   * {@link onApplyProposal}: the panel knows a set of requests was approved, not how the
   * scheduling page previews them. Accepting does not schedule anything — the host opens
   * the ordinary preview, and the person applies from there.
   */
  onApplyAutoSchedule?: (requestIds: string[]) => Promise<void>;
  /**
   * Takes the person to a view the assistant named. Injected for the same reason as the
   * apply handlers: the panel knows a view was asked for, not how the app routes. Returns
   * the label to show in the log, or null when this client cannot resolve the view.
   */
  onOpenView?: (view: string, entityId: string | null, siteId: string | null) => string | null;
}

/**
 * One line in the panel's visible history — the same shape the server stores, so a
 * conversation restores into exactly what it was. Kinds: `user`, `assistant`, `action`
 * (something the assistant did to the screen, recorded so nothing moves silently), and
 * `error`.
 */
type Entry = AiEntry;

/**
 * The assistant conversation.
 *
 * The turn stays stateless: the transcript comes back with every turn and is echoed on
 * the next one, and the server never reads storage while answering. Conversations are
 * saved alongside through their own endpoints, so losing a save costs history, never an
 * answer. Closing the panel aborts the in-flight turn,
 * which stops the server spending tokens on an answer nobody will read.
 */
export function AssistantPanel({
  open,
  onOpenChange,
  context,
  onApplyProposal,
  onApplyAutoSchedule,
  onOpenView,
}: AssistantPanelProps) {
  const canEdit = useCanEdit();
  // The site decides which zone "tomorrow morning" means; the turn is useless at guessing.
  const selectedSiteId = useAppStore((s) => s.selectedSiteId);
  const { data: status } = useAiStatus(open);

  const [entries, setEntries] = useState<Entry[]>([]);
  const [transcript, setTranscript] = useState<AiMessage[]>([]);
  const [proposal, setProposal] = useState<AiProposal | null>(null);
  const [phase, setPhase] = useState<string | null>(null);
  const [input, setInput] = useState("");
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

  // On a phone the panel is the whole screen, so there is nothing to drag it against.
  const { isPhone } = useBreakpoint();
  const { width, isDragging, onPointerDown, onKeyDown } = usePanelWidth("orkyo.assistant.width");

  const abortRef = useRef<AbortController | null>(null);
  const inputRef = useRef<HTMLInputElement>(null);
  const seededFor = useRef<string | null>(null);

  const queryClient = useQueryClient();

  // Titles only; a body is fetched when a conversation is actually opened.
  const { data: conversations = [] } = useQuery({
    queryKey: qk.ai.conversations(),
    queryFn: listAiConversations,
    enabled: open,
  });

  /**
   * What is already stored. Saving is driven by state changing, so without this a restore
   * would immediately write back what it just read. Compared by reference: entries and
   * transcript are always replaced, never mutated, so identity is exact — where counting
   * lengths would miss a same-length replacement.
   */
  const saved = useRef<{ id: string; entries: Entry[]; transcript: AiMessage[] } | null>(null);
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
  }, []);

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
  useEffect(() => {
    if (busy || entries.length === 0) return;

    const last = saved.current;
    if (last?.id === conversationId && last.entries === entries && last.transcript === transcript) return;
    saved.current = { id: conversationId, entries, transcript };

    const firstAsked = entries.find((e) => e.kind === "user")?.text ?? "Conversation";
    void saveAiConversation(conversationId, {
      title: firstAsked.slice(0, 120),
      entries,
      transcript,
    })
      .then(() => queryClient.invalidateQueries({ queryKey: qk.ai.conversations() }))
      .catch((err: unknown) => logger.error("Could not save the conversation", err));
  }, [busy, entries, transcript, conversationId, queryClient]);

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

  // Opening from a conflict asks the first question on the person's behalf, once.
  useEffect(() => {
    if (!open || !context) return;
    const key = `${context.requestId}:${context.kind ?? ""}`;
    if (seededFor.current === key) return;

    // A conflict opens its own conversation. Clearing the entries alone would leave the
    // previous conversation's id in place, and the save that follows this turn would
    // overwrite that conversation with this one.
    startNewConversation();
    seededFor.current = key;
    void runTurn(undefined, { context, transcript: [] });
    // runTurn changes with the transcript; seeding must happen only on a new context.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, context]);

  // Closing the panel cancels the turn rather than leaving it running unseen.
  useEffect(() => {
    if (!open) abortRef.current?.abort();
  }, [open]);

  const handleDeleteConversation = useCallback(async (id: string) => {
    try {
      await deleteAiConversation(id);
      await queryClient.invalidateQueries({ queryKey: qk.ai.conversations() });
      // Deleting the conversation on screen leaves nothing to write back to.
      if (id === conversationId) startNewConversation();
    } catch (err) {
      logger.error("Could not delete the conversation", err);
    }
  }, [conversationId, queryClient, startNewConversation]);

  const handleSend = async () => {
    const text = input.trim();
    if (!text || busy) return;
    setInput("");
    setEntries((prev) => [...prev, { kind: "user", text }]);
    await runTurn(text);
  };

  const handleApply = async () => {
    if (!proposal) return;
    // Each proposal kind has its own accept path, and a kind whose host handler is absent
    // is not offered an Apply button at all — see canApplyProposal.
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

  const handleDecline = async () => {
    if (!proposal) return;
    const toolUseId = proposal.toolUseId;
    setProposal(null);
    await runTurn(undefined, { pendingToolResult: { toolUseId, status: "declined" } });
  };

  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent
        side="right"
        // The resize handle is the first tabbable node inside the panel, so Radix would
        // hand it the focus and leave the person tabbing before they can type.
        onOpenAutoFocus={(e) => {
          e.preventDefault();
          inputRef.current?.focus();
        }}
        // The utility width is dropped above the phone breakpoint so the inline width
        // wins; below it the panel stays full-bleed. `md` matches useBreakpoint's phone
        // boundary — the old `sm:` prefix disagreed with the hook by 128px.
        // max-w-none unprefixed: the sheet's own variant caps at sm:max-w-sm, so between
        // 640 and 767px the panel was 384px wide while claiming to be full width.
        className="w-full max-w-none md:max-w-none flex flex-col gap-0 p-0"
        style={isPhone ? undefined : { width }}
      >
        {!isPhone && (
          <div
            role="separator"
            aria-orientation="vertical"
            aria-label="Resize assistant panel"
            aria-valuenow={width}
            aria-valuemin={MIN_PANEL_WIDTH}
            aria-valuemax={MAX_PANEL_WIDTH}
            tabIndex={0}
            onPointerDown={onPointerDown}
            onKeyDown={onKeyDown}
            className={`absolute inset-y-0 left-0 w-1 cursor-ew-resize group flex items-center justify-center touch-none ${
              isDragging ? "bg-primary" : "bg-border hover:bg-primary"
            }`}
          >
            {/* The icon is wider than the 1px bar, so it is only painted on hover —
                otherwise it sits over the message text and eats clicks there. */}
            <GripVertical className="h-4 w-4 text-muted-foreground absolute opacity-0 group-hover:opacity-100 group-hover:text-primary pointer-events-none" />
          </div>
        )}
        <SheetHeader className="border-b p-4">
          <SheetTitle className="flex items-center gap-2">
            <Bot className="h-4 w-4" />
            <span className="flex-1">Assistant</span>

            <Button
              variant="ghost"
              size="icon"
              className="h-7 w-7"
              onClick={startNewConversation}
              aria-label="New conversation"
              title="New conversation"
            >
              <Plus className="h-4 w-4" />
            </Button>

            <DropdownMenu>
              <DropdownMenuTrigger asChild>
                <Button
                  variant="ghost"
                  size="icon"
                  className="h-7 w-7"
                  aria-label="Saved conversations"
                  title="Saved conversations"
                >
                  <History className="h-4 w-4" />
                </Button>
              </DropdownMenuTrigger>
              <DropdownMenuContent align="end" className="w-72">
                {conversations.length === 0 ? (
                  <DropdownMenuItem disabled>Nothing saved yet</DropdownMenuItem>
                ) : (
                  // Open and delete are two menu items, not a button nested inside one: a
                  // menuitem must not contain focusable children, and Radix's roving
                  // tabindex made the nested button unreachable by keyboard entirely.
                  conversations.map((saved) => (
                    <div key={saved.id} className="flex items-center">
                      <DropdownMenuItem
                        className="flex-1 truncate"
                        onSelect={() => void openConversation(saved.id)}
                      >
                        {saved.title}
                      </DropdownMenuItem>
                      <DropdownMenuItem
                        aria-label={`Delete ${saved.title}`}
                        className="text-muted-foreground focus:text-destructive"
                        onSelect={() => void handleDeleteConversation(saved.id)}
                      >
                        <Trash2 className="h-3.5 w-3.5" />
                      </DropdownMenuItem>
                    </div>
                  ))
                )}
                <DropdownMenuSeparator />
                <DropdownMenuItem onSelect={startNewConversation}>New conversation</DropdownMenuItem>
              </DropdownMenuContent>
            </DropdownMenu>
          </SheetTitle>
          <SheetDescription>
            {status?.monthlyTokenLimit != null
              ? `${status.usedTotalTokens.toLocaleString()} of ${status.monthlyTokenLimit.toLocaleString()} tokens used this month.`
              : "Ask about your schedule, resources, and conflicts."}
          </SheetDescription>
        </SheetHeader>

        <div
          className="flex-1 overflow-y-auto p-4 space-y-3"
          role="log"
          aria-live="polite"
          aria-label="Assistant conversation"
        >
          {entries.length === 0 && !busy && (
            <p className="text-sm text-muted-foreground">
              Ask a question, for example “which requests are in conflict this week?”
            </p>
          )}

          {entries.map((entry, index) => (
            <div
              key={index}
              className={
                entry.kind === "user"
                  ? "text-sm bg-muted rounded-lg p-2 ml-8"
                  : entry.kind === "error"
                    ? "text-sm text-destructive"
                    : entry.kind === "action"
                      ? "text-xs text-muted-foreground italic"
                      : "text-sm whitespace-pre-wrap"
              }
            >
              {entry.text}
            </div>
          ))}

          {notice && <p className="text-sm text-destructive">{notice}</p>}

          {tooLong && (
            <Button variant="outline" size="sm" onClick={startNewConversation}>
              Start a new conversation
            </Button>
          )}

          {proposal && (
            <ProposalCard
              proposal={proposal}
              canApply={canEdit && canApplyProposal(proposal, { onApplyProposal, onApplyAutoSchedule })}
              isApplying={applying}
              onApply={handleApply}
              onDecline={handleDecline}
            />
          )}

          {busy && phase && (
            <p className="text-sm text-muted-foreground flex items-center gap-2">
              <Loader2 className="h-3 w-3 animate-spin" />
              {phase}…
            </p>
          )}
        </div>

        <div className="border-t p-3 flex gap-2">
          <Input
            ref={inputRef}
            value={input}
            onChange={(e) => setInput(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === "Enter" && !e.shiftKey) {
                e.preventDefault();
                void handleSend();
              }
            }}
            placeholder="Ask about your schedule"
            aria-label="Message the assistant"
            disabled={busy}
          />
          <Button onClick={() => void handleSend()} disabled={busy || !input.trim()} size="icon">
            <Send className="h-4 w-4" />
            <span className="sr-only">Send</span>
          </Button>
        </div>
      </SheetContent>
    </Sheet>
  );
}
