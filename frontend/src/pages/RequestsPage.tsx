import {
    RequestFormDialog,
    type RequestFormData,
} from "@foundation/src/components/requests/RequestFormDialog";
import { RequestTreeView } from "@foundation/src/components/requests/RequestTreeView";
import { RequestListView } from "@foundation/src/components/requests/RequestListView";
import { ScrollArea } from "@foundation/src/components/ui/scroll-area";
import { ConfirmDialog } from "@foundation/src/components/ui/ConfirmDialog";
import { Button } from "@foundation/src/components/ui/button";
import { LoadingSpinner } from "@foundation/src/components/ui/LoadingSpinner";
import { EmptyState } from "@foundation/src/components/ui/EmptyState";
import { Input } from "@foundation/src/components/ui/input";
import { PageLayout, PageHeader } from "@foundation/src/components/layout";
import { usePageTitle } from "@foundation/src/hooks/usePageTitle";
import { toast } from "sonner";
import {
    Tooltip,
    TooltipContent,
    TooltipProvider,
    TooltipTrigger,
} from "@foundation/src/components/ui/tooltip";
import {
    createRequest,
    deleteRequest,
    getRequests,
    moveRequest,
    updateRequest,
} from "@foundation/src/lib/api/request-api";
import { useConflictRegistry } from "@foundation/src/hooks/useConflictRegistry";
import { useCanEdit } from "@foundation/src/hooks/usePermissions";
import { useNow } from "@foundation/src/hooks/useNow";
import { useDebouncedCallback } from "@foundation/src/hooks/useDebouncedCallback";
import { withEffectiveStatus } from "@foundation/src/domain/scheduling/effective-status";
import type {
    CreateRequestRequest,
    PlanningMode,
    Request,
} from "@foundation/src/types/requests";
import {
    buildChildrenIdMap,
    buildRequestTree,
    flattenTree,
    flattenVisibleTree,
    getAncestorIds,
    getDescendantIds,
    getNextSortOrder,
    canHaveChildren,
} from "@foundation/src/domain/request-tree";
import { useRequestTreeStore } from "@foundation/src/store/request-tree-store";
import {
    Calendar,
    ChevronsDown,
    ChevronsUp,
    List,
    Plus,
    Search,
    TreePine,
} from "lucide-react";
import { useEffect, useState, useMemo, useCallback, useRef } from "react";
import { useNavigate, useSearchParams } from "react-router-dom";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { qk } from "@foundation/src/lib/api/query-keys";
import { useExportHandler, useImportHandler } from "@foundation/src/hooks/useImportExport";
import { exportRequests, importRequests } from "@foundation/src/lib/utils/export-handlers";
import { buildCreatePayload, buildUpdatePayload } from "@foundation/src/lib/utils/utils";
import { deleteRequestSubtree } from "@foundation/src/lib/api/request-api";
import { logger } from "@foundation/src/lib/core/logger";
import { invalidateRequestData } from "@foundation/src/lib/core/invalidate-request-data";

const EMPTY_REQUESTS: Request[] = [];

/**
 * Discriminated union for the page's modal/dialog state.
 *
 * Only one dialog can be open at a time, so a single union is simpler and
 * safer than juggling seven independent useState flags. Each variant carries
 * exactly the data the dialog needs to render and act on.
 */
type Dialog =
  | { kind: "create"; parent: Request | null; defaultMode?: PlanningMode }
  | { kind: "edit"; request: Request }
  | { kind: "delete"; request: Request };

// ---------------------------------------------------------------------------
// Page
// ---------------------------------------------------------------------------

export function RequestsPage() {
  usePageTitle("Requests");
  const queryClient = useQueryClient();
  const canEdit = useCanEdit();
  const navigate = useNavigate();
  const [searchParams, setSearchParams] = useSearchParams();
  // The request list lives in the query cache under the shared `requests`
  // prefix, so `invalidateRequestData` alone refreshes it after any mutation —
  // no manual re-fetch bookkeeping.
  const {
    data: rawRequests = EMPTY_REQUESTS,
    isLoading: requestsLoading,
    error: requestsError,
    refetch: refetchRequests,
  } = useQuery({
    queryKey: qk.requests.list(),
    queryFn: () => getRequests(true),
  });
  const nowMs = useNow();
  // Recompute the time-derived lifecycle (new → in_progress → done) live so the list/tree/detail
  // auto-update as the clock advances, matching the utilization view. cancelled/deferred pass through;
  // withEffectiveStatus keeps the array ref stable until a real status flip.
  const requests = useMemo(() => withEffectiveStatus(rawRequests, nowMs), [rawRequests, nowMs]);
  const [searchQuery, setSearchQuery] = useState("");
  const [debouncedSearch, setDebouncedSearch] = useState("");
  const [dialog, setDialog] = useState<Dialog | null>(null);
  // Mutation in-flight flag + error; list load state comes from the query above.
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const isLoading = requestsLoading || loading;
  const errorMessage =
    error ??
    (requestsError
      ? requestsError instanceof Error
        ? requestsError.message
        : "Failed to load requests"
      : null);
  const hasInitializedTreeExpansionRef = useRef(false);

  const {
    expandedIds,
    toggle,
    expandAncestors,
    expandAll,
    collapseAll,
    selectedId,
    setSelectedId,
    viewMode,
    setViewMode,
  } = useRequestTreeStore();

  // Parent (expandable) ids — feeds the toolbar Expand/Collapse-all buttons.
  // Mirrors the one-liner the tree uses for its `*` shortcut.
  const expandableIds = useMemo(
    () => requests.filter((r) => canHaveChildren(r.planningMode)).map((r) => r.id),
    [requests],
  );

  // Debounce search input
  const debounceSearch = useDebouncedCallback(setDebouncedSearch, 300);
  useEffect(() => {
    debounceSearch(searchQuery);
  }, [searchQuery, debounceSearch]);

  // Handle export/import
  useExportHandler('requests', async (format) => {
    await exportRequests(requests, format);
    logger.info(`Exported ${requests.length} requests as ${format.toUpperCase()}`);
  });

  useImportHandler('requests', async (file, format) => {
    try {
      const importedRequests = await importRequests(file, format);
      if (!importedRequests.length) {
        throw new Error('No valid requests found in file');
      }
      for (const req of importedRequests) {
        await createRequest(req as CreateRequestRequest);
      }
      invalidateRequestData(queryClient);
      toast.success(`Imported ${importedRequests.length} requests`);
    } catch (error) {
      logger.error('Import failed:', error);
      toast.error('Failed to import requests', {
        description: error instanceof Error ? error.message : undefined,
      });
    }
  });

  // Handle ?edit=<id> query param from global search
  useEffect(() => {
    const editId = searchParams.get('edit');
    if (editId && requests.length > 0 && !isLoading) {
      const requestToEdit = requests.find(r => r.id === editId);
      if (requestToEdit) {
        setDialog({ kind: "edit", request: requestToEdit });
        searchParams.delete('edit');
        setSearchParams(searchParams, { replace: true });
      }
    }
  }, [searchParams, requests, isLoading, setSearchParams]);

  // Parent → child-ids map, built once per list change and shared by every
  // getDescendantIds call site below.
  const childrenById = useMemo(() => buildChildrenIdMap(requests), [requests]);

  // Search matches + their ancestors (tree mode only) — computed once and
  // shared by the visible-entries memo and the auto-expand effect.
  const searchMatches = useMemo(() => {
    if (viewMode !== "tree" || !debouncedSearch) return null;
    const query = debouncedSearch.toLowerCase();
    const matchIds = new Set(
      requests
        .filter(r =>
          r.name.toLowerCase().includes(query) ||
          r.description?.toLowerCase().includes(query)
        )
        .map(r => r.id)
    );

    const ancestorIds = new Set<string>();
    if (matchIds.size > 0) {
      const byId = new Map(requests.map(r => [r.id, r]));
      for (const id of matchIds) {
        for (const aid of getAncestorIds(id, requests, byId)) {
          ancestorIds.add(aid);
        }
      }
    }
    return { matchIds, ancestorIds };
  }, [requests, debouncedSearch, viewMode]);

  // Build tree + flatten, respecting expanded state and search (tree mode only)
  const visibleEntries = useMemo(() => {
    if (viewMode !== "tree") return [];

    const tree = buildRequestTree(requests);

    if (searchMatches) {
      const { matchIds, ancestorIds } = searchMatches;
      if (matchIds.size === 0) return [];

      const allFlat = flattenTree(tree);
      return allFlat.filter(e =>
        matchIds.has(e.request.id) || ancestorIds.has(e.request.id)
      );
    }

    // No search — use flattenVisibleTree which respects expand/collapse state
    return flattenVisibleTree(tree, expandedIds);
  }, [requests, searchMatches, expandedIds, viewMode]);

  // Filtered requests for list mode
  const filteredRequests = useMemo(() => {
    if (viewMode !== "list") return [];
    if (!debouncedSearch) return requests;
    const query = debouncedSearch.toLowerCase();
    return requests.filter(r =>
      r.name.toLowerCase().includes(query) ||
      r.description?.toLowerCase().includes(query)
    );
  }, [requests, debouncedSearch, viewMode]);

  // Auto-expand ancestors when search matches (tree mode) — reuse the
  // matches/ancestors already computed by `searchMatches`.
  useEffect(() => {
    if (!searchMatches || searchMatches.ancestorIds.size === 0) return;
    expandAncestors([...searchMatches.ancestorIds]);
  }, [searchMatches, expandAncestors]);

  const handleCreateRequest = useCallback((defaultMode: PlanningMode = 'leaf') => {
    setDialog({ kind: "create", parent: null, defaultMode });
  }, []);

  const handleDrop = useCallback(async (draggedId: string, targetId: string) => {
    try {
      setLoading(true);
      setError(null);
      await moveRequest(draggedId, {
        newParentRequestId: targetId,
        sortOrder: getNextSortOrder(targetId, requests),
      });
      invalidateRequestData(queryClient);
      // Auto-expand the new parent
      if (!expandedIds.has(targetId)) {
        toggle(targetId);
      }
    } catch (err) {
      logger.error("Failed to move request:", err);
      setError(err instanceof Error ? err.message : "Failed to move request");
    } finally {
      setLoading(false);
    }
  }, [requests, queryClient, expandedIds, toggle]);

  const handleEditRequest = useCallback((request: Request) => {
    setDialog({ kind: "edit", request });
  }, []);

  const handleDeleteRequest = useCallback((request: Request) => {
    setDialog({ kind: "delete", request });
  }, []);

  const handleConfirmDelete = useCallback(async () => {
    if (dialog?.kind !== "delete") return;
    const request = dialog.request;
    setDialog(null);

    try {
      setLoading(true);
      setError(null);

      // Use subtree delete if the request has descendants
      const descendantIds = getDescendantIds(request.id, requests, childrenById);
      if (descendantIds.length > 0) {
        await deleteRequestSubtree(request.id);
      } else {
        await deleteRequest(request.id);
      }

      // Clear selection if the deleted request (or one of its descendants) was selected
      if (selectedId) {
        if (selectedId === request.id || descendantIds.includes(selectedId)) {
          setSelectedId(request.parentRequestId ?? null);
        }
      }

      invalidateRequestData(queryClient);
      toast.success("Request deleted");
    } catch (err) {
      logger.error("Failed to delete request:", err);
      const message = err instanceof Error ? err.message : "Failed to delete request";
      setError(message);
      toast.error("Failed to delete request", { description: message });
    } finally {
      setLoading(false);
    }
  }, [dialog, requests, childrenById, queryClient, selectedId, setSelectedId]);

  const handleSaveRequest = useCallback(async (data: RequestFormData) => {
    try {
      setLoading(true);
      setError(null);

      const isEdit = dialog?.kind === "edit";
      let created: Request | undefined;
      if (isEdit) {
        await updateRequest(dialog.request.id, buildUpdatePayload(data, dialog.request.planningMode, dialog.request.siteId));
      } else {
        created = await createRequest(buildCreatePayload(data));
      }

      invalidateRequestData(queryClient);
      setDialog(null);
      toast.success(isEdit ? "Request updated" : "Request created");
      // Returned so the dialog can create children queued on its Children tab.
      return created;
    } catch (err) {
      logger.error("Failed to save request:", err);
      const message = err instanceof Error ? err.message : "Failed to save request";
      setError(message);
      toast.error(dialog?.kind === "edit" ? "Failed to update request" : "Failed to create request", {
        description: message,
      });
      throw err;
    } finally {
      setLoading(false);
    }
  }, [dialog, queryClient]);

  const handleSelect = useCallback((id: string) => {
    setSelectedId(selectedId === id ? null : id);
  }, [selectedId, setSelectedId]);

  const handleNavigateToRequest = useCallback((id: string) => {
    const ancestors = getAncestorIds(id, requests);
    if (ancestors.length > 0) expandAncestors(ancestors);
    setSelectedId(id);
    // Switch to tree view so the hierarchy is visible
    if (viewMode !== "tree") setViewMode("tree");
  }, [requests, expandAncestors, setSelectedId, viewMode, setViewMode]);

  // Re-target the form dialog to another request (breadcrumb / Children tab
  // navigation) and keep the tree selection/expansion in sync.
  const handleDialogNavigate = useCallback((id: string) => {
    const target = requests.find((r) => r.id === id);
    if (target) setDialog({ kind: "edit", request: target });
    handleNavigateToRequest(id);
  }, [requests, handleNavigateToRequest]);

  // Build conflict count map for tree view (own + descendant conflicts)
  const { conflictsByRequest: storeConflicts } = useConflictRegistry();

  const handleOpenConflicts = useCallback(
    (requestId: string) => {
      const candidateIds = [requestId, ...getDescendantIds(requestId, requests, childrenById)];
      const targetRequestId =
        candidateIds.find((id) => (storeConflicts.get(id)?.length ?? 0) > 0) ?? requestId;
      const targetConflictId = storeConflicts.get(targetRequestId)?.[0]?.id;

      const params = new URLSearchParams();
      params.set("requestId", targetRequestId);
      if (targetConflictId) {
        params.set("conflictId", targetConflictId);
      }

      navigate(`/insights/conflicts?${params.toString()}`);
    },
    [navigate, requests, childrenById, storeConflicts],
  );

  const conflictMap = useMemo(() => {
    const map = new Map<string, number>();
    for (const r of requests) {
      const own = storeConflicts.get(r.id)?.length ?? 0;
      // Bubble up: count descendant conflicts for parent rows
      let total = own;
      if (canHaveChildren(r.planningMode)) {
        const descIds = getDescendantIds(r.id, requests, childrenById);
        for (const dId of descIds) {
          total += storeConflicts.get(dId)?.length ?? 0;
        }
      }
      if (total > 0) map.set(r.id, total);
    }
    return map;
  }, [requests, childrenById, storeConflicts]);

  // Expand all parent nodes on initial load
  useEffect(() => {
    if (hasInitializedTreeExpansionRef.current) return;
    if (requests.length > 0 && expandedIds.size === 0) {
      const parentIds = requests
        .filter(r => canHaveChildren(r.planningMode) && requests.some(c => c.parentRequestId === r.id))
        .map(r => r.id);
      if (parentIds.length > 0) expandAll(parentIds);
      hasInitializedTreeExpansionRef.current = true;
    }
  }, [requests, expandedIds.size, expandAll]);

  const isEmpty = viewMode === "tree"
    ? visibleEntries.length === 0
    : filteredRequests.length === 0;

  return (
    <TooltipProvider delayDuration={300}>
    <PageLayout>
      <PageHeader
        title="Requests"
        description="Organize tasks and groups and track their schedules."
      />

      {/* Toolbar: search (left) · expand/collapse-all + view toggle + primary (right) */}
      <div className="flex items-center gap-3 mb-4 shrink-0">
        <div className="relative max-w-sm flex-1">
          <Search className="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-muted-foreground" />
          <Input
            placeholder="Search requests..."
            aria-label="Search requests"
            value={searchQuery}
            onChange={(e) => setSearchQuery(e.target.value)}
            className="pl-9"
          />
        </div>

        <div className="ml-auto flex items-center gap-2">
          {viewMode === "tree" && (
            <div className="flex items-center gap-1">
              <Tooltip>
                <TooltipTrigger asChild>
                  <Button
                    variant="ghost"
                    size="icon-sm"
                    onClick={() => expandAll(expandableIds)}
                    aria-label="Expand all"
                  >
                    <ChevronsDown className="h-4 w-4" />
                  </Button>
                </TooltipTrigger>
                <TooltipContent>Expand all (*)</TooltipContent>
              </Tooltip>
              <Tooltip>
                <TooltipTrigger asChild>
                  <Button
                    variant="ghost"
                    size="icon-sm"
                    onClick={() => collapseAll()}
                    aria-label="Collapse all"
                  >
                    <ChevronsUp className="h-4 w-4" />
                  </Button>
                </TooltipTrigger>
                <TooltipContent>Collapse all</TooltipContent>
              </Tooltip>
            </div>
          )}

          <div className="flex border rounded-md" role="group" aria-label="View mode">
            <Tooltip>
              <TooltipTrigger asChild>
                <Button
                  variant={viewMode === "tree" ? "secondary" : "ghost"}
                  size="sm"
                  className="rounded-r-none"
                  onClick={() => setViewMode("tree")}
                  aria-label="Tree view"
                  aria-pressed={viewMode === "tree"}
                >
                  <TreePine className="h-4 w-4" />
                </Button>
              </TooltipTrigger>
              <TooltipContent>Tree view</TooltipContent>
            </Tooltip>
            <Tooltip>
              <TooltipTrigger asChild>
                <Button
                  variant={viewMode === "list" ? "secondary" : "ghost"}
                  size="sm"
                  className="rounded-l-none"
                  onClick={() => setViewMode("list")}
                  aria-label="List view"
                  aria-pressed={viewMode === "list"}
                >
                  <List className="h-4 w-4" />
                </Button>
              </TooltipTrigger>
              <TooltipContent>List view</TooltipContent>
            </Tooltip>
          </div>

          <Button onClick={() => handleCreateRequest('leaf')} disabled={!canEdit}>
            <Plus className="h-4 w-4 mr-2" />
            New Request
          </Button>
        </div>
      </div>

      {/* Body: full-width view. Row click opens the RequestFormDialog below —
          view mode for viewers, edit mode for editors. */}
      <div className="flex-1 overflow-hidden min-h-0">
        {/* Single scroll owner regardless of view mode: the container never scrolls
            (overflow-hidden); the tree owns its own scroll, and the list view is
            wrapped in its own bounded ScrollArea below. */}
        <div className="flex-1 min-h-0 overflow-hidden h-full">
          {isLoading && requests.length === 0 ? (
            <LoadingSpinner fullScreen={false} message="Loading requests…" />
          ) : errorMessage ? (
            <div className="flex flex-col items-center justify-center h-full p-12 text-center">
              <div className="text-destructive mb-4">⚠️</div>
              <h3 className="text-lg font-medium mb-2">Error loading requests</h3>
              <p className="text-muted-foreground mb-4">{errorMessage}</p>
              <Button onClick={() => refetchRequests()} variant="outline">Try again</Button>
            </div>
          ) : isEmpty ? (
            <div className="flex h-full flex-col items-center justify-center p-12">
              <EmptyState
                icon={<Calendar className="h-12 w-12 text-muted-foreground" />}
                message={
                  <>
                    <h3 className="mb-2 text-lg font-medium text-foreground">No requests found</h3>
                    <p>
                      {searchQuery
                        ? "Try adjusting your search"
                        : "Get started by creating your first request"}
                    </p>
                  </>
                }
                action={
                  !searchQuery ? (
                    <Button onClick={() => handleCreateRequest()} disabled={!canEdit}>
                      <Plus className="h-4 w-4 mr-2" />
                      Create Request
                    </Button>
                  ) : undefined
                }
              />
            </div>
          ) : viewMode === "tree" ? (
            <RequestTreeView
              entries={visibleEntries}
              allRequests={requests}
              selectedId={selectedId}
              conflictMap={conflictMap}
              onOpenConflicts={handleOpenConflicts}
              onToggle={toggle}
              onSelect={handleSelect}
              onEdit={handleEditRequest}
              onDelete={handleDeleteRequest}
              onDrop={handleDrop}
            />
          ) : (
            <ScrollArea type="auto" className="h-full">
              <RequestListView
                requests={filteredRequests}
                selectedId={selectedId}
                onSelect={handleSelect}
                onEdit={handleEditRequest}
                onDelete={handleDeleteRequest}
                onNavigateToParent={handleNavigateToRequest}
              />
            </ScrollArea>
          )}
        </div>
      </div>

      {/* Form Dialog — the single view+edit surface for a request (create or edit). */}
      <RequestFormDialog
        key={
          dialog?.kind === "edit"
            ? dialog.request.id
            : dialog?.kind === "create"
              ? (dialog.parent?.id ?? dialog.defaultMode ?? 'new')
              : 'closed'
        }
        open={dialog?.kind === "edit" || dialog?.kind === "create"}
        onOpenChange={(open) => { if (!open) setDialog(null); }}
        request={dialog?.kind === "edit" ? dialog.request : null}
        parentRequest={dialog?.kind === "create" ? dialog.parent : null}
        defaultPlanningMode={dialog?.kind === "create" ? dialog.defaultMode : undefined}
        canEdit={canEdit}
        allRequests={requests}
        onNavigate={handleDialogNavigate}
        onSave={handleSaveRequest}
      />

      {/* Delete Confirmation Dialog */}
      <ConfirmDialog
        open={dialog?.kind === "delete"}
        onOpenChange={(open) => { if (!open) setDialog(null); }}
        title="Delete request"
        description={
          dialog?.kind === "delete"
            ? (() => {
                const descendantIds = getDescendantIds(dialog.request.id, requests, childrenById);
                return descendantIds.length > 0
                  ? `This will permanently delete "${dialog.request.name}" and ${descendantIds.length} child request${descendantIds.length === 1 ? '' : 's'}. This cannot be undone.`
                  : `This will permanently delete "${dialog.request.name}". This cannot be undone.`;
              })()
            : ''
        }
        confirmLabel="Delete"
        destructive
        onConfirm={handleConfirmDelete}
      />
    </PageLayout>
    </TooltipProvider>
  );
}
