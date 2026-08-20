import { useMutation, useQuery } from "@tanstack/react-query";
import {
  activateCatalogType,
  deactivateCatalogType,
  getResourceTypeCatalog,
  purgeCatalogType,
  type CatalogPurgeResult,
} from "@foundation/src/lib/api/resource-type-catalog-api";
import { qk } from "@foundation/src/lib/api/query-keys";
import { RESOURCE_TYPE_INVALIDATES } from "@foundation/src/hooks/useResourceTypes";

/**
 * Every catalog switch changes the tenant's type list — the nav, the pickers, the
 * resource pages — so all three mutations invalidate the type queries alongside the
 * catalog itself.
 */
const CATALOG_INVALIDATES = [
  qk.resourceTypeCatalog.all(),
  ...RESOURCE_TYPE_INVALIDATES,
] as const;

/**
 * A purge additionally deletes resources, groups, assignments and request targets,
 * so everything that renders them refetches.
 */
const PURGE_INVALIDATES = [
  ...CATALOG_INVALIDATES,
  qk.resources.all(),
  qk.resourceGroups.all(),
  qk.resourceGroups.allFlat(),
  qk.requests.all(),
] as const;

export const useResourceTypeCatalog = () =>
  useQuery({
    queryKey: qk.resourceTypeCatalog.all(),
    queryFn: getResourceTypeCatalog,
  });

/** Switch ON: creates the type with its preset fields, or re-activates the tenant's row. */
export const useActivateCatalogType = () =>
  useMutation({
    mutationFn: (key: string) => activateCatalogType(key),
    meta: {
      successMessage: (data) =>
        `${(data as { displayName?: string })?.displayName ?? "Type"} activated`,
      errorMessage: "Failed to activate resource type",
      invalidates: CATALOG_INVALIDATES,
    },
  });

/** Switch OFF, the Hide path: the type disappears from pickers, its data survives. */
export const useDeactivateCatalogType = () =>
  useMutation({
    mutationFn: (key: string) => deactivateCatalogType(key),
    meta: {
      successMessage: "Type hidden — its data is kept",
      errorMessage: "Failed to hide resource type",
      invalidates: CATALOG_INVALIDATES,
    },
  });

/** Switch OFF, the destructive path: deletes the type and everything of that type. */
export const usePurgeCatalogType = () =>
  useMutation({
    mutationFn: (key: string) => purgeCatalogType(key),
    meta: {
      successMessage: (data) => {
        const r = data as CatalogPurgeResult;
        return `Type deleted (${r.resources} resources, ${r.assignments} assignments removed)`;
      },
      errorMessage: "Failed to delete resource type",
      invalidates: PURGE_INVALIDATES,
    },
  });
