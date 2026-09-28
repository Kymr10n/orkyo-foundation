import { useMutation, useQuery } from "@tanstack/react-query";
import {
  createApiAccessToken,
  listApiAccessTokens,
  revokeApiAccessToken,
  type CreateApiAccessTokenRequest,
  type CreatedApiAccessToken,
} from "@foundation/src/lib/api/api-access-tokens-api";
import {
  createReportingToken,
  listReportingTokens,
  revokeReportingToken,
  type CreatedReportingToken,
  type CreateReportingTokenRequest,
} from "@foundation/src/lib/api/reporting-tokens-api";
import { qk } from "@foundation/src/lib/api/query-keys";

/**
 * The two API-credential classes stay separate in storage, auth and audit, so each keeps
 * its own list, create and revoke hook here.
 */

/** Write-capable API access tokens. Disabled until the API-access entitlement is known. */
export const useApiAccessTokens = (enabled: boolean) =>
  useQuery({
    queryKey: qk.apiAccessTokens.all(),
    queryFn: listApiAccessTokens,
    enabled,
  });

// No successMessage: the raw token dialog that opens on success is the feedback. The create
// dialog stays open on failure and shows the message inline.
export const useCreateApiAccessToken = () =>
  useMutation<CreatedApiAccessToken, Error, CreateApiAccessTokenRequest>({
    mutationFn: (request) => createApiAccessToken(request),
    meta: { suppressErrorToast: true, invalidates: [qk.apiAccessTokens.all()] },
  });

/** Read-only reporting tokens. Disabled until the API-access entitlement is known. */
export const useReportingTokens = (enabled: boolean) =>
  useQuery({
    queryKey: qk.reportingTokens.all(),
    queryFn: listReportingTokens,
    enabled,
  });

export const useCreateReportingToken = () =>
  useMutation<CreatedReportingToken, Error, CreateReportingTokenRequest>({
    mutationFn: (request) => createReportingToken(request),
    meta: { suppressErrorToast: true, invalidates: [qk.reportingTokens.all()] },
  });

const revokeMeta = (invalidates: readonly unknown[]) => ({
  successMessage: "Token revoked",
  errorMessage: "Failed to revoke token. Please try again.",
  invalidates: [invalidates],
});

export const useRevokeApiAccessToken = () =>
  useMutation({
    mutationFn: (id: string) => revokeApiAccessToken(id),
    meta: revokeMeta(qk.apiAccessTokens.all()),
  });

export const useRevokeReportingToken = () =>
  useMutation({
    mutationFn: (id: string) => revokeReportingToken(id),
    meta: revokeMeta(qk.reportingTokens.all()),
  });
