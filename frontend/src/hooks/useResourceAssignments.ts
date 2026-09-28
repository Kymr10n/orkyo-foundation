import { useMutation } from "@tanstack/react-query";
import {
  cancelAssignment,
  createAssignment,
  validateAssignment,
  validateAssignmentsBatch,
  type CreateResourceAssignmentRequest,
  type ValidateResourceAssignmentRequest,
} from "@foundation/src/lib/api/resource-assignments-api";
import { REQUEST_DERIVED_QUERY_KEYS } from "@foundation/src/lib/core/invalidate-request-data";

// The staffing surfaces (the request's People section and the utilization assignment dialog)
// run these per row, each row with its own status, so callers use `mutateAsync` and keep the
// row state themselves. No toasts here: each surface reports per row.

/** Dry-run one assignment and return its blockers and warnings. Writes nothing. */
export const useValidateAssignment = () => useMutation({
    mutationFn: (request: ValidateResourceAssignmentRequest) => validateAssignment(request),
  });

/** Dry-run many assignments in one call. Writes nothing. */
export const useValidateAssignmentsBatch = () =>
  useMutation({
    mutationFn: (requests: ValidateResourceAssignmentRequest[]) => validateAssignmentsBatch(requests),
  });

/** An assignment changes occupancy and conflicts, so the request-derived views refresh. */
export const useCreateAssignment = () =>
  useMutation({
    mutationFn: (request: CreateResourceAssignmentRequest) => createAssignment(request),
    meta: { invalidates: REQUEST_DERIVED_QUERY_KEYS },
  });

/** Cancelling changes occupancy and conflicts, so the request-derived views refresh. */
export const useCancelAssignment = () =>
  useMutation({
    mutationFn: (id: string) => cancelAssignment(id),
    meta: { invalidates: REQUEST_DERIVED_QUERY_KEYS },
  });
