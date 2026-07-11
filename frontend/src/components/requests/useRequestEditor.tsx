import { useCallback, useState, type ReactNode } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { useAuth } from "@foundation/src/contexts/AuthContext";
import { TENANT_ROLE } from "@foundation/src/hooks/usePermissions";
import {
  RequestFormDialog,
  type RequestFormData,
} from "@foundation/src/components/requests/RequestFormDialog";
import { updateRequest } from "@foundation/src/lib/api/request-api";
import { buildUpdatePayload } from "@foundation/src/lib/utils/utils";
import { invalidateRequestData } from "@foundation/src/lib/core/invalidate-request-data";
import type { Conflict, Request } from "@foundation/src/types/requests";

interface UseRequestEditorResult {
  /** Open the request dialog — edit mode for admin/editor, read-only view mode
   *  otherwise. Pass the request's conflicts (from the registry) to surface
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
 * Owns: dialog state, role gate (admin|editor → edit, otherwise → read-only
 * view), save handler, and React Query invalidation. Always opens
 * `RequestFormDialog` — `canEdit` decides edit vs. view mode. These callers
 * open a single request by id (no tree), so `allRequests`/`onNavigate` are
 * omitted and the dialog's breadcrumb/Children tab/derived rollups hide.
 */
export function useRequestEditor(): UseRequestEditorResult {
  const queryClient = useQueryClient();
  const { membership } = useAuth();
  // Deliberately NOT useCanEdit(): that hook also grants site admins (break-glass)
  // and tenant admins whose membership role differs — this gate is role-only.
  const userCanEdit =
    membership?.role === TENANT_ROLE.Admin || membership?.role === TENANT_ROLE.Editor;

  const [request, setRequest] = useState<Request | null>(null);
  const [conflicts, setConflicts] = useState<Conflict[]>([]);
  const [isOpen, setIsOpen] = useState(false);

  const open = useCallback((next: Request, nextConflicts: Conflict[] = []) => {
    setRequest(next);
    setConflicts(nextConflicts);
    setIsOpen(true);
  }, []);

  const handleSave = useCallback(
    async (data: RequestFormData) => {
      if (!request) return;
      await updateRequest(request.id, buildUpdatePayload(data, request.planningMode, request.siteId));
      invalidateRequestData(queryClient);
      setIsOpen(false);
      setRequest(null);
    },
    [request, queryClient],
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
      canEdit={userCanEdit}
      onSave={handleSave}
    />
  );

  return { open, dialogs };
}
