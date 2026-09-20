import { useCallback } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  deleteAiCredential,
  getAiCredential,
  getAiDailyLimits,
  getAiStatus,
  listAiAllowances,
  listAiConversations,
  revokeAiAllowance,
  saveAiAllowance,
  saveAiCredential,
  saveAiDailyLimits,
  testAiCredential,
} from "@foundation/src/lib/api/ai-api";
import type { AiDailyLimits } from "@foundation/src/lib/api/ai-api";
import { updateRequest } from "@foundation/src/lib/api/request-api";
import type { UpdateRequestRequest } from "@foundation/src/types/requests";
import { qk } from "@foundation/src/lib/api/query-keys";
import { STALE } from "@foundation/src/lib/core/query-client";

/** The workspace's stored key — configured or not, plus its display hint. Admin surface. */
export function useAiCredential(enabled = true) {
  return useQuery({
    queryKey: qk.ai.credential(),
    queryFn: getAiCredential,
    staleTime: STALE.OPERATIONAL,
    enabled,
  });
}

export function useSaveAiCredential() {
  return useMutation({
    mutationFn: (apiKey: string) => saveAiCredential(apiKey),
    // A new key changes who can chat, so the member-facing status is stale too.
    meta: {
      successMessage: "AI key saved.",
      errorMessage: "That key was not accepted. Check that it is an Anthropic API key.",
      invalidates: [qk.ai.all()],
    },
  });
}

export function useDeleteAiCredential() {
  return useMutation({
    mutationFn: deleteAiCredential,
    meta: {
      successMessage: "AI key removed. The assistant is switched off for this workspace.",
      errorMessage: "Could not remove the AI key",
      invalidates: [qk.ai.all()],
    },
  });
}

export function useTestAiCredential() {
  return useMutation({
    mutationFn: testAiCredential,
    meta: { invalidates: [qk.ai.credential()] },
  });
}

/** Every workspace member with their grant and this month's spend. Admin surface. */
export function useAiAllowances(enabled = true) {
  return useQuery({
    queryKey: qk.ai.allowances(),
    queryFn: listAiAllowances,
    staleTime: STALE.OPERATIONAL,
    enabled,
  });
}

export function useAiDailyLimits(enabled = true) {
  return useQuery({
    queryKey: qk.ai.dailyLimits(),
    queryFn: getAiDailyLimits,
    staleTime: STALE.OPERATIONAL,
    enabled,
  });
}

export function useSaveAiDailyLimits() {
  return useMutation({
    mutationFn: (limits: AiDailyLimits) => saveAiDailyLimits(limits),
    // Invalidates the whole AI prefix: changing a limit changes what the member-facing
    // status endpoint reports, not only this admin screen.
    meta: {
      successMessage: "Daily limits saved",
      errorMessage: "Could not save the daily limits",
      invalidates: [qk.ai.all()],
    },
  });
}

export function useSaveAiAllowance() {
  return useMutation({
    mutationFn: ({
      userId,
      monthlyTokenLimit,
    }: {
      userId: string;
      monthlyTokenLimit: number | null;
    }) => saveAiAllowance(userId, monthlyTokenLimit),
    meta: {
      successMessage: "Allowance updated.",
      errorMessage: "Could not update the allowance",
      invalidates: [qk.ai.all()],
    },
  });
}

export function useRevokeAiAllowance() {
  return useMutation({
    mutationFn: (userId: string) => revokeAiAllowance(userId),
    meta: {
      successMessage: "Access removed.",
      errorMessage: "Could not remove access",
      invalidates: [qk.ai.all()],
    },
  });
}

/**
 * Whether *this* user can chat right now, and what budget is left.
 *
 * Distinct from `useFeatureEnabled(FeatureKeys.AiAssistant)`, which only answers whether the workspace's
 * plan includes the feature. A member of an entitled workspace still needs a grant.
 */
export function useAiStatus(enabled = true) {
  return useQuery({
    queryKey: qk.ai.status(),
    queryFn: getAiStatus,
    staleTime: STALE.OPERATIONAL,
    enabled,
  });
}

/** The person's own saved conversations — titles only; a body is fetched when one is opened. */
export function useAiConversations(enabled = true) {
  return useQuery({
    queryKey: qk.ai.conversations(),
    queryFn: listAiConversations,
    enabled,
  });
}

/**
 * Re-read the status the header counts down from. A finished turn spends an interaction, and the
 * assistant writes conversations outside react-query, so both caches are refreshed by hand.
 */
export function useInvalidateAiStatus() {
  const queryClient = useQueryClient();
  return useCallback(
    () => queryClient.invalidateQueries({ queryKey: qk.ai.status() }),
    [queryClient],
  );
}

/** Re-read the conversation list after a save or a delete. */
export function useInvalidateAiConversations() {
  const queryClient = useQueryClient();
  return useCallback(
    () => queryClient.invalidateQueries({ queryKey: qk.ai.conversations() }),
    [queryClient],
  );
}

/**
 * Write the change an accepted proposal describes.
 *
 * It goes through the ordinary request endpoint under this person's own session, so the same
 * validation and permissions apply as to a manual edit. A request change can create or clear a
 * conflict, so both caches are re-read.
 */
export function useApplyAssistantProposal() {
  const queryClient = useQueryClient();

  return useCallback(
    async (requestId: string, changes: Record<string, unknown>) => {
      await updateRequest(requestId, changes as UpdateRequestRequest);
      await queryClient.invalidateQueries({ queryKey: qk.requests.all() });
      await queryClient.invalidateQueries({ queryKey: qk.conflicts.all() });
    },
    [queryClient],
  );
}
