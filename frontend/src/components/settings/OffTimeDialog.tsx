import { useState, useEffect } from "react";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from "@foundation/src/components/ui/dialog";
import { Button } from "@foundation/src/components/ui/button";
import { Input } from "@foundation/src/components/ui/input";
import { Label } from "@foundation/src/components/ui/label";
import { DateTimePicker } from "@foundation/src/components/ui/date-time-picker";
import { Switch } from "@foundation/src/components/ui/switch";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@foundation/src/components/ui/select";
import { Loader2 } from "lucide-react";
import { Alert, AlertDescription } from "@foundation/src/components/ui/alert";
import { useAppStore } from "@foundation/src/store/app-store";
import { getSpaces } from "@foundation/src/lib/api/space-api";
import type { Space } from "@foundation/src/types/space";
import type { OffTimeDefinition, OffTimeType } from "@foundation/src/domain/scheduling/types";
import { OFF_TIME_TYPE_LABELS } from "@foundation/src/domain/scheduling/types";

interface OffTimeDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  offTime: OffTimeDefinition | null;
  onSave: (data: Omit<OffTimeDefinition, "id" | "siteId">) => Promise<void>;
}

function toDateTimeLocal(epochMs: number): string {
  const d = new Date(epochMs);
  const pad = (n: number) => String(n).padStart(2, "0");
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
}

export function OffTimeDialog({ open, onOpenChange, offTime, onSave }: OffTimeDialogProps) {
  const selectedSiteId = useAppStore((s) => s.selectedSiteId);

  const [title, setTitle] = useState("");
  const [type, setType] = useState<OffTimeType>("maintenance");
  const [startLocal, setStartLocal] = useState("");
  const [endLocal, setEndLocal] = useState("");
  const [appliesToAllSpaces, setAppliesToAllSpaces] = useState(true);
  const [spaceIds, setSpaceIds] = useState<string[]>([]);
  const [isRecurring, setIsRecurring] = useState(false);
  const [recurrenceRule, setRecurrenceRule] = useState("");
  const [enabled, setEnabled] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [spaces, setSpaces] = useState<Space[]>([]);

  // Load spaces for space picker
  useEffect(() => {
    if (open && selectedSiteId) {
      getSpaces(selectedSiteId).then(setSpaces).catch(() => setSpaces([]));
    }
  }, [open, selectedSiteId]);

  // Seed form when editing
  useEffect(() => {
    if (open) {
      if (offTime) {
        setTitle(offTime.title);
        setType(offTime.type);
        setStartLocal(toDateTimeLocal(offTime.startMs));
        setEndLocal(toDateTimeLocal(offTime.endMs));
        setAppliesToAllSpaces(offTime.appliesToAllSpaces);
        setSpaceIds(offTime.spaceIds);
        setIsRecurring(offTime.isRecurring);
        setRecurrenceRule(offTime.recurrenceRule ?? "");
        setEnabled(offTime.enabled);
      } else {
        setTitle("");
        setType("maintenance");
        setStartLocal("");
        setEndLocal("");
        setAppliesToAllSpaces(true);
        setSpaceIds([]);
        setIsRecurring(false);
        setRecurrenceRule("");
        setEnabled(true);
      }
      setError(null);
    }
  }, [open, offTime]);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError(null);

    if (!title.trim()) {
      setError("Title is required.");
      return;
    }
    if (!startLocal || !endLocal) {
      setError("Start and end dates are required.");
      return;
    }

    const startMs = new Date(startLocal).getTime();
    const endMs = new Date(endLocal).getTime();

    if (startMs >= endMs) {
      setError("Start must be before end.");
      return;
    }

    if (isRecurring && !recurrenceRule.trim()) {
      setError("Recurrence rule is required when recurring is enabled.");
      return;
    }

    setSaving(true);
    try {
      await onSave({
        title: title.trim(),
        type,
        appliesToAllSpaces,
        spaceIds: appliesToAllSpaces ? [] : spaceIds,
        startMs,
        endMs,
        isRecurring,
        recurrenceRule: isRecurring ? recurrenceRule.trim() : null,
        enabled,
      });
    } catch (e) {
      setError(e instanceof Error ? e.message : "Failed to save off-time.");
    } finally {
      setSaving(false);
    }
  };

  const toggleSpaceId = (id: string) => {
    setSpaceIds((prev) =>
      prev.includes(id) ? prev.filter((s) => s !== id) : [...prev, id]
    );
  };

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-lg">
        <DialogHeader>
          <DialogTitle>{offTime ? "Edit Off-Time" : "Add Off-Time"}</DialogTitle>
          <DialogDescription>
            {offTime ? "Update the off-time definition." : "Define a new period when work should not be scheduled."}
          </DialogDescription>
        </DialogHeader>

        <form onSubmit={handleSubmit} className="space-y-4">
          {error && (
            <Alert variant="destructive">
              <AlertDescription>{error}</AlertDescription>
            </Alert>
          )}

          <div className="space-y-2">
            <Label htmlFor="off-time-title">Title</Label>
            <Input
              id="off-time-title"
              value={title}
              onChange={(e) => setTitle(e.target.value)}
              placeholder="e.g., Christmas Shutdown"
            />
          </div>

          <div className="space-y-2">
            <Label htmlFor="off-time-type">Type</Label>
            <Select value={type} onValueChange={(v) => setType(v as OffTimeType)}>
              <SelectTrigger id="off-time-type">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {(Object.entries(OFF_TIME_TYPE_LABELS) as [OffTimeType, string][]).map(
                  ([value, label]) => (
                    <SelectItem key={value} value={value}>{label}</SelectItem>
                  )
                )}
              </SelectContent>
            </Select>
          </div>

          <div className="space-y-2">
            <Label htmlFor="off-time-start">Start</Label>
            <DateTimePicker
              id="off-time-start"
              value={startLocal}
              onChange={setStartLocal}
            />
          </div>
          <div className="space-y-2">
            <Label htmlFor="off-time-end">End</Label>
            <DateTimePicker
              id="off-time-end"
              value={endLocal}
              onChange={setEndLocal}
            />
          </div>

          <div className="flex items-center justify-between">
            <Label htmlFor="off-time-enabled">Enabled</Label>
            <Switch
              id="off-time-enabled"
              checked={enabled}
              onCheckedChange={setEnabled}
            />
          </div>

          <div className="flex items-center justify-between">
            <Label htmlFor="off-time-all-spaces">Applies to all spaces</Label>
            <Switch
              id="off-time-all-spaces"
              checked={appliesToAllSpaces}
              onCheckedChange={setAppliesToAllSpaces}
            />
          </div>

          {!appliesToAllSpaces && (
            <div className="space-y-2">
              <Label>Spaces</Label>
              <div className="max-h-40 overflow-y-auto border rounded-md p-2 space-y-1">
                {spaces.length === 0 ? (
                  <p className="text-sm text-muted-foreground">No spaces available.</p>
                ) : (
                  spaces.map((space) => (
                    <label
                      key={space.id}
                      className="flex items-center gap-2 cursor-pointer text-sm py-1 px-1 rounded hover:bg-muted"
                    >
                      <input
                        type="checkbox"
                        checked={spaceIds.includes(space.id)}
                        onChange={() => toggleSpaceId(space.id)}
                        className="rounded"
                      />
                      <span>{space.code} – {space.name}</span>
                    </label>
                  ))
                )}
              </div>
            </div>
          )}

          <div className="flex items-center justify-between">
            <Label htmlFor="off-time-recurring">Recurring</Label>
            <Switch
              id="off-time-recurring"
              checked={isRecurring}
              onCheckedChange={setIsRecurring}
            />
          </div>

          {isRecurring && (
            <div className="space-y-2">
              <Label htmlFor="off-time-rrule">Recurrence Rule (RRULE)</Label>
              <Input
                id="off-time-rrule"
                value={recurrenceRule}
                onChange={(e) => setRecurrenceRule(e.target.value)}
                placeholder="e.g., FREQ=YEARLY;BYMONTH=12;BYMONTHDAY=24"
              />
              <p className="text-xs text-muted-foreground">
                RFC 5545 RRULE format. Example: FREQ=WEEKLY;BYDAY=SA,SU
              </p>
            </div>
          )}

          <div className="flex justify-end gap-3 pt-2">
            <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>
              Cancel
            </Button>
            <Button type="submit" disabled={saving}>
              {saving && <Loader2 className="h-4 w-4 mr-2 animate-spin" />}
              {offTime ? "Update" : "Create"}
            </Button>
          </div>
        </form>
      </DialogContent>
    </Dialog>
  );
}
