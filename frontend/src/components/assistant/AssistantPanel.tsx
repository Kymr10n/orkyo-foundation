import { useCallback, useEffect, useRef, useState } from "react";
import { Bot, Loader2, Send } from "lucide-react";
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
import { streamAiChat, type AiMessage, type AiProposal } from "@foundation/src/lib/api/ai-api";
import { ProposalCard, proposalToAutoScheduleRequestIds, proposalToRequestUpdate } from "./ProposalCard";
import { logger } from "@foundation/src/lib/core/logger";

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
}

/** One line in the panel's visible history. */
type Entry =
  | { kind: "user"; text: string }
  | { kind: "assistant"; text: string }
  | { kind: "error"; text: string };

/**
 * The assistant conversation.
 *
 * The conversation lives here rather than on the server: the transcript comes back with
 * every turn and is echoed on the next one. Closing the panel aborts the in-flight turn,
 * which stops the server spending tokens on an answer nobody will read.
 */
export function AssistantPanel({
  open,
  onOpenChange,
  context,
  onApplyProposal,
  onApplyAutoSchedule,
}: AssistantPanelProps) {
  const canEdit = useCanEdit();
  const { data: status } = useAiStatus(open);

  const [entries, setEntries] = useState<Entry[]>([]);
  const [transcript, setTranscript] = useState<AiMessage[]>([]);
  const [proposal, setProposal] = useState<AiProposal | null>(null);
  const [phase, setPhase] = useState<string | null>(null);
  const [input, setInput] = useState("");
  const [busy, setBusy] = useState(false);
  const [applying, setApplying] = useState(false);

  const abortRef = useRef<AbortController | null>(null);
  const seededFor = useRef<string | null>(null);

  const runTurn = useCallback(
    async (
      message: string | undefined,
      opts?: {
        context?: AssistantContext;
        pendingToolResult?: { toolUseId: string; status: "applied" | "declined" | "failed"; detail?: string };
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
            transcript,
            context: opts?.context
              ? { type: "conflict", requestId: opts.context.requestId, kind: opts.context.kind }
              : undefined,
            pendingToolResult: opts?.pendingToolResult,
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
            case "error":
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
        setBusy(false);
        setPhase(null);
      }
    },
    [transcript]
  );

  // Opening from a conflict asks the first question on the person's behalf, once.
  useEffect(() => {
    if (!open || !context) return;
    const key = `${context.requestId}:${context.kind ?? ""}`;
    if (seededFor.current === key) return;
    seededFor.current = key;

    setEntries([]);
    setTranscript([]);
    void runTurn(undefined, { context });
    // runTurn changes with the transcript; seeding must happen only on a new context.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, context]);

  // Closing the panel cancels the turn rather than leaving it running unseen.
  useEffect(() => {
    if (!open) abortRef.current?.abort();
  }, [open]);

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
      <SheetContent side="right" className="w-full sm:max-w-md flex flex-col gap-0 p-0">
        <SheetHeader className="border-b p-4">
          <SheetTitle className="flex items-center gap-2">
            <Bot className="h-4 w-4" />
            Assistant
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
                    : "text-sm whitespace-pre-wrap"
              }
            >
              {entry.text}
            </div>
          ))}

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
