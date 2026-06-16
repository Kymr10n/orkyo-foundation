import { useCallback, useState, type ReactNode } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { useAuth } from "@foundation/src/contexts/AuthContext";
import { RequestDetailsDialog } from "@foundation/src/components/requests/RequestDetailsDialog";
import {
  RequestFormDialog,
  type RequestFormData,
} from "@foundation/src/components/requests/RequestFormDialog";
import { updateRequest } from "@foundation/src/lib/api/request-api";
import { buildUpdatePayload } from "@foundation/src/lib/utils/utils";
import { invalidateRequestData } from "@foundation/src/lib/core/invalidate-request-data";
import type { Conflict, Request } from "@foundation/src/types/requests";

interface UseRequestEditorResult {
  /** Open the edit dialog (admin/editor) or read-only details dialog for a request. Pass the
   *  request's conflicts (from the registry) to surface indicators in the edit form. */
  open: (request: Request, conflicts?: Conflict[]) => void;
  /**
   * Both dialogs as a single ReactNode. Mount once at the page root —
   * they portal to document.body so the mount point doesn't affect layout.
   */
  dialogs: ReactNode;
}

/**
 * Centralises the open / edit / view-request dialog flow shared by
 * UtilizationPage and ConflictsPage.
 *
 * Owns: dialog state, role gate (admin|editor → edit, otherwise → details),
 * save handler, and React Query invalidation.
 */
export function useRequestEditor(): UseRequestEditorResult {
  const queryClient = useQueryClient();
  const { membership } = useAuth();
  const userCanEdit =
    membership?.role === "admin" || membership?.role === "editor";

  const [request, setRequest] = useState<Request | null>(null);
  const [conflicts, setConflicts] = useState<Conflict[]>([]);
  const [isEditOpen, setIsEditOpen] = useState(false);
  const [isDetailsOpen, setIsDetailsOpen] = useState(false);

  const open = useCallback(
    (next: Request, nextConflicts: Conflict[] = []) => {
      setRequest(next);
      setConflicts(nextConflicts);
      if (userCanEdit) {
        setIsEditOpen(true);
      } else {
        setIsDetailsOpen(true);
      }
    },
    [userCanEdit],
  );

  const handleSave = useCallback(
    async (data: RequestFormData) => {
      if (!request) return;
      await updateRequest(request.id, buildUpdatePayload(data, request.planningMode, request.siteId));
      invalidateRequestData(queryClient);
      setIsEditOpen(false);
      setRequest(null);
    },
    [request, queryClient],
  );

  const dialogs = (
    <>
      <RequestFormDialog
        key={request?.id ?? "new"}
        open={isEditOpen}
        onOpenChange={(next) => {
          setIsEditOpen(next);
          if (!next) setRequest(null);
        }}
        request={request}
        conflicts={conflicts}
        onSave={handleSave}
      />
      <RequestDetailsDialog
        open={isDetailsOpen}
        onOpenChange={(next) => {
          setIsDetailsOpen(next);
          if (!next) setRequest(null);
        }}
        request={request}
      />
    </>
  );

  return { open, dialogs };
}
