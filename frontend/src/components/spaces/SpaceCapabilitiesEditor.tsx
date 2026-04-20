import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { ScrollArea } from "@/components/ui/scroll-area";
import { Separator } from "@/components/ui/separator";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { getCriteria } from "@/lib/api/criteria-api";
import {
  addSpaceCapability,
  deleteSpaceCapability,
  getSpaceCapabilities,
} from "@/lib/api/space-capability-api";
import { getDataTypeColor } from "@/lib/utils";
import type { Criterion, CriterionValue } from "@/types/criterion";
import { Plus, Trash2 } from "lucide-react";
import { useEffect, useState } from "react";
import { CriterionRequirementInput } from "../requests/CriterionRequirementInput";
import { logger } from "@/lib/core/logger";

interface SpaceCapabilitiesEditorProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  siteId: string;
  spaceId: string;
  spaceName: string;
}

export function SpaceCapabilitiesEditor({
  open,
  onOpenChange,
  siteId,
  spaceId,
  spaceName,
}: SpaceCapabilitiesEditorProps) {
  const [capabilities, setCapabilities] = useState(new Map());
  const [availableCriteria, setAvailableCriteria] = useState<Criterion[]>([]);
  const [isLoadingCriteria, setIsLoadingCriteria] = useState(false);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [selectedCriterionId, setSelectedCriterionId] = useState("");
  const [error, setError] = useState<string | null>(null);

  // Load data when dialog opens
  useEffect(() => {
    const loadData = async () => {
      if (!open) return;

      setIsLoadingCriteria(true);
      setError(null);
      try {
        const [capsData, criteriaData] = await Promise.all([
          getSpaceCapabilities(siteId, spaceId),
          getCriteria(),
        ]);

        // Convert capabilities array to map
        const capMap = new Map<string, CriterionValue | null>();
        capsData.forEach((cap) => {
          capMap.set(cap.criterionId, cap.value);
        });
        setCapabilities(capMap);
        setAvailableCriteria(criteriaData);
      } catch (err) {
        logger.error("Failed to load data:", err);
        setError(
          err instanceof Error ? err.message : "Failed to load capabilities"
        );
      } finally {
        setIsLoadingCriteria(false);
      }
    };

    loadData();
  }, [open, siteId, spaceId]);

  const handleAddCapability = () => {
    if (!selectedCriterionId) return;

    const criterion = availableCriteria.find(
      (c) => c.id === selectedCriterionId
    );
    if (!criterion) return;

    // Set default value based on type
    let defaultValue: CriterionValue | null = null;
    if (criterion.dataType === "Boolean") {
      defaultValue = false;
    }

    const newCapabilities = new Map(capabilities);
    newCapabilities.set(selectedCriterionId, defaultValue);
    setCapabilities(newCapabilities);
    setSelectedCriterionId("");
  };

  const handleRemoveCapability = (criterionId: string) => {
    const newCapabilities = new Map(capabilities);
    newCapabilities.delete(criterionId);
    setCapabilities(newCapabilities);
  };

  const handleCapabilityValueChange = (criterionId: string, value: CriterionValue | null) => {
    const newCapabilities = new Map(capabilities);
    newCapabilities.set(criterionId, value);
    setCapabilities(newCapabilities);
  };

  const handleSave = async () => {
    setIsSubmitting(true);
    setError(null);

    try {
      // Get existing capabilities
      const existingCaps = await getSpaceCapabilities(siteId, spaceId);
      const existingMap = new Map(
        existingCaps.map((cap) => [cap.criterionId, cap.id])
      );

      // Determine which to add and which to delete
      const toAdd: { criterionId: string; value: CriterionValue }[] = [];
      const toDelete: string[] = [];

      // Find new capabilities to add
      capabilities.forEach((value, criterionId) => {
        if (!existingMap.has(criterionId) && value !== null) {
          toAdd.push({ criterionId, value });
        }
      });

      // Find capabilities to delete
      existingMap.forEach((capId, criterionId) => {
        if (!capabilities.has(criterionId)) {
          toDelete.push(capId);
        }
      });

      // Execute changes
      await Promise.all([
        ...toAdd.map((cap) => addSpaceCapability(siteId, spaceId, cap)),
        ...toDelete.map((capId) =>
          deleteSpaceCapability(siteId, spaceId, capId)
        ),
      ]);

      onOpenChange(false);
    } catch (err) {
      logger.error("Failed to save capabilities:", err);
      setError(
        err instanceof Error ? err.message : "Failed to save capabilities"
      );
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-2xl max-h-[90vh] flex flex-col p-0">
        <DialogHeader className="px-6 pt-6 pb-4">
          <DialogTitle>Capabilities for {spaceName}</DialogTitle>
        </DialogHeader>

        <ScrollArea className="flex-1 px-6">
          <div className="space-y-6 pb-6">
            <p className="text-sm text-muted-foreground">
              Define criterion values that describe this space's characteristics
            </p>

            <Separator />

            {/* Capabilities */}
            <div className="space-y-4">
              <div className="flex items-center justify-between">
                <h3 className="text-sm font-medium">Capabilities</h3>
                <Badge variant="outline" className="text-xs">
                  {capabilities.size} active
                </Badge>
              </div>

              {/* Add Capability */}
              {availableCriteria.filter((c) => !capabilities.has(c.id))
                .length > 0 && (
                <div className="flex gap-2">
                  <Select
                    value={selectedCriterionId}
                    onValueChange={setSelectedCriterionId}
                    disabled={isLoadingCriteria || isSubmitting}
                  >
                    <SelectTrigger className="flex-1">
                      <SelectValue placeholder="Select a criterion to add" />
                    </SelectTrigger>
                    <SelectContent>
                      {availableCriteria
                        .filter((c) => !capabilities.has(c.id))
                        .map((criterion) => (
                          <SelectItem key={criterion.id} value={criterion.id}>
                            <div className="flex items-center gap-2">
                              <span>{criterion.name}</span>
                              <Badge
                                variant="outline"
                                className={`text-xs ${getDataTypeColor(criterion.dataType)}`}
                              >
                                {criterion.dataType}
                              </Badge>
                            </div>
                          </SelectItem>
                        ))}
                    </SelectContent>
                  </Select>
                  <Button
                    type="button"
                    onClick={handleAddCapability}
                    disabled={!selectedCriterionId || isSubmitting}
                    size="sm"
                  >
                    <Plus className="h-4 w-4" />
                  </Button>
                </div>
              )}

              {/* Active Capabilities */}
              {capabilities.size === 0 ? (
                <div className="text-center py-8 text-sm text-muted-foreground border rounded-lg border-dashed">
                  No capabilities added yet. Add criteria to specify
                  capabilities.
                </div>
              ) : (
                <div className="space-y-4 border rounded-lg p-4">
                  {Array.from(capabilities.entries()).map(
                    ([criterionId, value]) => {
                      const criterion = availableCriteria.find(
                        (c) => c.id === criterionId
                      );
                      if (!criterion) return null;

                      return (
                        <div key={criterionId} className="flex gap-3">
                          <div className="flex-1">
                            <CriterionRequirementInput
                              criterion={criterion}
                              value={value}
                              onChange={(newValue) =>
                                handleCapabilityValueChange(
                                  criterionId,
                                  newValue
                                )
                              }
                            />
                          </div>
                          <Button
                            type="button"
                            variant="ghost"
                            size="sm"
                            onClick={() =>
                              handleRemoveCapability(criterionId)
                            }
                            className="mt-7"
                            disabled={isSubmitting}
                          >
                            <Trash2 className="h-4 w-4 text-destructive" />
                          </Button>
                        </div>
                      );
                    }
                  )}
                </div>
              )}
            </div>

            {/* Error Message */}
            {error && (
              <div className="text-sm text-destructive bg-destructive/10 p-3 rounded-md">
                {error}
              </div>
            )}
          </div>
        </ScrollArea>

        <Separator />
        <DialogFooter className="px-6 py-4">
          <Button
            type="button"
            variant="outline"
            onClick={() => onOpenChange(false)}
            disabled={isSubmitting}
          >
            Cancel
          </Button>
          <Button onClick={handleSave} disabled={isSubmitting}>
            {isSubmitting ? "Saving..." : "Save Changes"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
