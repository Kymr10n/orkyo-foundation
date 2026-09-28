import { useCallback } from "react";
import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  getFeedback,
  getFeedbackItem,
  updateFeedback,
  type FeedbackDetail,
  type FeedbackStatus,
} from "@foundation/src/lib/api/feedback-admin-api";
import { qk } from "@foundation/src/lib/api/query-keys";

export interface UpdateFeedbackInput {
  id: string;
  status: FeedbackStatus;
  adminNotes: string;
  githubIssueUrl: string;
}

/**
 * The feedback list for one status filter (null = every status). The previous filter's rows
 * stay on screen while the next one loads, so a filter change does not flash the spinner.
 */
export const useFeedbackList = (status: FeedbackStatus | null) =>
  useQuery({
    queryKey: qk.admin.feedbackList(status),
    queryFn: () => getFeedback(status ? { status } : undefined),
    placeholderData: keepPreviousData,
  });

/** Read one item with its full text when the operator opens it. */
export function useFetchFeedbackItem(): (id: string) => Promise<FeedbackDetail> {
  const queryClient = useQueryClient();
  return useCallback(
    (id: string) =>
      queryClient.fetchQuery({ queryKey: qk.admin.feedbackItem(id), queryFn: () => getFeedbackItem(id) }),
    [queryClient],
  );
}

/** Save the operator's triage of one feedback item. */
export const useUpdateFeedback = (onSaved: () => void) =>
  useMutation({
    mutationFn: ({ id, status, adminNotes, githubIssueUrl }: UpdateFeedbackInput) =>
      updateFeedback(id, { status, adminNotes, githubIssueUrl }),
    meta: {
      successMessage: "Feedback updated",
      errorMessage: "Failed to update feedback",
      invalidates: [qk.admin.feedback()],
    },
    onSuccess: () => onSaved(),
  });
