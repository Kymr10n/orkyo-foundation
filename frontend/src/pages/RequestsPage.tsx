import {
    RequestFormDialog,
    type RequestFormData,
} from "@/components/requests/RequestFormDialog";
import { RequestDetailPanel } from "@/components/requests/RequestDetailPanel";
import { RequestTreeView } from "@/components/requests/RequestTreeView";
import { RequestListView } from "@/components/requests/RequestListView";
import { AddExistingRequestsDialog } from "@/components/requests/AddExistingRequestsDialog";
import { MoveToDialog } from "@/components/requests/MoveToDialog";
import {
    AlertDialog,
    AlertDialogAction,
    AlertDialogCancel,
    AlertDialogContent,
    AlertDialogDescription,
    AlertDialogFooter,
    AlertDialogHeader,
    AlertDialogTitle,
} from "@/components/ui/alert-dialog";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import {
    Tooltip,
    TooltipContent,
    TooltipProvider,
    TooltipTrigger,
} from "@/components/ui/tooltip";
import {
    createRequest,
    deleteRequest,
    getRequests,
    moveRequest,
    updateRequest,
} from "@/lib/api/request-api";
import { useAppStore } from "@/store/app-store";
import type {
    CreateRequestRequest,
    PlanningMode,
    Request,
} from "@/types/requests";
import {
    buildRequestTree,
    flattenTree,
    flattenVisibleTree,
    getAncestorIds,
    getDescendantIds,
    getNextSortOrder,
    canHaveChildren,
} from "@/domain/request-tree";
import { useRequestTreeStore } from "@/store/request-tree-store";
import {
    Calendar,
    Filter,
    List,
    Plus,
    Search,
    TreePine,
} from "lucide-react";
import { useEffect, useState, useMemo, useCallback, useRef } from "react";
import { useNavigate, useSearchParams } from "react-router-dom";
import { useQueryClient } from "@tanstack/react-query";
import { useExportHandler, useImportHandler } from "@/hooks/useImportExport";
import { exportRequests, importRequests } from "@/lib/utils/export-handlers";
import { buildCreatePayload, buildUpdatePayload } from "@/lib/utils/utils";
import { deleteRequestSubtree } from "@/lib/api/request-api";
import { logger } from "@/lib/core/logger";

type ViewMode = "tree" | "list";

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
  | { kind: "addExisting"; parent: Request }
  | { kind: "delete"; request: Request }
  | { kind: "moveTo"; request: Request };

// ---------------------------------------------------------------------------
// Page
// ---------------------------------------------------------------------------

export function RequestsPage() {
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const [searchParams, setSearchParams] = useSearchParams();
  const [requests, setRequests] = useState<Request[]>([]);
  const [searchQuery, setSearchQuery] = useState("");
  const [debouncedSearch, setDebouncedSearch] = useState("");
  const [viewMode, setViewMode] = useState<ViewMode>("tree");
  const [dialog, setDialog] = useState<Dialog | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const hasInitializedTreeExpansionRef = useRef(false);

  const { expandedIds, toggle, expandAncestors, expandAll, selectedId, setSelectedId } =
    useRequestTreeStore();

  // Debounce search input
  useEffect(() => {
    const timer = setTimeout(() => setDebouncedSearch(searchQuery), 300);
    return () => clearTimeout(timer);
  }, [searchQuery]);

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
      await loadRequests();
      alert(`Successfully imported ${importedRequests.length} requests`);
    } catch (error) {
      logger.error('Import failed:', error);
      alert(error instanceof Error ? error.message : 'Failed to import requests');
    }
  });

  // Load requests from API
  useEffect(() => {
    loadRequests();
  }, []);

  // Handle ?edit=<id> query param from global search
  useEffect(() => {
    const editId = searchParams.get('edit');
    if (editId && requests.length > 0 && !loading) {
      const requestToEdit = requests.find(r => r.id === editId);
      if (requestToEdit) {
        setDialog({ kind: "edit", request: requestToEdit });
        searchParams.delete('edit');
        setSearchParams(searchParams, { replace: true });
      }
    }
  }, [searchParams, requests, loading, setSearchParams]);

  // Build tree + flatten, respecting expanded state and search (tree mode only)
  const visibleEntries = useMemo(() => {
    if (viewMode !== "tree") return [];

    const tree = buildRequestTree(requests);

    if (debouncedSearch) {
      const query = debouncedSearch.toLowerCase();
      const matchIds = new Set(
        requests
          .filter(r =>
            r.name.toLowerCase().includes(query) ||
            r.description?.toLowerCase().includes(query)
          )
          .map(r => r.id)
      );

      if (matchIds.size === 0) return [];

      const byId = new Map(requests.map(r => [r.id, r]));
      const ancestorIds = new Set<string>();
      for (const id of matchIds) {
        for (const aid of getAncestorIds(id, requests, byId)) {
          ancestorIds.add(aid);
        }
      }

      const allFlat = flattenTree(tree);
      return allFlat.filter(e =>
        matchIds.has(e.request.id) || ancestorIds.has(e.request.id)
      );
    }

    // No search — use flattenVisibleTree which respects expand/collapse state
    return flattenVisibleTree(tree, expandedIds);
  }, [requests, debouncedSearch, expandedIds, viewMode]);

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

  // Auto-expand ancestors when search matches (tree mode)
  useEffect(() => {
    if (viewMode !== "tree" || !debouncedSearch) return;
    const query = debouncedSearch.toLowerCase();
    const matchIds = requests
      .filter(r =>
        r.name.toLowerCase().includes(query) ||
        r.description?.toLowerCase().includes(query)
      )
      .map(r => r.id);
    if (matchIds.length === 0) return;

    const byId = new Map(requests.map(r => [r.id, r]));
    const ancestorIds = new Set<string>();
    for (const id of matchIds) {
      for (const aid of getAncestorIds(id, requests, byId)) {
        ancestorIds.add(aid);
      }
    }
    if (ancestorIds.size > 0) expandAncestors([...ancestorIds]);
  }, [debouncedSearch, requests, expandAncestors, viewMode]);

  const loadRequests = async () => {
    try {
      setLoading(true);
      setError(null);
      const data = await getRequests(true);
      setRequests(data);
    } catch (err) {
      logger.error("Failed to load requests:", err);
      setError(err instanceof Error ? err.message : "Failed to load requests");
    } finally {
      setLoading(false);
    }
  };

  const handleCreateRequest = useCallback((defaultMode: PlanningMode = 'leaf') => {
    setDialog({ kind: "create", parent: null, defaultMode });
  }, []);

  const handleAddChild = useCallback((parent: Request) => {
    setDialog({ kind: "create", parent });
  }, []);

  const handleAddSibling = useCallback((request: Request) => {
    const parent = request.parentRequestId
      ? requests.find(r => r.id === request.parentRequestId) ?? null
      : null;
    setDialog({ kind: "create", parent });
  }, [requests]);

  const handleAddExisting = useCallback((parent: Request) => {
    setDialog({ kind: "addExisting", parent });
  }, []);

  const handleMoveTo = useCallback((request: Request) => {
    setDialog({ kind: "moveTo", request });
  }, []);

  const handleConfirmMoveTo = useCallback(async (targetParentId: string | null) => {
    if (dialog?.kind !== "moveTo") return;
    const req = dialog.request;
    setDialog(null);

    try {
      setLoading(true);
      setError(null);
      await moveRequest(req.id, {
        newParentRequestId: targetParentId,
        sortOrder: targetParentId ? getNextSortOrder(targetParentId, requests) : 0,
      });
      await loadRequests();
      queryClient.invalidateQueries({ queryKey: ["requests"] });
      if (targetParentId && !expandedIds.has(targetParentId)) {
        toggle(targetParentId);
      }
    } catch (err) {
      logger.error("Failed to move request:", err);
      setError(err instanceof Error ? err.message : "Failed to move request");
    } finally {
      setLoading(false);
    }
  }, [dialog, requests, queryClient, expandedIds, toggle]);

  const handleConfirmAddExisting = useCallback(async (requestIds: string[]) => {
    if (dialog?.kind !== "addExisting") return;
    const parent = dialog.parent;
    try {
      setLoading(true);
      setError(null);
      for (const id of requestIds) {
        await moveRequest(id, {
          newParentRequestId: parent.id,
          sortOrder: getNextSortOrder(parent.id, requests),
        });
      }
      setDialog(null);
      await loadRequests();
      queryClient.invalidateQueries({ queryKey: ["requests"] });
      // Auto-expand the parent so user sees the newly added children
      if (!expandedIds.has(parent.id)) {
        toggle(parent.id);
      }
    } catch (err) {
      logger.error("Failed to move requests:", err);
      setError(err instanceof Error ? err.message : "Failed to move requests");
    } finally {
      setLoading(false);
    }
  }, [dialog, requests, queryClient, expandedIds, toggle]);

  const handleDrop = useCallback(async (draggedId: string, targetId: string) => {
    try {
      setLoading(true);
      setError(null);
      await moveRequest(draggedId, {
        newParentRequestId: targetId,
        sortOrder: getNextSortOrder(targetId, requests),
      });
      await loadRequests();
      queryClient.invalidateQueries({ queryKey: ["requests"] });
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
      const descendantIds = getDescendantIds(request.id, requests);
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

      await loadRequests();
      queryClient.invalidateQueries({ queryKey: ["requests"] });
    } catch (err) {
      logger.error("Failed to delete request:", err);
      setError(err instanceof Error ? err.message : "Failed to delete request");
    } finally {
      setLoading(false);
    }
  }, [dialog, requests, queryClient, selectedId, setSelectedId]);

  const handleSaveRequest = useCallback(async (data: RequestFormData) => {
    try {
      setLoading(true);
      setError(null);

      if (dialog?.kind === "edit") {
        await updateRequest(dialog.request.id, buildUpdatePayload(data));
      } else {
        await createRequest(buildCreatePayload(data));
      }

      await loadRequests();
      queryClient.invalidateQueries({ queryKey: ["requests"] });
      setDialog(null);
    } catch (err) {
      logger.error("Failed to save request:", err);
      setError(err instanceof Error ? err.message : "Failed to save request");
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
  }, [requests, expandAncestors, setSelectedId, viewMode]);

  const selectedRequest = useMemo(
    () => (selectedId ? requests.find((r) => r.id === selectedId) ?? null : null),
    [selectedId, requests],
  );

  // Build conflict count map for tree view (own + descendant conflicts)
  const storeConflicts = useAppStore((s) => s.conflicts);

  const handleOpenConflicts = useCallback(
    (requestId: string) => {
      const candidateIds = [requestId, ...getDescendantIds(requestId, requests)];
      const targetRequestId =
        candidateIds.find((id) => (storeConflicts.get(id)?.length ?? 0) > 0) ?? requestId;
      const targetConflictId = storeConflicts.get(targetRequestId)?.[0]?.id;

      const params = new URLSearchParams();
      params.set("requestId", targetRequestId);
      if (targetConflictId) {
        params.set("conflictId", targetConflictId);
      }

      navigate(`/conflicts?${params.toString()}`);
    },
    [navigate, requests, storeConflicts],
  );

  const conflictMap = useMemo(() => {
    const map = new Map<string, number>();
    for (const r of requests) {
      const own = storeConflicts.get(r.id)?.length ?? 0;
      // Bubble up: count descendant conflicts for parent rows
      let total = own;
      if (canHaveChildren(r.planningMode)) {
        const descIds = getDescendantIds(r.id, requests);
        for (const dId of descIds) {
          total += storeConflicts.get(dId)?.length ?? 0;
        }
      }
      if (total > 0) map.set(r.id, total);
    }
    return map;
  }, [requests, storeConflicts]);

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
    <TooltipProvider>
    <div className="rounded-2xl border bg-card overflow-hidden flex flex-col h-full">
      {/* Header */}
      <div className="p-6 border-b">
        <div className="flex items-center justify-between mb-4">
          <h1 className="text-xl font-semibold">Requests</h1>
          <div className="flex gap-2">
            {/* View mode toggle */}
            <div className="flex border rounded-md">
              <Tooltip>
                <TooltipTrigger asChild>
                  <Button
                    variant={viewMode === "tree" ? "secondary" : "ghost"}
                    size="sm"
                    className="rounded-r-none"
                    onClick={() => setViewMode("tree")}
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
                  >
                    <List className="h-4 w-4" />
                  </Button>
                </TooltipTrigger>
                <TooltipContent>List view</TooltipContent>
              </Tooltip>
            </div>

            <Button onClick={() => handleCreateRequest('leaf')}>
              <Plus className="h-4 w-4 mr-2" />
              New Task
            </Button>

            <Button onClick={() => handleCreateRequest('summary')}>
              <Plus className="h-4 w-4 mr-2" />
              New Group
            </Button>
          </div>
        </div>

        {/* Search and Filters */}
        <div className="flex gap-3">
          <div className="relative flex-1">
            <Search className="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-muted-foreground" />
            <Input
              placeholder="Search requests..."
              value={searchQuery}
              onChange={(e) => setSearchQuery(e.target.value)}
              className="pl-9"
            />
          </div>
          <Button variant="outline" size="sm">
            <Filter className="h-4 w-4 mr-2" />
            Filters
          </Button>
        </div>
      </div>

      {/* Body: View + Detail Panel */}
      <div className="flex-1 flex overflow-hidden">
        {/* Main content area */}
        <div className={`flex-1 overflow-hidden ${selectedRequest ? 'min-w-0' : ''}`}>
          {loading && requests.length === 0 ? (
            <div className="flex flex-col items-center justify-center h-full p-12 text-center">
              <div className="animate-spin h-8 w-8 border-4 border-primary border-t-transparent rounded-full mb-4" />
              <p className="text-muted-foreground">Loading requests...</p>
            </div>
          ) : error ? (
            <div className="flex flex-col items-center justify-center h-full p-12 text-center">
              <div className="text-destructive mb-4">⚠️</div>
              <h3 className="text-lg font-medium mb-2">Error loading requests</h3>
              <p className="text-muted-foreground mb-4">{error}</p>
              <Button onClick={loadRequests} variant="outline">Try Again</Button>
            </div>
          ) : isEmpty ? (
            <div className="flex flex-col items-center justify-center h-full p-12 text-center">
              <Calendar className="h-12 w-12 text-muted-foreground mb-4" />
              <h3 className="text-lg font-medium mb-2">No requests found</h3>
              <p className="text-muted-foreground mb-4">
                {searchQuery
                  ? "Try adjusting your search"
                  : "Get started by creating your first request"}
              </p>
              {!searchQuery && (
                <Button onClick={() => handleCreateRequest()}>
                  <Plus className="h-4 w-4 mr-2" />
                  Create Request
                </Button>
              )}
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
              onAddChild={handleAddChild}
              onAddSibling={handleAddSibling}
              onAddExisting={handleAddExisting}
              onMoveTo={handleMoveTo}
              onDrop={handleDrop}
            />
          ) : (
            <RequestListView
              requests={filteredRequests}
              selectedId={selectedId}
              onSelect={handleSelect}
              onEdit={handleEditRequest}
              onDelete={handleDeleteRequest}
              onAddChild={handleAddChild}
              onAddExisting={handleAddExisting}
            />
          )}
        </div>

        {/* Detail Panel */}
        {selectedRequest && (
          <div className="w-[360px] flex-shrink-0">
            <RequestDetailPanel
              request={selectedRequest}
              allRequests={requests}
              onEdit={handleEditRequest}
              onNavigate={handleNavigateToRequest}
              onClose={() => setSelectedId(null)}
            />
          </div>
        )}
      </div>

      {/* Form Dialog (create or edit) */}
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
        onSave={handleSaveRequest}
      />

      {/* Add Existing Requests Dialog */}
      {dialog?.kind === "addExisting" && (
        <AddExistingRequestsDialog
          open
          onOpenChange={(open) => { if (!open) setDialog(null); }}
          parentRequest={dialog.parent}
          allRequests={requests}
          onConfirm={handleConfirmAddExisting}
        />
      )}

      {/* Move To Dialog */}
      {dialog?.kind === "moveTo" && (
        <MoveToDialog
          open
          onOpenChange={(open) => { if (!open) setDialog(null); }}
          request={dialog.request}
          allRequests={requests}
          onConfirm={handleConfirmMoveTo}
        />
      )}

      {/* Delete Confirmation Dialog */}
      <AlertDialog
        open={dialog?.kind === "delete"}
        onOpenChange={(open) => { if (!open) setDialog(null); }}
      >
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Delete request</AlertDialogTitle>
            <AlertDialogDescription>
              {dialog?.kind === "delete" && (() => {
                const descendantIds = getDescendantIds(dialog.request.id, requests);
                return descendantIds.length > 0
                  ? `This will permanently delete "${dialog.request.name}" and ${descendantIds.length} child request${descendantIds.length === 1 ? '' : 's'}. This cannot be undone.`
                  : `This will permanently delete "${dialog.request.name}". This cannot be undone.`;
              })()}
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>Cancel</AlertDialogCancel>
            <AlertDialogAction
              className="bg-destructive text-destructive-foreground hover:bg-destructive/90"
              onClick={handleConfirmDelete}
            >
              Delete
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </div>
    </TooltipProvider>
  );
}
