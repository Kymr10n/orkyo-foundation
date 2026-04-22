/**
 * API client for Session operations
 *
 * These endpoints are called without tenant context (before tenant selection)
 * and require only OIDC authentication.
 */

import { TENANT_HEADER_NAME } from "@/constants/http";
import { apiPost } from "../core/api-client";
import { API_PATHS } from "../core/api-paths";

const sessionOptions = { omitHeaders: [TENANT_HEADER_NAME] };

/**
 * Mark the onboarding tour as seen for the current user.
 * Global per user — only needs to be called once.
 */
export async function markTourSeen(): Promise<void> {
  await apiPost<void>(
    API_PATHS.SESSION.TOUR_SEEN,
    {},
    { ...sessionOptions, skipJsonParse: true },
  );
}

/**
 * Accept Terms of Service
 */
export async function acceptTos(tosVersion: string): Promise<void> {
  await apiPost<void>(
    API_PATHS.SESSION.TOS_ACCEPT,
    { tosVersion },
    { ...sessionOptions, skipJsonParse: true },
  );
}
