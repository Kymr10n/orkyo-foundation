/**
 * API client for Interest Registration
 *
 * Allows users to register interest in Professional or Enterprise tiers.
 * The endpoint is anonymous (no auth required), but we send auth if available.
 */

import { TENANT_HEADER_NAME } from "@foundation/src/constants/http";
import { apiPost } from "../core/api-client";
import { API_PATHS } from "../core/api-paths";

interface RegisterInterestRequest {
  email: string;
  organizationId?: string;
  /** 1 = Professional, 2 = Enterprise */
  tier: number;
  source?: string;
}

interface RegisterInterestResponse {
  message: string;
  registrationId?: string;
}

/**
 * Register interest in a premium tier (Professional or Enterprise)
 */
export async function registerInterest(
  request: RegisterInterestRequest
): Promise<RegisterInterestResponse> {
  return apiPost<RegisterInterestResponse>(
    API_PATHS.INTEREST,
    {
      email: request.email,
      tier: request.tier,
      organizationId: request.organizationId ?? null,
      source: request.source ?? "onboarding",
    } as RegisterInterestRequest,
    { omitHeaders: [TENANT_HEADER_NAME] },
  );
}
