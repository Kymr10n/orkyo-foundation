import type { RequestDependency } from '@foundation/src/lib/api/request-dependency-api';

/** A finish-to-start edge between two requests, ids and names taken from the arguments. */
export function edge(predecessor: string, successor: string): RequestDependency {
  return {
    id: `${predecessor}->${successor}`,
    predecessorRequestId: predecessor,
    successorRequestId: successor,
    predecessorName: predecessor,
    successorName: successor,
    dependencyType: 'finish_to_start',
    lagMinutes: 0,
    createdAt: '2026-06-01T00:00:00Z',
  };
}
