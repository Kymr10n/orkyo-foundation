import { Check, Loader2, X } from "lucide-react";
import { Button } from "@foundation/src/components/ui/button";
import type { AiProposal } from "@foundation/src/lib/api/ai-api";

export interface ProposalCardProps {
  proposal: AiProposal;
  /** True while the change is being applied through the normal write endpoint. */
  isApplying?: boolean;
  onApply: () => void;
  onDecline: () => void;
  /** Hidden for people who cannot edit — they can read the reasoning, not act on it. */
  canApply: boolean;
}

/**
 * A change the assistant suggests, waiting on the person.
 *
 * The card renders the proposed values themselves rather than the model's prose about
 * them, so what someone approves is what will actually be written. Applying goes through
 * the ordinary request endpoint under the person's own session — the assistant has no
 * write path of its own.
 */
export function ProposalCard({
  proposal,
  isApplying = false,
  onApply,
  onDecline,
  canApply,
}: ProposalCardProps) {
  const parsed = parseProposal(proposal.input);

  return (
    <div className="rounded-lg border bg-card p-3 space-y-3" data-testid="ai-proposal">
      <div className="space-y-1">
        <p className="text-sm font-medium">{titleFor(proposal.kind)}</p>
        {parsed.rationale && (
          <p className="text-sm text-muted-foreground">{parsed.rationale}</p>
        )}
      </div>

      {parsed.changes.length > 0 && (
        <dl className="text-sm rounded-md bg-muted/50 p-2 space-y-1">
          {parsed.changes.map((change) => (
            <div key={change.label} className="flex gap-2">
              <dt className="text-muted-foreground shrink-0">{change.label}</dt>
              <dd className="font-mono text-xs break-all self-center">{change.value}</dd>
            </div>
          ))}
        </dl>
      )}

      {canApply ? (
        <div className="flex gap-2">
          <Button size="sm" onClick={onApply} disabled={isApplying}>
            {isApplying ? <Loader2 className="h-4 w-4 animate-spin" /> : <Check className="h-4 w-4" />}
            Apply
          </Button>
          <Button size="sm" variant="ghost" onClick={onDecline} disabled={isApplying}>
            <X className="h-4 w-4" />
            Not now
          </Button>
        </div>
      ) : (
        <p className="text-xs text-muted-foreground">
          You do not have permission to change the schedule. Ask someone who can edit.
        </p>
      )}
    </div>
  );
}

function titleFor(kind: string): string {
  switch (kind) {
    case "propose_update_request":
      return "Suggested change";
    case "propose_auto_schedule":
      return "Suggested auto-scheduling";
    default:
      return "Suggestion";
  }
}

/**
 * Pulls the human-relevant fields out of the tool input. Anything unrecognized is shown
 * as-is rather than hidden — a person should never approve something they cannot see.
 */
function parseProposal(input: string): {
  rationale: string | null;
  changes: { label: string; value: string }[];
} {
  try {
    const parsed = JSON.parse(input) as Record<string, unknown>;
    const rationale = typeof parsed.rationale === "string" ? parsed.rationale : null;

    const changes: { label: string; value: string }[] = [];
    const raw = (parsed.changes ?? {}) as Record<string, unknown>;

    for (const [key, value] of Object.entries(raw)) {
      changes.push({ label: LABELS[key] ?? key, value: formatValue(value) });
    }

    if (Array.isArray(parsed.requestIds)) {
      changes.push({ label: "Requests", value: String(parsed.requestIds.length) });
    }

    return { rationale, changes };
  } catch {
    return { rationale: null, changes: [{ label: "Details", value: input }] };
  }
}

const LABELS: Record<string, string> = {
  startTs: "New start",
  endTs: "New end",
  resourceIds: "Resources",
  siteId: "Site",
};

function formatValue(value: unknown): string {
  if (Array.isArray(value)) return value.join(", ");
  if (typeof value === "string") {
    const asDate = new Date(value);
    // ISO timestamps read badly raw; anything else is shown verbatim.
    if (!Number.isNaN(asDate.getTime()) && value.includes("T")) return asDate.toLocaleString();
    return value;
  }
  return String(value);
}

// Re-exported so the panel can convert a proposal into the update payload without
// duplicating the shape knowledge.
/**
 * The requests an auto-scheduling proposal names.
 *
 * Separate from {@link proposalToRequestUpdate} because the two proposal kinds have
 * genuinely different payloads: one names a single request and its new field values, the
 * other names a set of requests and no values at all.
 */
export function proposalToAutoScheduleRequestIds(input: string): string[] {
  try {
    const parsed = JSON.parse(input) as Record<string, unknown>;
    const ids = parsed.requestIds;
    if (!Array.isArray(ids)) return [];
    return ids.filter((id): id is string => typeof id === "string" && id.length > 0);
  } catch {
    return [];
  }
}

export function proposalToRequestUpdate(input: string): {
  requestId: string | null;
  changes: Record<string, unknown>;
} {
  try {
    const parsed = JSON.parse(input) as Record<string, unknown>;
    return {
      requestId: typeof parsed.requestId === "string" ? parsed.requestId : null,
      changes: (parsed.changes ?? {}) as Record<string, unknown>,
    };
  } catch {
    return { requestId: null, changes: {} };
  }
}
