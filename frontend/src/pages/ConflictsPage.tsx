import { useConflicts } from "@foundation/src/hooks/useConflicts";
import { useRequests } from "@foundation/src/hooks/useUtilization";
import { useRequestEditor } from "@foundation/src/components/requests/useRequestEditor";
import { AlertCircle, AlertTriangle, Info } from "lucide-react";
import { format } from "date-fns";
import { useExportHandler } from "@foundation/src/hooks/useImportExport";
import { exportConflicts } from "@foundation/src/lib/utils/export-handlers";
import type { Conflict, Request } from "@foundation/src/types/requests";
import React, { useMemo } from "react";
import { logger } from "@foundation/src/lib/core/logger";
import { PageLayout, PageHeader } from "@foundation/src/components/layout";

type ConflictWithRequest = Conflict & { request: Request };

// Memoized conflict item component
const ConflictItem = React.memo(function ConflictItem({
  item,
  isHighlighted,
  onOpen,
  peerRequest,
  getSeverityIcon,
  getSeverityBadgeClass,
  getConflictKindLabel,
}: {
  item: ConflictWithRequest;
  isHighlighted: boolean;
  onOpen: (request: Request) => void;
  peerRequest?: Request;
  getSeverityIcon: (severity: string) => React.ReactNode;
  getSeverityBadgeClass: (severity: string) => string;
  getConflictKindLabel: (kind: string) => string;
}) {
  return (
    <div
      role="button"
      tabIndex={0}
      className={`border rounded-lg p-4 bg-card text-card-foreground shadow-sm hover:bg-accent/50 transition-colors cursor-pointer ${
        isHighlighted ? "ring-2 ring-destructive/60 bg-destructive/5" : ""
      }`}
      onClick={() => onOpen(item.request)}
      onKeyDown={(e) => {
        if (e.key === "Enter" || e.key === " ") {
          e.preventDefault();
          onOpen(item.request);
        }
      }}
    >
      <div className="flex items-start gap-3">
        <div className="mt-0.5">{getSeverityIcon(item.severity)}</div>
        <div className="flex-1 min-w-0">
          <div className="flex items-center gap-2 mb-2">
            <h3 className="font-semibold truncate">{item.request.name}</h3>
            <span
              className={`px-2 py-0.5 rounded text-xs font-medium ${getSeverityBadgeClass(
                item.severity
              )}`}
            >
              {item.severity}
            </span>
            <span className="px-2 py-0.5 rounded text-xs font-medium bg-muted">
              {getConflictKindLabel(item.kind)}
            </span>
          </div>
          <p className="text-sm text-muted-foreground mb-3">{item.message}</p>
          {item.request.startTs && item.request.endTs && (
            <div className="text-xs text-muted-foreground">
              <span className="inline-flex items-center gap-1">
                <span>Scheduled:</span>
                <span className="font-medium">
                  {format(new Date(item.request.startTs), "MMM d, h:mm a")} –{" "}
                  {format(new Date(item.request.endTs), "MMM d, h:mm a")}
                </span>
              </span>
            </div>
          )}
          {peerRequest && (
            <div className="mt-3">
              <button
                className="text-xs text-primary underline-offset-2 hover:underline"
                onClick={(e) => {
                  e.stopPropagation();
                  onOpen(peerRequest);
                }}
              >
                View other request: {peerRequest.name}
              </button>
            </div>
          )}
        </div>
      </div>
    </div>
  );
});

export function ConflictsPage() {
  const { open: openRequestEditor, dialogs: requestEditorDialogs } = useRequestEditor();
  const { conflicts } = useConflicts();
  // useRequests is deduped by React Query (same key as inside useConflicts)
  const { data: requests = [] } = useRequests();

  const searchParams = useMemo(
    () =>
      new URLSearchParams(
        typeof window !== "undefined" ? window.location.search : "",
      ),
    [],
  );
  const targetRequestId = searchParams.get("requestId");
  const targetConflictId = searchParams.get("conflictId");

  // Create a map of requestId to request for easy lookup
  const requestMap = useMemo(
    () => new Map(requests.map((r) => [r.id, r])),
    [requests]
  );

  // Get all conflicts with their associated requests
  const conflictItems = useMemo(
    () =>
      Array.from(conflicts.entries()).flatMap(([requestId, requestConflicts]) => {
        const request = requestMap.get(requestId);
        if (!request) return [];

        return requestConflicts.map((conflict) => ({
          ...conflict,
          request,
        }));
      }),
    [conflicts, requestMap]
  );

  const visibleConflictItems = useMemo(() => {
    if (!targetRequestId) return conflictItems;
    return conflictItems.filter((item) => item.request.id === targetRequestId);
  }, [conflictItems, targetRequestId]);

  // Handle export
  useExportHandler('conflicts', async (format) => {
    await exportConflicts(visibleConflictItems, format);
    logger.info(`Exported ${visibleConflictItems.length} conflicts as ${format.toUpperCase()}`);
  });

  const getSeverityIcon = (severity: string) => {
    switch (severity) {
      case "error":
        return <AlertCircle className="w-5 h-5 text-destructive" />;
      case "warning":
        return <AlertTriangle className="w-5 h-5 text-yellow-500" />;
      default:
        return <Info className="w-5 h-5 text-blue-500" />;
    }
  };

  const getSeverityBadgeClass = (severity: string) => {
    switch (severity) {
      case "error":
        return "bg-red-100 text-red-800 dark:bg-red-900 dark:text-red-200";
      case "warning":
        return "bg-yellow-100 text-yellow-800 dark:bg-yellow-900 dark:text-yellow-200";
      default:
        return "bg-blue-100 text-blue-800 dark:bg-blue-900 dark:text-blue-200";
    }
  };

  const getConflictKindLabel = (kind: string) => {
    switch (kind) {
      case "overlap":
        return "Scheduling Overlap";
      case "below_min_duration":
        return "Below Minimum Duration";
      case "before_earliest_start":
        return "Before Earliest Start";
      case "after_latest_end":
        return "After Latest End";
      case "connector_mismatch":
        return "Capability Mismatch";
      case "load_exceeded":
        return "Load Exceeded";
      case "size_mismatch":
        return "Size Mismatch";
      case "capacity_exceeded":
        return "Capacity Exceeded";
      default:
        return kind;
    }
  };

  const description = visibleConflictItems.length === 0
    ? "No conflicts detected. All scheduled requests meet their requirements."
    : `${visibleConflictItems.length} conflict${visibleConflictItems.length > 1 ? "s" : ""} found in scheduled requests.`;

  return (
    <PageLayout>
      <PageHeader title="Conflicts" description={description} />
      {targetRequestId && (
        <p className="text-xs text-muted-foreground -mt-4 mb-4">
          Filtered to request {targetRequestId}
        </p>
      )}

      {visibleConflictItems.length === 0 ? (
        <div className="flex items-center justify-center py-12 text-muted-foreground">
          <div className="text-center">
            <AlertCircle className="w-12 h-12 mx-auto mb-3 opacity-50" />
            <p>No conflicts to display</p>
          </div>
        </div>
      ) : (
        <div className="space-y-4">
          {visibleConflictItems.map((item) => (
            <ConflictItem
              key={item.id}
              item={item}
              isHighlighted={targetConflictId === item.id}
              onOpen={openRequestEditor}
              peerRequest={item.peerRequestId ? requestMap.get(item.peerRequestId) : undefined}
              getSeverityIcon={getSeverityIcon}
              getSeverityBadgeClass={getSeverityBadgeClass}
              getConflictKindLabel={getConflictKindLabel}
            />
          ))}
        </div>
      )}
      {requestEditorDialogs}
    </PageLayout>
  );
}
