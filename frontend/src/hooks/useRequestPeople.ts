import { useEffect, useState, type Dispatch, type SetStateAction } from "react";
import { getResources, type ResourceInfo } from "@foundation/src/lib/api/resources-api";
import { getResourceTypes } from "@foundation/src/lib/api/resource-types-api";
import {
  getAssignmentsByRequest,
  type ResourceAssignmentInfo,
} from "@foundation/src/lib/api/resource-assignments-api";

interface UseRequestPeopleResult {
  /** Every active resource of a directory type — the assignable people. */
  people: ResourceInfo[];
  assignments: ResourceAssignmentInfo[];
  /** The section adds and removes rows locally after a save or a cancel. */
  setAssignments: Dispatch<SetStateAction<ResourceAssignmentInfo[]>>;
  /** Whether the tenant has any directory type at all. */
  hasDirectoryType: boolean;
  isLoading: boolean;
}

/**
 * The people a request can be staffed with, and the ones it already has.
 *
 * "People" means every active type with a directory profile — nothing is keyed to `person`,
 * which is just a catalog entry a tenant may or may not have activated (or renamed).
 */
export function useRequestPeople(requestId: string | undefined): UseRequestPeopleResult {
  const [people, setPeople] = useState<ResourceInfo[]>([]);
  const [assignments, setAssignments] = useState<ResourceAssignmentInfo[]>([]);
  // true until the type list proves otherwise, so the "activate a type" hint never flashes
  // during the initial load.
  const [hasDirectoryType, setHasDirectoryType] = useState(true);
  const [isLoading, setIsLoading] = useState(false);

  useEffect(() => {
    let cancelled = false;
    const load = async () => {
      setIsLoading(true);
      try {
        const types = await getResourceTypes(true);
        const directoryKeys = new Set(
          types.filter((t) => t.hasDirectoryProfile).map((t) => t.key),
        );
        const [peopleLists, assignmentsRes] = await Promise.all([
          Promise.all(
            [...directoryKeys].map((key) =>
              getResources({ resourceTypeKey: key, isActive: true }),
            ),
          ),
          requestId ? getAssignmentsByRequest(requestId) : Promise.resolve([]),
        ]);
        if (!cancelled) {
          setHasDirectoryType(directoryKeys.size > 0);
          setPeople(peopleLists.flatMap((res) => res.items));
          setAssignments(assignmentsRes.filter((a) => directoryKeys.has(a.resourceTypeKey)));
        }
      } catch {
        // Non-critical: section remains empty
      } finally {
        if (!cancelled) setIsLoading(false);
      }
    };
    load();
    return () => { cancelled = true; };
  }, [requestId]);

  return { people, assignments, setAssignments, hasDirectoryType, isLoading };
}
