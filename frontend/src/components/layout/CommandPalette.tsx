/**
 * Command Palette Component - Global search with keyboard navigation
 * Opens with Ctrl+K (Cmd+K on Mac)
 */

import { useCallback, useEffect, useRef, useState } from "react";
import { useNavigate } from "react-router-dom";
import {
  Building2,
  FileText,
  Grid3X3,
  Layers,
  ListChecks,
  MapPin,
  Search,
  X,
} from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { ScrollArea } from "@/components/ui/scroll-area";
import { VisuallyHidden } from "@/components/ui/visually-hidden";
import { cn } from "@/lib/utils";
import { globalSearch, type SearchResult } from "@/lib/api/search-api";
import { useAppStore } from "@/store/app-store";
import { logger } from "@/lib/core/logger";

// Icon mapping for entity types
const typeIcons: Record<SearchResult["type"], React.ReactNode> = {
  space: <Grid3X3 className="h-4 w-4" />,
  request: <FileText className="h-4 w-4" />,
  group: <Layers className="h-4 w-4" />,
  site: <Building2 className="h-4 w-4" />,
  template: <ListChecks className="h-4 w-4" />,
  criterion: <MapPin className="h-4 w-4" />,
};

// Display labels for entity types
const typeLabels: Record<SearchResult["type"], string> = {
  space: "Space",
  request: "Request",
  group: "Group",
  site: "Site",
  template: "Template",
  criterion: "Criterion",
};

// Badge colors for entity types
const typeBadgeVariants: Record<SearchResult["type"], "default" | "secondary" | "outline"> = {
  space: "default",
  request: "secondary",
  group: "secondary",
  site: "outline",
  template: "outline",
  criterion: "outline",
};

interface CommandPaletteProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

export function CommandPalette({ open, onOpenChange }: CommandPaletteProps) {
  const navigate = useNavigate();
  const selectedSiteId = useAppStore((state) => state.selectedSiteId);
  const setSelectedSiteId = useAppStore((state) => state.setSelectedSiteId);
  
  const [query, setQuery] = useState("");
  const [results, setResults] = useState<SearchResult[]>([]);
  const [selectedIndex, setSelectedIndex] = useState(0);
  const [isLoading, setIsLoading] = useState(false);
  
  const inputRef = useRef<HTMLInputElement>(null);
  const resultRefs = useRef<(HTMLDivElement | null)[]>([]);
  const debounceRef = useRef<ReturnType<typeof setTimeout>>();

  // Reset selection and focus when dialog opens
  useEffect(() => {
    if (open) {
      setSelectedIndex(0);
      // Focus input after dialog animation
      setTimeout(() => inputRef.current?.focus(), 50);
    }
  }, [open]);

  // Debounced search
  useEffect(() => {
    if (!query.trim()) {
      setResults([]);
      setSelectedIndex(0);
      return;
    }

    if (debounceRef.current) {
      clearTimeout(debounceRef.current);
    }

    debounceRef.current = setTimeout(() => {
      setIsLoading(true);
      globalSearch({
        query: query.trim(),
        siteId: selectedSiteId ?? undefined,
        limit: 20,
      }).then((response) => {
        setResults(response.results);
        setSelectedIndex(0);
      }).catch((error: unknown) => {
        logger.error("Search failed:", error);
        setResults([]);
      }).finally(() => {
        setIsLoading(false);
      });
    }, 200);

    return () => {
      if (debounceRef.current) {
        clearTimeout(debounceRef.current);
      }
    };
  }, [query, selectedSiteId]);

  // Scroll selected item into view
  useEffect(() => {
    resultRefs.current[selectedIndex]?.scrollIntoView({
      block: "nearest",
    });
  }, [selectedIndex]);

  // Navigate to selected result
  const navigateToResult = useCallback(
    (result: SearchResult) => {
      // If navigating to a different site, update the selected site
      if (result.siteId && result.siteId !== selectedSiteId) {
        setSelectedSiteId(result.siteId);
      }

      // Build the navigation path from the result's open property
      const { route: _route, params: _params } = result.open;
      
      // Map the route pattern to actual navigation
      switch (result.type) {
        case "space":
          // Navigate to spaces page, could also select the space
          navigate("/spaces");
          break;
        case "request":
          // Navigate to requests page
          navigate("/requests");
          break;
        case "group":
          // Groups are managed in spaces page
          navigate("/spaces");
          break;
        case "site":
          // Sites are in settings, switch to that site
          if (result.id !== selectedSiteId) {
            setSelectedSiteId(result.id);
          }
          navigate("/settings");
          break;
        case "template":
        case "criterion":
          // Templates and criteria are in settings
          navigate("/settings");
          break;
        default:
          navigate("/");
      }

      onOpenChange(false);
    },
    [navigate, onOpenChange, selectedSiteId, setSelectedSiteId]
  );

  // Navigate to edit mode for a result
  const navigateToEdit = useCallback(
    (result: SearchResult) => {
      // Close dialog first to ensure navigation works
      onOpenChange(false);

      // If navigating to a different site, update the selected site
      if (result.siteId && result.siteId !== selectedSiteId) {
        setSelectedSiteId(result.siteId);
      }

      // Navigate with edit query param after a small delay to ensure dialog closes
      setTimeout(() => {
        switch (result.type) {
          case "space":
            navigate(`/spaces?edit=${result.id}`);
            break;
          case "request":
            navigate(`/requests?edit=${result.id}`);
            break;
          case "group":
            navigate(`/settings?tab=groups&edit=${result.id}`);
            break;
          case "site":
            if (result.id !== selectedSiteId) {
              setSelectedSiteId(result.id);
            }
            navigate("/settings?tab=sites");
            break;
          case "template":
            navigate(`/settings?tab=templates&edit=${result.id}`);
            break;
          case "criterion":
            navigate(`/settings?tab=criteria&edit=${result.id}`);
            break;
          default:
            navigate("/");
        }
      }, 0);
    },
    [navigate, onOpenChange, selectedSiteId, setSelectedSiteId]
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
            navigateToResult(results[selectedIndex]);
          }
          break;
        case "Escape":
          e.preventDefault();
          onOpenChange(false);
          break;
      }
    },
    [results, selectedIndex, navigateToResult, onOpenChange]
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
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            className="flex h-12 w-full border-0 bg-transparent py-3 text-sm outline-none placeholder:text-muted-foreground focus-visible:ring-0"
          />
          {query && !isLoading && (
            <Button
              variant="ghost"
              size="icon"
              className="h-6 w-6 shrink-0"
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
                  ref={(el) => (resultRefs.current[index] = el)}
                  className={cn(
                    "flex cursor-pointer items-center gap-3 rounded-md px-3 py-2 text-sm",
                    index === selectedIndex
                      ? "bg-accent text-accent-foreground"
                      : "hover:bg-muted"
                  )}
                  onClick={() => navigateToResult(result)}
                  onMouseEnter={() => setSelectedIndex(index)}
                >
                  {/* Type Icon */}
                  <div className="flex h-8 w-8 shrink-0 items-center justify-center rounded-md border bg-background">
                    {typeIcons[result.type]}
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
                  <Badge variant={typeBadgeVariants[result.type]} className="shrink-0">
                    {typeLabels[result.type]}
                  </Badge>

                  {/* Edit button */}
                  {result.permissions.canEdit && (
                    <Button
                      variant="ghost"
                      size="sm"
                      className="h-6 px-2 text-xs"
                      onPointerDown={(e) => {
                        e.stopPropagation();
                      }}
                      onClick={(e) => {
                        e.stopPropagation();
                        e.preventDefault();
                        navigateToEdit(result);
                      }}
                    >
                      Edit
                    </Button>
                  )}
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
