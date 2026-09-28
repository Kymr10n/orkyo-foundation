/**
 * The parsers that turn an assistant proposal's JSON payload into what the host handlers
 * take. Pure: the panel and the conversation hook both read proposals through these.
 */
/**
 * The requests an auto-scheduling proposal names.
 *
 * Separate from {@link proposalToRequestUpdate} because the two proposal kinds have
 * genuinely different payloads: one names a single request and its new field values, the
 * other names a set of requests and no values at all.
 */
export function proposalToAutoScheduleRequestIds(input: string): string[] {
  try {
    const parsed = JSON.parse(input) as Record<string, unknown>;
    const ids = parsed.requestIds;
    if (!Array.isArray(ids)) return [];
    return ids.filter((id): id is string => typeof id === "string" && id.length > 0);
  } catch {
    return [];
  }
}

export function proposalToRequestUpdate(input: string): {
  requestId: string | null;
  changes: Record<string, unknown>;
} {
  try {
    const parsed = JSON.parse(input) as Record<string, unknown>;
    return {
      requestId: typeof parsed.requestId === "string" ? parsed.requestId : null,
      changes: (parsed.changes ?? {}) as Record<string, unknown>,
    };
  } catch {
    return { requestId: null, changes: {} };
  }
}
