/* eslint-disable orkyo/ui-primitives -- F3 (2026-09 review): 1 legacy hand-rolled empty/loading site; converge on touch, then drop this line. */
import { Fragment, useState } from "react";
import { toast } from "sonner";
import { ChevronDown, ChevronRight } from "lucide-react";
import { Card, CardContent, CardHeader, CardTitle } from "@foundation/src/components/ui/card";
import { Badge } from "@foundation/src/components/ui/badge";
import { LoadingSpinner } from "@foundation/src/components/ui/LoadingSpinner";
import { ErrorAlert } from "@foundation/src/components/ui/ErrorAlert";
import { BottleneckChart } from "@foundation/src/components/insights/InsightsTrendCharts";
import { useInsightsTabContext } from "@foundation/src/components/insights/insightsTabContext";
import { useCriticalPath, useInsightsBottlenecksByType } from "@foundation/src/hooks/useInsights";
import { useFetchRequest } from "@foundation/src/hooks/useRequests";
import { useResourceTypes } from "@foundation/src/hooks/useResourceTypes";
import { type InsightsBottlenecks } from "@foundation/src/lib/api/insights-api";
import type { ResourceTypeInfo } from "@foundation/src/lib/api/resource-types-api";
import { RESOURCE_CLASS, resourceClassOf } from "@foundation/src/constants/resource-class";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@foundation/src/components/ui/select";
import { useConflictRegistry } from "@foundation/src/hooks/useConflictRegistry";
import { useRequestEditor } from "@foundation/src/hooks/useRequestEditor";
import {
  type CriticalPathChain,
  type CriticalPathNode,
} from "@foundation/src/lib/api/request-dependency-api";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@foundation/src/components/ui/table";
import { formatDateTimeDisplay } from "@foundation/src/lib/formatters";
import { formatMinutesHuman } from "@foundation/src/lib/utils/utils";

/**
 * Where the plan is constrained: which resources are over capacity, and which dependency chains
 * are closest to missing a deadline.
 *
 * Its own tab rather than a section of Utilization, because neither half answers the question
 * that tab asks. Utilization trends over a bucket; both of these are point-in-time facts about
 * the current plan, and the bottleneck ranking deliberately ignores the bucket entirely.
 */
export function BottlenecksTab() {
  const { from, to, siteId } = useInsightsTabContext();

  // Two rankings — stations and assets — because that is how the app already divides resources
  // (a station has a fixed location, an asset moves), and each carries a filter for the types
  // inside it. One ranking per type produced a chart per type, which does not scale; one ranking
  // for everything let the busiest type take all ten slots.
  const { data: resourceTypes = [] } = useResourceTypes(true);

  // Every type is fetched, whichever filter is set: a class ranking is the merge of its types'
  // rankings, and narrowing to one type then reads a result already in cache.
  const rankings = useInsightsBottlenecksByType(
    resourceTypes.map((type) => type.key),
    siteId,
    from,
    to,
  );
  const byType = resourceTypes.map((type, i) => ({ type, result: rankings[i] }));

  // The network is computed once, without the period; the period only picks which chains to
  // list, so changing it does not recompute anything.
  const criticalPath = useCriticalPath(siteId);
  const chainsInPeriod = (criticalPath.data?.chains ?? []).filter(
    (chain) => new Date(chain.start) < to && new Date(chain.finish) > from,
  );

  const fetchRequest = useFetchRequest();
  const { conflictsByRequest } = useConflictRegistry();
  const { open: openRequestEditor, dialogs: requestEditorDialogs } = useRequestEditor();

  // A critical-path node carries only an id and a name, so the request is fetched when the user
  // asks for it. Eagerly loading every node's request would cost a tenant-wide read to make rows
  // clickable that mostly never get clicked.
  const openRequest = async (requestId: string) => {
    try {
      const request = await fetchRequest(requestId);
      openRequestEditor(request, conflictsByRequest.get(requestId) ?? []);
    } catch {
      // Silently doing nothing here reads as a dead row, which is worse than saying so.
      toast.error("Could not open that request. Please try again.");
    }
  };

  return (
    <div className="h-full space-y-4 overflow-auto p-1">
      <div className="grid grid-cols-1 gap-4 xl:grid-cols-2">
        <ClassBottlenecks
          label="stations"
          types={byType.filter(({ type }) => resourceClassOf(type) === RESOURCE_CLASS.STATION)}
        />
        <ClassBottlenecks
          label="assets"
          types={byType.filter(({ type }) => resourceClassOf(type) === RESOURCE_CLASS.ASSET)}
        />
      </div>

      <Card>
        <CardHeader className="pb-2 md:pb-2">
          <CardTitle className="text-sm">
            Dependency chains
            {criticalPath.data && criticalPath.data.chains.length > 0 && (
              <span className="ml-2 font-normal text-muted-foreground">
                {chainsInPeriod.length} in this period
              </span>
            )}
          </CardTitle>
        </CardHeader>
        <CardContent>
          {criticalPath.isLoading ? (
            <LoadingSpinner fullScreen={false} message="Computing…" />
          ) : criticalPath.error ? (
            <ErrorAlert message="Could not compute the dependency chains." />
          ) : (
            <CriticalPathBody
              hasNetwork={(criticalPath.data?.chains.length ?? 0) > 0}
              chains={chainsInPeriod}
              nodes={criticalPath.data?.nodes ?? []}
              diagnostics={criticalPath.data?.diagnostics ?? []}
              onOpenRequest={(id) => void openRequest(id)}
            />
          )}
        </CardContent>
      </Card>

      {requestEditorDialogs}
    </div>
  );
}

interface TypeRanking {
  type: ResourceTypeInfo;
  result?: { data?: InsightsBottlenecks; isLoading: boolean; error: unknown };
}

const ALL = "all";

/** Mirrors the server's per-type cap, so a merged class ranking is the same length as a type one. */
const BOTTLENECK_LIMIT = 10;

/**
 * One class of resources — stations or assets — ranked by overbooked time, with a filter for the
 * types inside it.
 *
 * "All" merges the per-type rankings rather than asking for an unfiltered one. That is exact, not
 * an approximation: a resource can only reach the class top ten if it is already in its own type's
 * top ten, so the merge of those lists contains every candidate.
 */
function ClassBottlenecks({ label, types }: { label: string; types: TypeRanking[] }) {
  const [selected, setSelected] = useState<string>(ALL);

  if (types.length === 0) return null;

  // A type can be deactivated while its filter is still selected; fall back rather than chart
  // nothing.
  const active = types.find(({ type }) => type.key === selected);
  const shown = active ? [active] : types;

  const isLoading = shown.some(({ result }) => result?.isLoading ?? true);
  const error = shown.find(({ result }) => result?.error)?.result?.error;

  const merged: InsightsBottlenecks | undefined = isLoading
    ? undefined
    : {
        ...(shown[0]?.result?.data ?? ({} as InsightsBottlenecks)),
        items: shown
          .flatMap(({ result }) => result?.data?.items ?? [])
          .sort((a, b) => b.overbookedMinutes - a.overbookedMinutes)
          .slice(0, BOTTLENECK_LIMIT),
      };

  return (
    <BottleneckChart
      // The type name is tenant-authored, so it is shown exactly as written — lowercasing it
      // turns "CNC Machines" into "cnc machines".
      //
      // With no type selected the card mixes several types, so it uses the neutral word
      // rather than the class word: "assets" is Orkyo's navigation vocabulary, and a list
      // of people headed "Most overloaded assets" reads badly to the people on it. The
      // class distinction still belongs on the filter control below.
      title={active ? `Most overloaded ${active.type.displayNamePlural}` : "Most overloaded resources"}
      data={merged}
      isLoading={isLoading}
      error={error}
      action={
        types.length > 1 ? (
          <Select value={selected} onValueChange={setSelected}>
            <SelectTrigger className="h-8 w-[170px]" aria-label={`Filter ${label} by resource type`}>
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value={ALL}>All {label}</SelectItem>
              {types.map(({ type }) => (
                <SelectItem key={type.key} value={type.key}>
                  {type.displayNamePlural}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        ) : undefined
      }
    />
  );
}

function CriticalPathBody({
  hasNetwork,
  chains,
  nodes,
  diagnostics,
  onOpenRequest,
}: {
  hasNetwork: boolean;
  chains: CriticalPathChain[];
  nodes: CriticalPathNode[];
  diagnostics: string[];
  onOpenRequest: (requestId: string) => void;
}) {
  const [expanded, setExpanded] = useState<ReadonlySet<string>>(new Set());

  if (!hasNetwork) {
    return (
      <p className="py-6 text-center text-sm text-muted-foreground">
        No open work depends on anything. Link requests on a request's Dependencies tab, and each
        chain appears here with its finish and deadline.
      </p>
    );
  }

  const day = formatDateTimeDisplay;
  const nodeById = new Map(nodes.map((n) => [n.requestId, n]));
  const toggle = (chainId: string) =>
    setExpanded((prev) => {
      const next = new Set(prev);
      if (next.has(chainId)) next.delete(chainId);
      else next.add(chainId);
      return next;
    });

  return (
    <div className="space-y-3">
      {diagnostics.length > 0 && (
        <ul className="space-y-1 text-xs text-muted-foreground">
          {diagnostics.map((d, i) => (
            <li key={i}>{d}</li>
          ))}
        </ul>
      )}

      {chains.length === 0 ? (
        <p className="py-6 text-center text-sm text-muted-foreground">
          No open dependency chains in this period.
        </p>
      ) : (
        <div className="overflow-x-auto">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead scope="col">Chain</TableHead>
                <TableHead scope="col">Starts</TableHead>
                <TableHead scope="col">Ends</TableHead>
                <TableHead scope="col">Deadline</TableHead>
                <TableHead scope="col" className="text-right">Slack</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {chains.map((chain) => {
                const isOpen = expanded.has(chain.chainId);
                const steps = chain.requestIds.flatMap((id) => nodeById.get(id) ?? []);
                return (
                  <Fragment key={chain.chainId}>
                    <TableRow>
                      <TableCell>
                        <button
                          type="button"
                          aria-expanded={isOpen}
                          className="flex items-center gap-2 text-left"
                          onClick={() => toggle(chain.chainId)}
                        >
                          {isOpen ? (
                            <ChevronDown className="h-4 w-4 shrink-0" aria-hidden />
                          ) : (
                            <ChevronRight className="h-4 w-4 shrink-0" aria-hidden />
                          )}
                          <span className="truncate">
                            {chain.firstName} → {chain.lastName}
                          </span>
                          <span className="shrink-0 text-xs text-muted-foreground">
                            {chain.requestIds.length} steps
                          </span>
                        </button>
                      </TableCell>
                      <TableCell className="whitespace-nowrap text-muted-foreground">
                        {day(chain.start)}
                      </TableCell>
                      <TableCell className="whitespace-nowrap text-muted-foreground">
                        {day(chain.finish)}
                      </TableCell>
                      <TableCell className="whitespace-nowrap text-muted-foreground">
                        {chain.deadline ? day(chain.deadline) : "—"}
                      </TableCell>
                      <TableCell className="text-right whitespace-nowrap">
                        <ChainSlack minutes={chain.slackMinutes} />
                      </TableCell>
                    </TableRow>
                    {isOpen && (
                      <TableRow className="hover:bg-transparent">
                        <TableCell colSpan={5} className="pl-8">
                          <ChainSteps steps={steps} onOpenRequest={onOpenRequest} />
                        </TableCell>
                      </TableRow>
                    )}
                  </Fragment>
                );
              })}
            </TableBody>
          </Table>
        </div>
      )}

      <p className="text-xs text-muted-foreground">
        Open work only. Work already placed is treated as fixed. Slack is the time between a
        chain's earliest finish and its deadline.
      </p>
    </div>
  );
}

function ChainSlack({ minutes }: { minutes: number | null }) {
  if (minutes === null) return <span className="text-muted-foreground">—</span>;
  if (minutes < 0) {
    return (
      <Badge variant="destructive" className="shrink-0">
        Late by {formatMinutesHuman(-minutes)}
      </Badge>
    );
  }
  return <>{formatMinutesHuman(minutes)}</>;
}

/** One chain's requests in dependency order. A critical step moves the chain's finish. */
function ChainSteps({
  steps,
  onOpenRequest,
}: {
  steps: CriticalPathNode[];
  onOpenRequest: (requestId: string) => void;
}) {
  const day = formatDateTimeDisplay;

  return (
    <Table>
      <TableHeader>
        <TableRow>
          <TableHead scope="col">Request</TableHead>
          <TableHead scope="col">Earliest</TableHead>
          <TableHead scope="col">Latest</TableHead>
          <TableHead scope="col" className="text-right">Float</TableHead>
        </TableRow>
      </TableHeader>
      <TableBody>
        {steps.map((n) => (
          // The whole row opens the request, matching the conflict cards on the Conflicts
          // tab. TableRow already brings the hover highlight, so this only adds the cursor.
          <TableRow
            key={n.requestId}
            role="button"
            tabIndex={0}
            className="cursor-pointer"
            onClick={() => onOpenRequest(n.requestId)}
            onKeyDown={(e) => {
              if (e.key === "Enter" || e.key === " ") {
                e.preventDefault();
                onOpenRequest(n.requestId);
              }
            }}
          >
            <TableCell>
              <div className="flex items-center gap-2">
                <span className="truncate">{n.name}</span>
                {n.isCritical && (
                  <Badge variant="destructive" className="shrink-0">
                    Critical
                  </Badge>
                )}
                {n.isScheduled && (
                  <Badge variant="secondary" className="shrink-0">
                    Scheduled
                  </Badge>
                )}
              </div>
            </TableCell>
            <TableCell className="whitespace-nowrap text-muted-foreground">
              {day(n.earliestStart)} – {day(n.earliestFinish)}
            </TableCell>
            <TableCell className="whitespace-nowrap text-muted-foreground">
              {day(n.latestStart)} – {day(n.latestFinish)}
            </TableCell>
            <TableCell className="text-right whitespace-nowrap">
              {/* Zero float is the definition of critical: any slip here moves the finish. */}
              {n.totalFloatMinutes <= 0 ? "—" : formatMinutesHuman(n.totalFloatMinutes)}
            </TableCell>
          </TableRow>
        ))}
      </TableBody>
    </Table>
  );
}
