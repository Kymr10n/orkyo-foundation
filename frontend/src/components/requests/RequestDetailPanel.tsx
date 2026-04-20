import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Separator } from "@/components/ui/separator";
import { getPlanningModeIcon, getPlanningModeLabel } from "@/constants";
import {
  canHaveChildren,
  computeDerivedValues,
  getAncestorIds,
  getDirectChildren,
} from "@/domain/request-tree";
import type { Request } from "@/types/requests";
import {
  formatDateDisplay,
  formatDuration,
  getStatusColor,
} from "@/lib/utils/utils";
import { ChevronRight, Edit, X } from "lucide-react";
import React, { useMemo } from "react";

interface RequestDetailPanelProps {
  request: Request;
  allRequests: Request[];
  onEdit: (request: Request) => void;
  onNavigate: (requestId: string) => void;
  onClose: () => void;
}

export const RequestDetailPanel = React.memo(function RequestDetailPanel({
  request,
  allRequests,
  onEdit,
  onNavigate,
  onClose,
}: RequestDetailPanelProps) {
  const Icon = getPlanningModeIcon(request.planningMode);
  const isParent = canHaveChildren(request.planningMode);

  const { derived, children, breadcrumb } = useMemo(() => {
    const byId = new Map(allRequests.map((r) => [r.id, r]));
    const _derived = isParent ? computeDerivedValues(request.id, allRequests) : null;
    const _children = getDirectChildren(request.id, allRequests);
    const ancestorIds = getAncestorIds(request.id, allRequests, byId);
    const _breadcrumb = ancestorIds.reverse().map((id) => byId.get(id)).filter(Boolean) as Request[];
    return { derived: _derived, children: _children, breadcrumb: _breadcrumb };
  }, [request.id, allRequests, isParent]);

  return (
    <div className="flex flex-col h-full border-l bg-card">
      {/* Header */}
      <div className="flex items-center justify-between p-4 border-b">
        <div className="flex items-center gap-2 min-w-0">
          <Icon className="h-5 w-5 text-muted-foreground flex-shrink-0" />
          <h2 className="text-lg font-semibold truncate">{request.name}</h2>
        </div>
        <div className="flex items-center gap-1 flex-shrink-0">
          <Button variant="ghost" size="sm" onClick={() => onEdit(request)}>
            <Edit className="h-4 w-4" />
          </Button>
          <Button variant="ghost" size="sm" onClick={onClose}>
            <X className="h-4 w-4" />
          </Button>
        </div>
      </div>

      {/* Content */}
      <div className="flex-1 overflow-auto p-4 space-y-5">
        {/* Breadcrumb */}
        {breadcrumb.length > 0 && (
          <div className="flex items-center gap-1 text-xs text-muted-foreground flex-wrap">
            {breadcrumb.map((ancestor, i) => (
              <React.Fragment key={ancestor.id}>
                {i > 0 && <ChevronRight className="h-3 w-3" />}
                <button
                  className="hover:text-foreground hover:underline"
                  onClick={() => onNavigate(ancestor.id)}
                >
                  {ancestor.name}
                </button>
              </React.Fragment>
            ))}
            <ChevronRight className="h-3 w-3" />
            <span className="text-foreground font-medium">{request.name}</span>
          </div>
        )}

        {/* Basic info */}
        <div>
          <div className="flex items-center gap-2 mb-2">
            <Badge variant="outline">{getPlanningModeLabel(request.planningMode)}</Badge>
            <Badge className={getStatusColor(request.status)}>{request.status}</Badge>
          </div>
          {request.description && (
            <p className="text-sm text-muted-foreground">{request.description}</p>
          )}
        </div>

        <Separator />

        {/* Schedule */}
        <div>
          <h3 className="text-sm font-medium mb-2">Schedule</h3>
          {request.startTs && request.endTs ? (
            <div className="text-sm space-y-1">
              <div className="flex justify-between">
                <span className="text-muted-foreground">Start</span>
                <span>{formatDateDisplay(request.startTs)}</span>
              </div>
              <div className="flex justify-between">
                <span className="text-muted-foreground">End</span>
                <span>{formatDateDisplay(request.endTs)}</span>
              </div>
            </div>
          ) : (
            <p className="text-sm text-muted-foreground">Not yet scheduled</p>
          )}
          {isParent && derived?.startTs && derived?.endTs && (
            <div className="text-sm space-y-1 mt-2 border-l-2 border-muted pl-3">
              <p className="text-xs text-muted-foreground italic mb-1">Derived from children</p>
              <div className="flex justify-between">
                <span className="text-muted-foreground">Earliest</span>
                <span className="italic">{formatDateDisplay(derived.startTs)}</span>
              </div>
              <div className="flex justify-between">
                <span className="text-muted-foreground">Latest</span>
                <span className="italic">{formatDateDisplay(derived.endTs)}</span>
              </div>
            </div>
          )}
        </div>

        <Separator />

        {/* Duration */}
        <div>
          <h3 className="text-sm font-medium mb-2">Duration</h3>
          <div className="text-sm">
            <div className="flex justify-between">
              <span className="text-muted-foreground">Minimal</span>
              <span>{formatDuration(request.minimalDurationValue, request.minimalDurationUnit)}</span>
            </div>
            {isParent && derived && (
              <div className="flex justify-between mt-1">
                <span className="text-muted-foreground">Sum of children</span>
                <span className="italic">
                  {formatDuration(derived.totalDurationValue, derived.totalDurationUnit)}
                </span>
              </div>
            )}
          </div>
        </div>

        {/* Children list */}
        {isParent && children.length > 0 && (
          <>
            <Separator />
            <div>
              <h3 className="text-sm font-medium mb-2">Children ({children.length})</h3>
              <div className="space-y-1">
                {children.map((child) => {
                  const ChildIcon = getPlanningModeIcon(child.planningMode);
                  return (
                    <button
                      key={child.id}
                      className="flex items-center gap-2 w-full text-left text-sm rounded px-2 py-1.5 hover:bg-muted transition-colors"
                      onClick={() => onNavigate(child.id)}
                    >
                      <ChildIcon className="h-3.5 w-3.5 text-muted-foreground flex-shrink-0" />
                      <span className="truncate">{child.name}</span>
                      <Badge variant="outline" className="text-[10px] ml-auto flex-shrink-0">
                        {getPlanningModeLabel(child.planningMode)}
                      </Badge>
                    </button>
                  );
                })}
              </div>
            </div>
          </>
        )}

        {/* Requirements */}
        {request.requirements && request.requirements.length > 0 && (
          <>
            <Separator />
            <div>
              <h3 className="text-sm font-medium mb-2">
                Requirements ({request.requirements.length})
              </h3>
              <div className="space-y-1">
                {request.requirements.map((req) => (
                  <div key={req.id} className="flex justify-between text-sm">
                    <span className="text-muted-foreground">
                      {req.criterion?.name ?? req.criterionId}
                    </span>
                    <span>
                      {String(req.value)}
                      {req.criterion?.unit ? ` ${req.criterion.unit}` : ""}
                    </span>
                  </div>
                ))}
              </div>
            </div>
          </>
        )}

        {/* Constraints */}
        {(request.earliestStartTs || request.latestEndTs) && (
          <>
            <Separator />
            <div>
              <h3 className="text-sm font-medium mb-2">Constraints</h3>
              <div className="text-sm space-y-1">
                {request.earliestStartTs && (
                  <div className="flex justify-between">
                    <span className="text-muted-foreground">Earliest start</span>
                    <span>{formatDateDisplay(request.earliestStartTs)}</span>
                  </div>
                )}
                {request.latestEndTs && (
                  <div className="flex justify-between">
                    <span className="text-muted-foreground">Latest end</span>
                    <span>{formatDateDisplay(request.latestEndTs)}</span>
                  </div>
                )}
              </div>
            </div>
          </>
        )}
      </div>
    </div>
  );
});
