import { useRef, useState } from "react";
import { STORAGE_KEYS } from "@foundation/src/constants/storage";
import { LoadingSpinner } from "@foundation/src/components/ui/LoadingSpinner";
import { Bot, GripVertical, History, Plus, Send, Trash2 } from "lucide-react";
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
import {
  useAssistantConversation,
  type AssistantContext,
} from "@foundation/src/hooks/useAssistantConversation";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@foundation/src/components/ui/dropdown-menu";
import { ProposalCard } from "./ProposalCard";
import { isEphemeralSession } from "@foundation/src/lib/utils/session-end";
import { getApexOrigin } from "@foundation/src/lib/utils/tenant-navigation";
import { useBreakpoint } from "@foundation/src/hooks/useBreakpoint";
import {
  usePanelWidth,
  MIN_PANEL_WIDTH,
  MAX_PANEL_WIDTH,
} from "@foundation/src/hooks/usePanelWidth";

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
 * The assistant panel. The conversation itself — turns, proposals, saved conversations —
 * lives in `useAssistantConversation`; this component renders it.
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
  const conversation = useAssistantConversation({
    open,
    context,
    onApplyProposal,
    onApplyAutoSchedule,
    onOpenView,
  });
  const {
    status, conversations, entries, proposal, proposalCanApply, phase, busy, applying,
    tooLong, notice, dailyLimitReached, outOfInteractions, startNewConversation,
  } = conversation;

  const [input, setInput] = useState("");
  const inputRef = useRef<HTMLInputElement>(null);

  // On a phone the panel is the whole screen, so there is nothing to drag it against.
  const { isPhone } = useBreakpoint();
  const { width, isDragging, onPointerDown, onKeyDown } = usePanelWidth(STORAGE_KEYS.ASSISTANT_WIDTH);

  const handleSend = async () => {
    const text = input.trim();
    if (!text || busy) return;
    setInput("");
    await conversation.send(text);
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
          {/* pr-6 keeps the actions clear of the sheet's own close button, which Radix
              positions absolutely at right-4 and is therefore not in this flex row. */}
          <SheetTitle className="flex items-center gap-2 pr-6">
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
                        onSelect={() => void conversation.openConversation(saved.id)}
                      >
                        {saved.title}
                      </DropdownMenuItem>
                      <DropdownMenuItem
                        aria-label={`Delete ${saved.title}`}
                        className="text-muted-foreground focus:text-destructive"
                        onSelect={() => void conversation.deleteConversation(saved.id)}
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
            {status?.dailyTurnLimit != null
              ? // Names the limit when it is the workspace's, because the same number means
                // something different then: everyone shares it, and it can fall while you
                // are not using the assistant at all.
                `AI interactions remaining: ${Math.max(0, status.dailyTurnLimit - status.usedTurnsToday)}${
                  status.dailyLimitIsWorkspaceWide ? " (whole organization)" : ""
                }`
              : status?.monthlyTokenLimit != null
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

          {dailyLimitReached && isEphemeralSession() && (
            // Only for a visitor who never had an account: an account holder cannot act on
            // "request a guided demonstration", and telling them to would be noise.
            <p className="text-sm">
              Demo AI limit reached. Try again tomorrow or{" "}
              <a
                className="underline"
                href={`${getApexOrigin()}/contact`}
                target="_blank"
                rel="noreferrer"
              >
                request a guided demonstration
              </a>
              .
            </p>
          )}

          {tooLong && (
            <Button variant="outline" size="sm" onClick={startNewConversation}>
              Start a new conversation
            </Button>
          )}

          {proposal && (
            <ProposalCard
              proposal={proposal}
              canApply={canEdit && proposalCanApply}
              isApplying={applying}
              onApply={() => void conversation.applyProposal()}
              onDecline={() => void conversation.declineProposal()}
            />
          )}

          {busy && phase && (
            <LoadingSpinner inline size="xs" muted message={`${phase}…`} />
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
            disabled={busy || outOfInteractions}
          />
          <Button
            onClick={() => void handleSend()}
            disabled={busy || outOfInteractions || !input.trim()}
            size="icon"
          >
            <Send className="h-4 w-4" />
            <span className="sr-only">Send</span>
          </Button>
        </div>
      </SheetContent>
    </Sheet>
  );
}
