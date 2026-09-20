import { useMutation } from "@tanstack/react-query";
import {
  updateFeedback,
  type FeedbackStatus,
} from "@foundation/src/lib/api/feedback-admin-api";

export interface UpdateFeedbackInput {
  id: string;
  status: FeedbackStatus;
  adminNotes: string;
  githubIssueUrl: string;
}

/**
 * Save the operator's triage of one feedback item.
 *
 * No `invalidates`: the feedback tab loads its list manually rather than through a
 * query (an operator surface, see docs/dialog-feedback.md rule 3), so there is no
 * cached consumer to invalidate. The caller's `onSaved` re-runs that load instead.
 */
export const useUpdateFeedback = (onSaved: () => void) =>
  useMutation({
    mutationFn: ({ id, status, adminNotes, githubIssueUrl }: UpdateFeedbackInput) =>
      updateFeedback(id, { status, adminNotes, githubIssueUrl }),
    meta: {
      successMessage: "Feedback updated",
      errorMessage: "Failed to update feedback",
    },
    onSuccess: () => onSaved(),
  });
