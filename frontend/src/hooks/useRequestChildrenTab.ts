import { useMemo, useRef, useState } from "react";
import { useMutation } from "@tanstack/react-query";
import { useVirtualizer } from "@tanstack/react-virtual";
import { createChildRequest, moveRequest } from "@foundation/src/lib/api/request-api";
import { REQUEST_DERIVED_QUERY_KEYS } from "@foundation/src/lib/core/invalidate-request-data";
import { getAncestorIds, getNextSortOrder } from "@foundation/src/domain/request-tree";
import { errorMessage } from "@foundation/src/hooks/mutation-utils";
import { useInvalidateRequestData } from "@foundation/src/hooks/useRequests";
import { logger } from "@foundation/src/lib/core/logger";
import type { Request } from "@foundation/src/types/requests";

export interface UseRequestChildrenTabOptions {
  open: boolean;
  /** The edited request; absent in create mode, where changes are queued until the save. */
  request: Request | null | undefined;
  allRequests: Request[] | undefined;
  requestsById: Map<string, Request>;
  /**
   * The dialog's inline error. An edit-mode change commits at once and the dialog stays
   * open, so its failure is shown there rather than toasted (one surface per error).
   */
  setError: (message: string | null) => void;
}

/**
 * The request dialog's Children tab: quick-add a child, pull existing parentless requests into
 * the group, remove a child from it.
 *
 * In edit mode each action commits at once through a mutation whose `meta` re-reads the request
 * data. In create mode there is no group id yet, so the tab queues names and ids, and
 * `commitPending` creates and reparents them once the group is saved — the dialog closes on
 * that save, so a failed item is toasted by the central MutationCache, named in the title.
 * The queue is committed item by item and the request data is re-read once after it, not once
 * per item.
 */
export function useRequestChildrenTab({
  open,
  request,
  allRequests,
  requestsById,
  setError,
}: UseRequestChildrenTabOptions) {
  // Inline quick-add child name.
  const [newChildName, setNewChildName] = useState("");
  // Create mode only: child names queued, created under the new group right after it is saved.
  const [pendingChildren, setPendingChildren] = useState<string[]>([]);
  // Create mode only: existing request ids queued to reparent under the new group once saved.
  const [pendingExistingIds, setPendingExistingIds] = useState<string[]>([]);
  // Inline "Add existing" picker: pull parentless requests into this group.
  const [addExistingOpen, setAddExistingOpen] = useState(false);
  const [addExistingSelected, setAddExistingSelected] = useState<Set<string>>(new Set());
  const [addExistingSearch, setAddExistingSearch] = useState("");

  // Opening starts the tab clean: a render-phase update, not an effect (see useEntityFormDialog.ts).
  const [syncedOpen, setSyncedOpen] = useState(open);
  if (syncedOpen !== open) {
    setSyncedOpen(open);
    if (open) {
      setPendingChildren([]);
      setPendingExistingIds([]);
      setNewChildName("");
      setAddExistingOpen(false);
      setAddExistingSelected(new Set());
      setAddExistingSearch("");
    }
  }

  const invalidateRequestData = useInvalidateRequestData();

  const createChild = ({ parentId, name, sortOrder }: { parentId: string; name: string; sortOrder: number }) =>
    createChildRequest(parentId, name, sortOrder);

  // Edit mode: the failure is shown inline, so no `meta` toast.
  const addChildMutation = useMutation({
    mutationFn: createChild,
    meta: { invalidates: REQUEST_DERIVED_QUERY_KEYS },
  });

  const addExistingMutation = useMutation({
    mutationFn: async ({ parentId, ids, baseSortOrder }: { parentId: string; ids: string[]; baseSortOrder: number }) => {
      // allRequests only refreshes after the invalidation, so the base is computed once and
      // offset per item — otherwise every move gets the same sortOrder.
      for (const [index, id] of ids.entries()) {
        await moveRequest(id, { newParentRequestId: parentId, sortOrder: baseSortOrder + index });
      }
    },
    meta: { invalidates: REQUEST_DERIVED_QUERY_KEYS },
  });

  const removeChildMutation = useMutation({
    mutationFn: ({ childId, sortOrder }: { childId: string; sortOrder: number }) =>
      moveRequest(childId, { newParentRequestId: null, sortOrder }),
    meta: { invalidates: REQUEST_DERIVED_QUERY_KEYS },
  });

  // Create mode: the dialog has closed, so the failure is toasted by name. No `invalidates`:
  // `commitPending` re-reads the request data once after the whole queue.
  const commitChildMutation = useMutation({
    mutationFn: createChild,
    meta: {
      errorMessage: (variables) => `Failed to create child "${(variables as { name: string }).name}"`,
    },
    onError: (error) => logger.error("Failed to create queued child request:", error),
  });

  const commitExistingMutation = useMutation({
    mutationFn: ({ parentId, id, sortOrder }: { parentId: string; id: string; name: string; sortOrder: number }) =>
      moveRequest(id, { newParentRequestId: parentId, sortOrder }),
    meta: {
      errorMessage: (variables) => `Failed to add "${(variables as { name: string }).name}"`,
    },
    onError: (error) => logger.error("Failed to reparent queued request:", error),
  });

  const handleAddChild = async () => {
    const name = newChildName.trim();
    if (!name) return;
    // Create mode: queue locally; children are created together with the group.
    if (!request) {
      setPendingChildren((prev) => [...prev, name]);
      setNewChildName("");
      return;
    }
    if (!allRequests) return;
    setError(null);
    try {
      await addChildMutation.mutateAsync({
        parentId: request.id,
        name,
        sortOrder: getNextSortOrder(request.id, allRequests),
      });
      setNewChildName("");
    } catch (error) {
      logger.error("Failed to add child request:", error);
      setError(errorMessage(error));
    }
  };

  // Eligible existing requests for the inline "Add existing" picker: parentless
  // ("not in a group yet"), not this request, not an ancestor (no cycle), and
  // not already queued. Parentless keeps both Tasks and Groups while excluding
  // current children. Computed only while the picker is open, and O(n): a
  // parentless candidate can only create a cycle if it's one of this request's
  // ancestors (its root), so we exclude the ancestor set instead of running a
  // per-candidate descendant walk.
  const addExistingBase = useMemo(() => {
    if (!addExistingOpen || !allRequests) return [] as Request[];
    const ancestors = request
      ? new Set(getAncestorIds(request.id, allRequests, requestsById))
      : new Set<string>();
    return allRequests.filter((r) =>
      !r.parentRequestId &&
      r.id !== request?.id &&
      !ancestors.has(r.id) &&
      !pendingExistingIds.includes(r.id),
    );
  }, [addExistingOpen, allRequests, request, pendingExistingIds, requestsById]);

  const addExistingCandidates = useMemo(() => {
    const q = addExistingSearch.trim().toLowerCase();
    if (!q) return addExistingBase;
    return addExistingBase.filter(
      (r) => r.name.toLowerCase().includes(q) || r.description?.toLowerCase().includes(q),
    );
  }, [addExistingBase, addExistingSearch]);

  // Virtualize the candidate list so the picker opens instantly regardless of
  // how many requests the tenant has (only ~15 rows mount at once).
  const addExistingViewportRef = useRef<HTMLDivElement>(null);
  // TanStack Virtual's API is not memoizable, so the compiler skips this hook. Nothing to fix.
  // eslint-disable-next-line react-hooks/incompatible-library
  const addExistingVirtualizer = useVirtualizer({
    count: addExistingCandidates.length,
    getScrollElement: () => addExistingViewportRef.current,
    estimateSize: () => 44,
    overscan: 8,
  });

  // Existing requests queued in create mode, resolved for display in the
  // "to be added" list. Order follows pendingExistingIds.
  const pendingExistingRequests = useMemo(() => {
    if (!pendingExistingIds.length || !allRequests) return [] as Request[];
    return pendingExistingIds.map((id) => requestsById.get(id)).filter(Boolean) as Request[];
  }, [pendingExistingIds, allRequests, requestsById]);

  const toggleAddExistingSelected = (id: string) => {
    setAddExistingSelected((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  };

  const closeAddExisting = () => {
    setAddExistingSelected(new Set());
    setAddExistingSearch("");
    setAddExistingOpen(false);
  };

  const handleAddExisting = async () => {
    if (addExistingSelected.size === 0) return;
    // Create mode: no group id yet — queue and reparent on save.
    if (!request) {
      setPendingExistingIds((prev) => [...prev, ...[...addExistingSelected].filter((id) => !prev.includes(id))]);
      closeAddExisting();
      return;
    }
    if (!allRequests) return;
    setError(null);
    try {
      await addExistingMutation.mutateAsync({
        parentId: request.id,
        ids: [...addExistingSelected],
        baseSortOrder: getNextSortOrder(request.id, allRequests),
      });
      closeAddExisting();
    } catch (error) {
      logger.error("Failed to add existing requests:", error);
      setError(errorMessage(error));
    }
  };

  const handleRemoveChild = async (child: Request) => {
    if (!allRequests) return;
    setError(null);
    // Reparent to root (reversible). sortOrder = end of the current root list.
    const sortOrder = getNextSortOrder(null, allRequests);
    try {
      await removeChildMutation.mutateAsync({ childId: child.id, sortOrder });
    } catch (error) {
      logger.error("Failed to remove child from group:", error);
      setError(errorMessage(error));
    }
  };

  /**
   * Create mode, after the group is saved: create the queued children and reparent the queued
   * existing requests under it. Each failure is toasted and skipped — the group and whatever
   * succeeded are kept, and the rest can be re-added from the group's dialog. The request data
   * is re-read once at the end, not once per item.
   */
  const commitPending = async (groupId: string) => {
    if (!pendingChildren.length && !pendingExistingIds.length) return;
    for (const [index, name] of pendingChildren.entries()) {
      await commitChildMutation
        .mutateAsync({ parentId: groupId, name, sortOrder: index })
        .catch(() => undefined);
    }
    for (const [index, id] of pendingExistingIds.entries()) {
      await commitExistingMutation
        .mutateAsync({
          parentId: groupId,
          id,
          name: requestsById.get(id)?.name ?? "request",
          sortOrder: pendingChildren.length + index,
        })
        .catch(() => undefined);
    }
    await invalidateRequestData();
  };

  return {
    newChildName,
    setNewChildName,
    isAddingChild: addChildMutation.isPending,
    handleAddChild,
    addExistingOpen,
    setAddExistingOpen,
    addExistingSearch,
    setAddExistingSearch,
    addExistingCandidates,
    addExistingSelected,
    toggleAddExistingSelected,
    addExistingViewportRef,
    addExistingVirtualizer,
    isAddingExisting: addExistingMutation.isPending,
    handleAddExisting,
    pendingChildren,
    pendingExistingRequests,
    setPendingChildren,
    setPendingExistingIds,
    handleRemoveChild,
    /** Create mode: queued work that Discard legitimately drops, so it counts as unsaved. */
    hasPending: !request && (pendingChildren.length > 0 || pendingExistingIds.length > 0),
    commitPending,
  };
}

/** What the Children tab reads and calls. */
export type RequestChildrenTab = ReturnType<typeof useRequestChildrenTab>;
