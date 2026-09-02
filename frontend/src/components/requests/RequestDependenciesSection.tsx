import { useMemo, useState } from "react";
import { useMutation, useQuery } from "@tanstack/react-query";
import { Button } from "@foundation/src/components/ui/button";
import { Badge } from "@foundation/src/components/ui/badge";
import { Input } from "@foundation/src/components/ui/input";
import { Label } from "@foundation/src/components/ui/label";
import { LoadingSpinner } from "@foundation/src/components/ui/LoadingSpinner";
import { ErrorAlert } from "@foundation/src/components/ui/ErrorAlert";
import { Combobox } from "@foundation/src/components/ui/combobox";
import { qk } from "@foundation/src/lib/api/query-keys";
import { REQUEST_DERIVED_QUERY_KEYS } from "@foundation/src/lib/core/invalidate-request-data";
import { STALE } from "@foundation/src/lib/core/query-client";
import {
  addRequestDependency,
  deleteRequestDependency,
  getRequestDependencies,
  type RequestDependency,
} from "@foundation/src/lib/api/request-dependency-api";
import { updateRequest } from "@foundation/src/lib/api/request-api";
import { PREDECESSOR_LOGIC_OPTIONS } from "@foundation/src/constants/predecessor-logic";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@foundation/src/components/ui/select";
import type { PredecessorLogic, Request } from "@foundation/src/types/requests";
import { ArrowRight, Trash2 } from "lucide-react";

/**
 * The DEPENDENCIES tab — what this request waits for, and what waits on it.
 *
 * Editing is deliberately confined to this list rather than the tree view: the tree's drag
 * gesture already means "reparent", and overloading it to also mean "sequence" would make every
 * mis-drop ambiguous. Containment and precedence are different questions and get different UI.
 */
export function RequestDependenciesSection({
  request,
  readOnly,
  candidates,
  onNavigate,
}: {
  request: Request | null | undefined;
  readOnly: boolean;
  /** Leaves that may be linked. Groups are excluded — an edge on one cannot be enforced. */
  candidates: Request[];
  onNavigate?: (requestId: string) => void;
}) {
  const requestId = request?.id;

  const [predecessorId, setPredecessorId] = useState<string>("");
  const [lagHours, setLagHours] = useState<string>("0");

  // A blank field means no gap; anything unparseable (a pasted comma decimal, say) must not
  // reach the API as NaN, which serializes to null and comes back a 400 with no explanation.
  const parsedLagHours = lagHours.trim() === "" ? 0 : Number(lagHours);
  const lagIsValid = Number.isFinite(parsedLagHours) && parsedLagHours >= 0;
  const lagMinutes = lagIsValid ? Math.round(parsedLagHours * 60) : 0;

  const { data, isLoading, error } = useQuery({
    queryKey: qk.requests.dependencies(requestId ?? ""),
    queryFn: () => getRequestDependencies(requestId!),
    enabled: !!requestId,
    staleTime: STALE.REALTIME,
  });

  const addMutation = useMutation({
    mutationFn: () => addRequestDependency(requestId!, predecessorId, lagMinutes),
    meta: {
      successMessage: "Dependency added",
      errorMessage: "Could not add the dependency",
      invalidates: REQUEST_DERIVED_QUERY_KEYS,
    },
    onSuccess: () => {
      setPredecessorId("");
      setLagHours("0");
    },
  });

  // The condition is a property of the request, not of an edge, so it rides the request update
  // rather than getting an endpoint of its own. Logic and k always travel together: the server
  // clears k unless the logic is k_of_n, which is what stops a stale k outliving its logic.
  const conditionMutation = useMutation({
    mutationFn: (next: { logic: PredecessorLogic; k: number | null }) =>
      updateRequest(requestId!, {
        predecessorLogic: next.logic,
        predecessorLogicK: next.logic === "k_of_n" ? next.k : null,
      }),
    meta: {
      successMessage: "Start condition updated",
      errorMessage: "Could not update the start condition",
      invalidates: REQUEST_DERIVED_QUERY_KEYS,
    },
  });

  const removeMutation = useMutation({
    mutationFn: (dependencyId: string) => deleteRequestDependency(requestId!, dependencyId),
    meta: {
      successMessage: "Dependency removed",
      errorMessage: "Could not remove the dependency",
      invalidates: REQUEST_DERIVED_QUERY_KEYS,
    },
  });

  // Above the early returns: hooks must run in the same order every render. Memoized because a
  // tenant can hold thousands of requests and this maps all of them — without it every keystroke
  // in the Gap field reallocates the whole list and busts the picker's own filter cache. Keyed on
  // `data` rather than the derived arrays, whose `?? []` fallback is a new identity each render.
  const options = useMemo(() => {
    // The request itself, anything it already waits for, and anything already waiting on it:
    // linking to a successor would close a loop, which the API rejects with a 409.
    const linkedIds = new Set<string>([
      requestId ?? "",
      ...(data?.predecessors ?? []).map((e) => e.predecessorRequestId),
      ...(data?.successors ?? []).map((e) => e.successorRequestId),
    ]);

    return candidates
      .filter((c) => !linkedIds.has(c.id))
      .map((c) => ({ id: c.id, label: c.name }));
  }, [candidates, requestId, data]);

  if (!request) return null;
  if (isLoading) return <LoadingSpinner fullScreen={false} message="Loading dependencies…" />;
  if (error) return <ErrorAlert message="Could not load this request's dependencies." />;

  const predecessors = data?.predecessors ?? [];
  const successors = data?.successors ?? [];

  return (
    <div className="space-y-6">
      <section className="space-y-2">
        <h4 className="text-sm font-medium">Waits for</h4>
        {predecessors.length > 1 && (
          <StartConditionControl
            logic={request?.predecessorLogic ?? "all"}
            k={request?.predecessorLogicK ?? null}
            predecessorCount={predecessors.length}
            readOnly={readOnly || conditionMutation.isPending}
            onChange={(logic, k) => conditionMutation.mutate({ logic, k })}
          />
        )}
        {predecessors.length === 0 ? (
          <p className="text-sm text-muted-foreground">
            Nothing. This request can start as soon as its own window allows.
          </p>
        ) : (
          <ul className="space-y-2">
            {predecessors.map((edge) => (
              <EdgeRow
                key={edge.id}
                edge={edge}
                peerId={edge.predecessorRequestId}
                peerName={edge.predecessorName}
                readOnly={readOnly}
                onNavigate={onNavigate}
                onRemove={() => removeMutation.mutate(edge.id)}
                removing={removeMutation.isPending && removeMutation.variables === edge.id}
              />
            ))}
          </ul>
        )}

        {!readOnly && (
          <div className="flex flex-wrap items-end gap-2 pt-2">
            <div className="min-w-[12rem] flex-1 space-y-1">
              <Label htmlFor="dependency-predecessor" className="text-xs">
                Add something it waits for
              </Label>
              {/* Searchable rather than a plain select: a tenant can hold thousands of
                  requests, and a select renders every one of them even while closed. */}
              <Combobox
                id="dependency-predecessor"
                value={predecessorId}
                onChange={setPredecessorId}
                options={options}
                placeholder="Choose a request…"
                searchPlaceholder="Search requests…"
                emptyText="No matching request"
                maxResults={50}
              />
            </div>
            <div className="w-28 space-y-1">
              <Label htmlFor="dependency-lag" className="text-xs">
                Gap (hours)
              </Label>
              <Input
                id="dependency-lag"
                type="number"
                min={0}
                value={lagHours}
                onChange={(e) => setLagHours(e.target.value)}
              />
            </div>
            <Button
              type="button"
              onClick={() => addMutation.mutate()}
              disabled={!predecessorId || !lagIsValid || addMutation.isPending}
            >
              Add
            </Button>
          </div>
        )}
      </section>

      <section className="space-y-2">
        <h4 className="text-sm font-medium">Waited on by</h4>
        {successors.length === 0 ? (
          <p className="text-sm text-muted-foreground">Nothing waits for this request.</p>
        ) : (
          <ul className="space-y-2">
            {successors.map((edge) => (
              <EdgeRow
                key={edge.id}
                edge={edge}
                peerId={edge.successorRequestId}
                peerName={edge.successorName}
                readOnly={readOnly}
                onNavigate={onNavigate}
                onRemove={() => removeMutation.mutate(edge.id)}
                removing={removeMutation.isPending && removeMutation.variables === edge.id}
              />
            ))}
          </ul>
        )}
      </section>
    </div>
  );
}

function EdgeRow({
  edge,
  peerId,
  peerName,
  readOnly,
  onNavigate,
  onRemove,
  removing,
}: {
  edge: RequestDependency;
  peerId: string;
  peerName: string;
  readOnly: boolean;
  onNavigate?: (requestId: string) => void;
  onRemove: () => void;
  removing: boolean;
}) {
  return (
    <li className="flex items-center justify-between gap-2 rounded-md border p-2">
      <div className="flex min-w-0 items-center gap-2">
        <ArrowRight className="h-4 w-4 shrink-0 text-muted-foreground" />
        {onNavigate ? (
          <button
            type="button"
            className="truncate text-sm underline-offset-2 hover:underline"
            onClick={() => onNavigate(peerId)}
          >
            {peerName}
          </button>
        ) : (
          <span className="truncate text-sm">{peerName}</span>
        )}
        {edge.lagMinutes > 0 && (
          <Badge variant="secondary" className="shrink-0">
            +{formatLag(edge.lagMinutes)}
          </Badge>
        )}
      </div>
      {!readOnly && (
        <Button
          type="button"
          variant="ghost"
          size="icon"
          aria-label={`Remove dependency on ${peerName}`}
          onClick={onRemove}
          disabled={removing}
        >
          <Trash2 className="h-4 w-4" />
        </Button>
      )}
    </li>
  );
}

/** Lag reads in the largest whole unit that fits, because "2880 minutes" is arithmetic. */
function formatLag(minutes: number): string {
  if (minutes % (24 * 60) === 0) {
    const days = minutes / (24 * 60);
    return `${days} day${days === 1 ? "" : "s"}`;
  }
  if (minutes % 60 === 0) {
    const hours = minutes / 60;
    return `${hours} hour${hours === 1 ? "" : "s"}`;
  }
  return `${minutes} min`;
}

/**
 * Which predecessors have to be done before this request may start.
 *
 * Shown only when there is more than one predecessor: with none the question is moot, and with
 * exactly one every logic means the same thing, so offering the choice would be noise.
 */
function StartConditionControl({
  logic,
  k,
  predecessorCount,
  readOnly,
  onChange,
}: {
  logic: PredecessorLogic;
  k: number | null;
  predecessorCount: number;
  readOnly: boolean;
  onChange: (logic: PredecessorLogic, k: number | null) => void;
}) {
  // A k that outlived the edges it described still has to render as something sensible, and
  // "all of them" is how the server reads it too.
  const effectiveK = Math.min(Math.max(k ?? predecessorCount, 1), predecessorCount);

  const [draftK, setDraftK] = useState(String(effectiveK));
  // Follow the stored value when it changes underneath — after a save, or when an edge is added
  // or removed and the clamp moves. Adjusted during render rather than in an effect: React
  // re-runs this component before touching the DOM, so the box never paints the stale value.
  const [lastSyncedK, setLastSyncedK] = useState(effectiveK);
  if (lastSyncedK !== effectiveK) {
    setLastSyncedK(effectiveK);
    setDraftK(String(effectiveK));
  }

  const commitK = () => {
    const parsed = Number(draftK);
    // An empty or unparseable box means "no change"; it is a half-typed value, not a request to
    // store 0.
    if (draftK.trim() === "" || !Number.isFinite(parsed)) {
      setDraftK(String(effectiveK));
      return;
    }
    const next = Math.min(Math.max(Math.round(parsed), 1), predecessorCount);
    setDraftK(String(next));
    if (next !== effectiveK) onChange("k_of_n", next);
  };

  return (
    <div className="flex flex-wrap items-end gap-2 rounded-md border bg-muted/30 p-2">
      <div className="space-y-1">
        <Label htmlFor="dependency-logic" className="text-xs">
          Can start when
        </Label>
        <Select
          value={logic}
          disabled={readOnly}
          onValueChange={(next) =>
            onChange(next as PredecessorLogic, next === "k_of_n" ? effectiveK : null)
          }
        >
          <SelectTrigger id="dependency-logic" className="h-8 w-[190px]">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            {PREDECESSOR_LOGIC_OPTIONS.map((option) => (
              <SelectItem key={option.value} value={option.value}>
                {option.label}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>

      {logic === "k_of_n" && (
        <div className="space-y-1">
          <Label htmlFor="dependency-logic-k" className="text-xs">
            How many
          </Label>
          <Input
            id="dependency-logic-k"
            type="number"
            min={1}
            max={predecessorCount}
            className="h-8 w-20"
            // Local draft, committed on blur or Enter. Writing on every keystroke made the field
            // untypable — clearing it to type "12" produced Number("") === 0, which clamped to 1
            // and saved — and fired a request update plus a tenant-wide invalidation per
            // character.
            value={draftK}
            disabled={readOnly}
            onChange={(e) => setDraftK(e.target.value)}
            onBlur={commitK}
            onKeyDown={(e) => {
              if (e.key === "Enter") {
                e.preventDefault();
                commitK();
              }
            }}
          />
        </div>
      )}

      <p className="text-xs text-muted-foreground pb-2">
        {PREDECESSOR_LOGIC_OPTIONS.find((o) => o.value === logic)?.hint}
      </p>
    </div>
  );
}
