import { useMutation, useQuery } from "@tanstack/react-query";
import {
  deleteAiCredential,
  getAiCredential,
  getAiDailyLimits,
  getAiStatus,
  listAiAllowances,
  revokeAiAllowance,
  saveAiAllowance,
  saveAiCredential,
  saveAiDailyLimits,
  testAiCredential,
} from "@foundation/src/lib/api/ai-api";
import type { AiDailyLimits } from "@foundation/src/lib/api/ai-api";
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
    meta: { invalidates: [qk.ai.all()] },
  });
}

export function useDeleteAiCredential() {
  return useMutation({
    mutationFn: deleteAiCredential,
    meta: { invalidates: [qk.ai.all()] },
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
    meta: { invalidates: [qk.ai.all()] },
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
    meta: { invalidates: [qk.ai.all()] },
  });
}

export function useRevokeAiAllowance() {
  return useMutation({
    mutationFn: (userId: string) => revokeAiAllowance(userId),
    meta: { invalidates: [qk.ai.all()] },
  });
}

/**
 * Whether *this* user can chat right now, and what budget is left.
 *
 * Distinct from `useAiAssistantAvailable`, which only answers whether the workspace's
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
