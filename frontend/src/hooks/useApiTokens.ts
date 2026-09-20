import { useMutation, useQuery } from "@tanstack/react-query";
import {
  createApiAccessToken,
  listApiAccessTokens,
  type CreateApiAccessTokenRequest,
  type CreatedApiAccessToken,
} from "@foundation/src/lib/api/api-access-tokens-api";
import {
  createReportingToken,
  listReportingTokens,
  type CreatedReportingToken,
  type CreateReportingTokenRequest,
} from "@foundation/src/lib/api/reporting-tokens-api";
import { qk } from "@foundation/src/lib/api/query-keys";

/**
 * The two API-credential classes stay separate in storage, auth and audit, so each keeps
 * its own list/create hook here; only revoking is generic enough to share.
 */

/** Write-capable API access tokens. Disabled until the API-access entitlement is known. */
export const useApiAccessTokens = (enabled: boolean) =>
  useQuery({
    queryKey: qk.apiAccessTokens.all(),
    queryFn: listApiAccessTokens,
    enabled,
  });

// No successMessage: the raw token dialog that opens on success is the feedback.
export const useCreateApiAccessToken = (onSuccess: (result: CreatedApiAccessToken) => void) =>
  useMutation({
    mutationFn: (request: CreateApiAccessTokenRequest) => createApiAccessToken(request),
    meta: {
      errorMessage: "Failed to create token. Please try again.",
      invalidates: [qk.apiAccessTokens.all()],
    },
    onSuccess,
  });

/** Read-only reporting tokens. Disabled until the API-access entitlement is known. */
export const useReportingTokens = (enabled: boolean) =>
  useQuery({
    queryKey: qk.reportingTokens.all(),
    queryFn: listReportingTokens,
    enabled,
  });

export const useCreateReportingToken = (onSuccess: (result: CreatedReportingToken) => void) =>
  useMutation({
    mutationFn: (request: CreateReportingTokenRequest) => createReportingToken(request),
    meta: {
      errorMessage: "Failed to create token. Please try again.",
      invalidates: [qk.reportingTokens.all()],
    },
    onSuccess,
  });

/** Revoking is the same job for both classes; the caller names the endpoint and the list to refresh. */
export const useRevokeToken = (
  revokeFn: (id: string) => Promise<void>,
  invalidates: readonly unknown[],
  onSuccess: () => void,
) =>
  useMutation({
    mutationFn: (id: string) => revokeFn(id),
    meta: {
      successMessage: "Token revoked",
      errorMessage: "Failed to revoke token. Please try again.",
      invalidates: [invalidates],
    },
    onSuccess,
  });
