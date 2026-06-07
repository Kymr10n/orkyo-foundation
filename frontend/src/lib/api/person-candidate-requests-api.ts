/**
 * API client for the Person Assignment dialog's request options.
 *
 * A single read-only endpoint returns every active request whose scheduled
 * window overlaps the given period, annotated with:
 *   - `assignmentId` — non-null when the request is already assigned to this
 *     person (the dialog's removable "Assigned" list); null for candidates.
 *   - `requirements` — each required criterion with a server-computed
 *     `satisfied` flag (capability match is the backend's single source of
 *     truth, so the client never re-implements matching).
 */

import { apiGet } from "../core/api-client";
import { API_PATHS } from "../core/api-paths";

export interface CandidateRequirement {
  label: string;
  satisfied: boolean;
}

export interface PersonAssignmentOption {
  requestId: string;
  name: string;
  startTs: string | null;
  endTs: string | null;
  requirements: CandidateRequirement[];
  /** Non-null → already assigned to this person (used to remove). */
  assignmentId: string | null;
}

/** Count of unsatisfied requirements for an option. */
export function mismatchCount(option: PersonAssignmentOption): number {
  return option.requirements.filter((r) => !r.satisfied).length;
}

/** Whether the person satisfies every requirement of the request. */
export function matchesAllRequirements(option: PersonAssignmentOption): boolean {
  return mismatchCount(option) === 0;
}

export async function getPersonAssignmentOptions(
  personId: string,
  start: string,
  end: string,
): Promise<PersonAssignmentOption[]> {
  const params = new URLSearchParams({ start, end });
  return apiGet<PersonAssignmentOption[]>(
    `${API_PATHS.resourceCandidateRequests(personId)}?${params}`,
  );
}
