import { useCallback, useState, type ReactNode } from "react";
import {
  RequestFormDialog,
  type RequestFormData,
} from "@foundation/src/components/requests/RequestFormDialog";
import { saveRequestVariables, useSaveRequest } from "@foundation/src/hooks/useRequests";
import type { Conflict, Request } from "@foundation/src/types/requests";

interface UseRequestEditorResult {
  /** Open the request dialog — the dialog itself decides edit or read-only view
   *  mode (`useCanEdit()`). Pass the request's conflicts (from the registry) to surface
   *  indicators in the edit form. */
  open: (request: Request, conflicts?: Conflict[]) => void;
  /**
   * The dialog as a ReactNode. Mount once at the page root — it portals to
   * document.body so the mount point doesn't affect layout.
   */
  dialogs: ReactNode;
}

/**
 * Centralises the open / edit / view-request dialog flow shared by
 * UtilizationPage and ConflictsPage.
 *
 * Owns: dialog state and the save, through `useSaveRequest` like every request save. Always opens
 * `RequestFormDialog`, which decides edit vs. view mode from `useCanEdit()`. These callers
 * open a single request by id (no tree), so `allRequests`/`onNavigate` are
 * omitted and the dialog's breadcrumb, Children tab, Dependencies tab, and derived
 * rollups hide. The Dependencies tab needs `allRequests` to offer predecessors, so it
 * stays hidden here rather than opening onto an empty picker.
 */
export function useRequestEditor(): UseRequestEditorResult {
  const [request, setRequest] = useState<Request | null>(null);
  const [conflicts, setConflicts] = useState<Conflict[]>([]);
  const [isOpen, setIsOpen] = useState(false);

  const open = useCallback((next: Request, nextConflicts: Conflict[] = []) => {
    setRequest(next);
    setConflicts(nextConflicts);
    setIsOpen(true);
  }, []);

  const { mutateAsync: saveRequest } = useSaveRequest({
    onSuccess: () => {
      setIsOpen(false);
      setRequest(null);
    },
  });

  const handleSave = useCallback(
    async (data: RequestFormData) => {
      if (!request) return;
      // Returned so the dialog can say when the scheduler moved the dates that were typed.
      return saveRequest(saveRequestVariables(data, request));
    },
    [request, saveRequest],
  );

  const dialogs = (
    <RequestFormDialog
      key={request?.id ?? "new"}
      open={isOpen}
      onOpenChange={(next) => {
        setIsOpen(next);
        if (!next) setRequest(null);
      }}
      request={request}
      conflicts={conflicts}
      onSave={handleSave}
    />
  );

  return { open, dialogs };
}
