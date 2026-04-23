import { Badge } from "@foundation/src/components/ui/badge";
import { Button } from "@foundation/src/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from "@foundation/src/components/ui/dialog";
import { Separator } from "@foundation/src/components/ui/separator";
import { getDataTypeColor, formatDuration, formatMinutesHuman, getStatusColor } from "@foundation/src/lib/utils";
import type { Request } from "@foundation/src/types/requests";
import type { CriterionValue } from "@foundation/src/types/criterion";
import { Calendar, Clock, MapPin, Tag, FileText } from "lucide-react";

interface RequestDetailsDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  request: Request | null;
}

export function RequestDetailsDialog({
  open,
  onOpenChange,
  request,
}: RequestDetailsDialogProps) {
  if (!request) return null;

  const formatDate = (dateStr?: string | null) => {
    if (!dateStr) return "Not set";
    const date = new Date(dateStr);
    return date.toLocaleDateString(undefined, {
      weekday: "short",
      year: "numeric",
      month: "short",
      day: "numeric",
    });
  };

  const formatTime = (dateStr?: string | null) => {
    if (!dateStr) return "";
    const date = new Date(dateStr);
    return date.toLocaleTimeString(undefined, {
      hour: "2-digit",
      minute: "2-digit",
    });
  };



  const formatRequirementValue = (value: CriterionValue | null | undefined, dataType?: string) => {
    if (value === null || value === undefined) return "Not set";
    if (dataType === "Boolean") return value ? "Yes" : "No";
    if (typeof value === "object") return JSON.stringify(value);
    return String(value);
  };

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-lg">
        <DialogHeader>
          <DialogTitle className="flex items-center gap-3">
            <span>{request.name}</span>
            <Badge className={getStatusColor(request.status)}>
              {request.status}
            </Badge>
          </DialogTitle>
          <DialogDescription className="sr-only">Details for request {request.name}</DialogDescription>
        </DialogHeader>

        <div className="space-y-6 mt-4">
          {/* Description */}
          {request.description && (
            <div className="space-y-2">
              <h4 className="text-sm font-medium text-muted-foreground flex items-center gap-2">
                <FileText className="h-4 w-4" />
                Description
              </h4>
              <p className="text-sm">{request.description}</p>
            </div>
          )}

          <Separator />

          {/* Schedule */}
          <div className="space-y-3">
            <h4 className="text-sm font-medium text-muted-foreground flex items-center gap-2">
              <Calendar className="h-4 w-4" />
              Schedule
            </h4>
            <div className="grid grid-cols-2 gap-4 text-sm">
              <div>
                <span className="text-muted-foreground">Start:</span>
                <div className="font-medium">
                  {formatDate(request.startTs)}
                  {request.startTs && (
                    <span className="text-muted-foreground ml-2">
                      {formatTime(request.startTs)}
                    </span>
                  )}
                </div>
              </div>
              <div>
                <span className="text-muted-foreground">End:</span>
                <div className="font-medium">
                  {formatDate(request.endTs)}
                  {request.endTs && (
                    <span className="text-muted-foreground ml-2">
                      {formatTime(request.endTs)}
                    </span>
                  )}
                </div>
              </div>
            </div>
          </div>

          {/* Duration */}
          <div className="space-y-2">
            <h4 className="text-sm font-medium text-muted-foreground flex items-center gap-2">
              <Clock className="h-4 w-4" />
              Duration
            </h4>
            <div className="text-sm">
              <p className="font-medium">
                {formatDuration(
                  request.minimalDurationValue,
                  request.minimalDurationUnit
                )}
                <span className="text-muted-foreground ml-1">(working time)</span>
              </p>
              {request.actualDurationValue != null && request.actualDurationUnit && (
                <p className="text-muted-foreground mt-1">
                  {formatMinutesHuman(request.actualDurationValue)} total (incl. off-times)
                </p>
              )}
            </div>
          </div>

          {/* Constraints (if set) */}
          {(request.earliestStartTs || request.latestEndTs) && (
            <>
              <Separator />
              <div className="space-y-3">
                <h4 className="text-sm font-medium text-muted-foreground">
                  Constraints
                </h4>
                <div className="grid grid-cols-2 gap-4 text-sm">
                  {request.earliestStartTs && (
                    <div>
                      <span className="text-muted-foreground">
                        Earliest Start:
                      </span>
                      <div className="font-medium">
                        {formatDate(request.earliestStartTs)}
                      </div>
                    </div>
                  )}
                  {request.latestEndTs && (
                    <div>
                      <span className="text-muted-foreground">Latest End:</span>
                      <div className="font-medium">
                        {formatDate(request.latestEndTs)}
                      </div>
                    </div>
                  )}
                </div>
              </div>
            </>
          )}

          {/* Requirements */}
          {request.requirements && request.requirements.length > 0 && (
            <>
              <Separator />
              <div className="space-y-3">
                <h4 className="text-sm font-medium text-muted-foreground flex items-center gap-2">
                  <Tag className="h-4 w-4" />
                  Requirements ({request.requirements.length})
                </h4>
                <div className="space-y-2">
                  {request.requirements.map((req) => (
                    <div
                      key={req.id}
                      className="flex items-center justify-between p-3 border rounded-lg bg-muted/30"
                    >
                      <div className="flex items-center gap-2">
                        <span className="font-medium text-sm">
                          {req.criterion?.name || "Unknown"}
                        </span>
                        {req.criterion?.dataType && (
                          <Badge
                            variant="outline"
                            className={`text-xs ${getDataTypeColor(req.criterion.dataType)}`}
                          >
                            {req.criterion.dataType}
                          </Badge>
                        )}
                      </div>
                      <span className="text-sm">
                        {formatRequirementValue(
                          req.value,
                          req.criterion?.dataType
                        )}
                        {req.criterion?.unit && (
                          <span className="text-muted-foreground ml-1">
                            {req.criterion.unit}
                          </span>
                        )}
                      </span>
                    </div>
                  ))}
                </div>
              </div>
            </>
          )}

          {/* Space assignment */}
          {request.spaceId && (
            <>
              <Separator />
              <div className="space-y-2">
                <h4 className="text-sm font-medium text-muted-foreground flex items-center gap-2">
                  <MapPin className="h-4 w-4" />
                  Assigned Space
                </h4>
                <p className="text-sm font-medium">{request.spaceId}</p>
              </div>
            </>
          )}
        </div>

        <div className="flex justify-end mt-6">
          <Button variant="outline" onClick={() => onOpenChange(false)}>
            Close
          </Button>
        </div>
      </DialogContent>
    </Dialog>
  );
}
