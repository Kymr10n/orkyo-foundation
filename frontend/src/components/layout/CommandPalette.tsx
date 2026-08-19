/**
 * Command Palette Component - Global search with keyboard navigation
 * Opens with Ctrl+K (Cmd+K on Mac)
 */

import { useCallback, useEffect, useRef, useState } from "react";
import { useNavigate } from "react-router";
import { typeRoute } from "@foundation/src/constants/resource-class";
import { useResourceTypes } from "@foundation/src/hooks/useResourceTypes";
import type { ResourceTypeInfo } from "@foundation/src/lib/api/resource-types-api";
import {
  Building2,
  FileText,
  Layers,
  ListChecks,
  MapPin,
  Search,
  X,
} from "lucide-react";
import { Badge } from "@foundation/src/components/ui/badge";
import { Button } from "@foundation/src/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogTitle } from "@foundation/src/components/ui/dialog";
import { Input } from "@foundation/src/components/ui/input";
import { ScrollArea } from "@foundation/src/components/ui/scroll-area";
import { VisuallyHidden } from "@foundation/src/components/ui/visually-hidden";
import { cn } from "@foundation/src/lib/utils";
import { globalSearch, type SearchResult } from "@foundation/src/lib/api/search-api";
import { useAppStore } from "@foundation/src/store/app-store";
import { useCanEdit, useIsTenantAdmin } from "@foundation/src/hooks/usePermissions";
import { useDebouncedCallback } from "@foundation/src/hooks/useDebouncedCallback";
import { ROUTE_SETTINGS, ROUTE_TENANT_ADMIN } from "@foundation/src/constants/auth";
import { resourceTypeIcon } from "@foundation/src/components/resources/resource-type-icon";
import { logger } from "@foundation/src/lib/core/logger";

// Non-resource entity types. Resources are handled separately: their icon comes from the
// type's own registry and their label from the type key, so a tenant-defined type shows up
// correctly without anything being added here.
const typeIcons: Record<Exclude<SearchResult["type"], "resource">, React.ReactNode> = {
  request: <FileText className="h-4 w-4" />,
  group: <Layers className="h-4 w-4" />,
  site: <Building2 className="h-4 w-4" />,
  template: <ListChecks className="h-4 w-4" />,
  criterion: <MapPin className="h-4 w-4" />,
};

const typeLabels: Record<Exclude<SearchResult["type"], "resource">, string> = {
  request: "Request",
  group: "Group",
  site: "Site",
  template: "Template",
  criterion: "Criterion",
};

const typeBadgeVariants: Record<
  Exclude<SearchResult["type"], "resource">,
  "default" | "secondary" | "outline"
> = {
  request: "secondary",
  group: "secondary",
  site: "outline",
  template: "outline",
  criterion: "outline",
};

/** Built-in types keep their dedicated pages; everything else uses the generic one. */


export function iconForResult(result: SearchResult): React.ReactNode {
  if (result.type === "resource") {
    const Icon = resourceTypeIcon(result.resourceTypeKey);
    return <Icon className="h-4 w-4" />;
  }
  return typeIcons[result.type];
}

/** Title-cases the type key so "delivery_van" reads as "Delivery van". */
export function labelForResult(result: SearchResult): string {
  if (result.type !== "resource") return typeLabels[result.type];
  const key = result.resourceTypeKey ?? "Resource";
  const words = key.replace(/_/g, " ");
  return words.charAt(0).toUpperCase() + words.slice(1);
}

export function badgeVariantForResult(
  result: SearchResult,
): "default" | "secondary" | "outline" {
  return result.type === "resource" ? "default" : typeBadgeVariants[result.type];
}

/**
 * Where a hit opens. `types` lets a resource hit go straight to its class page — a result carries
 * the type key but not the class, so without them the only honest destination is the legacy route
 * that resolves it, at the cost of a redirect and a loading flash per hit.
 */
export function editPathForResult(
  result: SearchResult,
  types: readonly ResourceTypeInfo[] = [],
): string {
  const edit = `edit=${result.id}`;
  const known = (key: string) => types.find((t) => t.key === key);
  switch (result.type) {
    case "resource": {
      const key = result.resourceTypeKey ?? "";
      const type = known(key);
      return type
        ? `${typeRoute(type, "instances")}?${edit}`
        : `/resources/${key}/list?${edit}`;
    }
    case "request":
      return `/requests?${edit}`;
    case "group": {
      const key = result.resourceTypeKey ?? "";
      const type = known(key);
      return type ? `${typeRoute(type, "groups")}?${edit}` : `/resources/${key}/groups?${edit}`;
    }
    case "site":
      return `${ROUTE_TENANT_ADMIN}/sites?${edit}`;
    case "template":
      return `${ROUTE_SETTINGS}/templates?${edit}`;
    case "criterion":
      return `${ROUTE_SETTINGS}/criteria?${edit}`;
    default:
      return "/";
  }
}

interface CommandPaletteProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

export function CommandPalette({ open, onOpenChange }: CommandPaletteProps) {
  const navigate = useNavigate();
  // A result names its type but not the class it belongs to; these supply the difference so a hit
  // opens its page directly instead of bouncing through the legacy route.
  const { data: resourceTypes = [] } = useResourceTypes(true);
  const selectedSiteId = useAppStore((state) => state.selectedSiteId);
  const setSelectedSiteId = useAppStore((state) => state.setSelectedSiteId);
  // Only surface results the current role can actually open: criteria/templates live on the
  // Settings page (editors) and sites on the Administration page (admins). For other roles those
  // would dead-end at a route-guard redirect, so filter them out.
  const canEdit = useCanEdit();
  const isTenantAdmin = useIsTenantAdmin();

  const [query, setQuery] = useState("");
  const [results, setResults] = useState<SearchResult[]>([]);
  const [selectedIndex, setSelectedIndex] = useState(0);
  const [isLoading, setIsLoading] = useState(false);

  const inputRef = useRef<HTMLInputElement>(null);
  const resultRefs = useRef<(HTMLDivElement | null)[]>([]);

  // Resetting the selection is a render-phase update; focusing after the dialog animation
  // is a real side effect and stays in the effect below.
  const [syncedOpen, setSyncedOpen] = useState(open);
  if (syncedOpen !== open) {
    setSyncedOpen(open);
    if (open) setSelectedIndex(0);
  }

  useEffect(() => {
    if (!open) return;
    // Focus input after dialog animation
    const t = setTimeout(() => inputRef.current?.focus(), 50);
    return () => clearTimeout(t);
  }, [open]);

  const runSearch = useDebouncedCallback(() => {
    setIsLoading(true);
    globalSearch({
      query: query.trim(),
      siteId: selectedSiteId ?? undefined,
      limit: 20,
    }).then((response) => {
      const visible = response.results.filter((r) => {
        if (r.type === "site") return isTenantAdmin;
        if (r.type === "template" || r.type === "criterion") return canEdit;
        return true; // stations/assets/requests/groups are viewable on core pages
      });
      setResults(visible);
      setSelectedIndex(0);
    }).catch((error: unknown) => {
      logger.error("Search failed:", error);
      setResults([]);
    }).finally(() => {
      setIsLoading(false);
    });
  }, 200);

  // Clearing the results when the query empties is a render-phase update; running and
  // cancelling the debounced search is a timer side effect and stays in the effect below.
  const queryEmpty = !query.trim();
  const [syncedEmpty, setSyncedEmpty] = useState(queryEmpty);
  if (syncedEmpty !== queryEmpty) {
    setSyncedEmpty(queryEmpty);
    if (queryEmpty) {
      setResults([]);
      setSelectedIndex(0);
    }
  }

  // Debounced search
  useEffect(() => {
    if (queryEmpty) {
      runSearch.cancel();
      return;
    }
    runSearch();
  }, [queryEmpty, query, selectedSiteId, canEdit, isTenantAdmin, runSearch]);

  // Scroll selected item into view
  useEffect(() => {
    resultRefs.current[selectedIndex]?.scrollIntoView({
      block: "nearest",
    });
  }, [selectedIndex]);

  // Open the selected result's detail dialog. The destination page reads the
  // `?edit=<id>` param (see editPathForResult) and opens the item in view or edit
  // mode by permission. Close the palette first, then navigate on the next tick so
  // the dialog has torn down before the route change.
  const openResult = useCallback(
    (result: SearchResult) => {
      onOpenChange(false);

      // Switch to the result's site first so its page loads the item to open.
      if (result.siteId && result.siteId !== selectedSiteId) {
        setSelectedSiteId(result.siteId);
      }

      setTimeout(() => navigate(editPathForResult(result, resourceTypes)), 0);
    },
    [navigate, onOpenChange, selectedSiteId, setSelectedSiteId, resourceTypes]
  );

  // Keyboard navigation
  const handleKeyDown = useCallback(
    (e: React.KeyboardEvent) => {
      switch (e.key) {
        case "ArrowDown":
          e.preventDefault();
          setSelectedIndex((i) => Math.min(i + 1, results.length - 1));
          break;
        case "ArrowUp":
          e.preventDefault();
          setSelectedIndex((i) => Math.max(i - 1, 0));
          break;
        case "Enter":
          e.preventDefault();
          if (results[selectedIndex]) {
            openResult(results[selectedIndex]);
          }
          break;
        case "Escape":
          e.preventDefault();
          onOpenChange(false);
          break;
      }
    },
    [results, selectedIndex, openResult, onOpenChange]
  );

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent
        className="overflow-hidden p-0 shadow-lg sm:max-w-[550px]"
        onKeyDown={handleKeyDown}
      >
        <VisuallyHidden>
          <DialogTitle>Search</DialogTitle>
          <DialogDescription>Search spaces, requests, groups, and sites</DialogDescription>
        </VisuallyHidden>
        {/* Search Input */}
        <div className="flex items-center border-b px-3">
          <Search className="mr-2 h-4 w-4 shrink-0 opacity-50" />
          <Input
            ref={inputRef}
            placeholder="Search spaces, requests, groups, sites..."
            aria-label="Search spaces, requests, groups, sites"
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            className="flex h-12 w-full border-0 bg-transparent py-3 text-sm outline-hidden placeholder:text-muted-foreground focus-visible:ring-0"
          />
          {query && !isLoading && (
            <Button
              variant="ghost"
              size="icon"
              className="h-6 w-6 shrink-0"
              aria-label="Clear search"
              onClick={() => {
                setQuery("");
                setResults([]);
                inputRef.current?.focus();
              }}
            >
              <X className="h-4 w-4" />
            </Button>
          )}
          {isLoading && (
            <div className="h-4 w-4 animate-spin rounded-full border-2 border-primary border-t-transparent" />
          )}
        </div>

        {/* Results */}
        <ScrollArea className="max-h-[400px]">
          {results.length === 0 && query.trim() && !isLoading ? (
            <div className="py-6 text-center text-sm text-muted-foreground">
              No results found for "{query}"
            </div>
          ) : results.length > 0 ? (
            <div className="p-2">
              {results.map((result, index) => (
                <div
                  key={`${result.type}-${result.id}`}
                  ref={(el) => { resultRefs.current[index] = el; }}
                  className={cn(
                    "flex cursor-pointer items-center gap-3 rounded-md px-3 py-2 text-sm",
                    index === selectedIndex
                      ? "bg-accent text-accent-foreground"
                      : "hover:bg-muted"
                  )}
                  onClick={() => openResult(result)}
                  onMouseEnter={() => setSelectedIndex(index)}
                >
                  {/* Type Icon */}
                  <div className="flex h-8 w-8 shrink-0 items-center justify-center rounded-md border bg-background">
                    {iconForResult(result)}
                  </div>

                  {/* Content */}
                  <div className="flex min-w-0 flex-1 flex-col">
                    <span className="truncate font-medium">{result.title}</span>
                    {result.subtitle && (
                      <span className="truncate text-xs text-muted-foreground">
                        {result.subtitle}
                      </span>
                    )}
                  </div>

                  {/* Type Badge */}
                  <Badge variant={badgeVariantForResult(result)} className="shrink-0">
                    {labelForResult(result)}
                  </Badge>
                </div>
              ))}
            </div>
          ) : !query.trim() ? (
            <div className="py-6 text-center text-sm text-muted-foreground">
              Start typing to search...
            </div>
          ) : null}
        </ScrollArea>

        {/* Footer with keyboard hints */}
        {results.length > 0 && (
          <div className="flex items-center justify-between border-t px-3 py-2 text-xs text-muted-foreground">
            <div className="flex gap-4">
              <span>
                <kbd className="rounded border bg-muted px-1.5 py-0.5 font-mono">↑</kbd>
                <kbd className="ml-1 rounded border bg-muted px-1.5 py-0.5 font-mono">↓</kbd>
                <span className="ml-1.5">Navigate</span>
              </span>
              <span>
                <kbd className="rounded border bg-muted px-1.5 py-0.5 font-mono">↵</kbd>
                <span className="ml-1.5">Open</span>
              </span>
              <span>
                <kbd className="rounded border bg-muted px-1.5 py-0.5 font-mono">Esc</kbd>
                <span className="ml-1.5">Close</span>
              </span>
            </div>
            <span>{results.length} result{results.length !== 1 ? "s" : ""}</span>
          </div>
        )}
      </DialogContent>
    </Dialog>
  );
}
