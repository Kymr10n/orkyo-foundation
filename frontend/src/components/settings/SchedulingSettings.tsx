import { useState, useRef, useCallback, useEffect } from "react";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { SettingsPageHeader } from "./SettingsPageHeader";
import { Button } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
import { Switch } from "@/components/ui/switch";
import { TimePicker } from "@/components/ui/time-picker";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { Alert, AlertDescription } from "@/components/ui/alert";
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
  AlertDialogTrigger,
} from "@/components/ui/alert-dialog";
import {
  Clock,
  Globe,
  Calendar,
  Plus,
  Trash2,
  Loader2,
  Check,
  AlertCircle,
  Pencil,
  RotateCcw,
} from "lucide-react";
import { useAppStore } from "@/store/app-store";
import {
  useSchedulingSettings,
  useUpsertSchedulingSettings,
  useDeleteSchedulingSettings,
  useOffTimes,
  useCreateOffTime,
  useUpdateOffTime,
  useDeleteOffTime,
} from "@/hooks/useScheduling";
import type { SchedulingSettings as SchedulingSettingsType } from "@/domain/scheduling/types";
import type { OffTimeDefinition } from "@/domain/scheduling/types";
import { OFF_TIME_TYPE_LABELS } from "@/domain/scheduling/types";
import { OffTimeDialog } from "./OffTimeDialog";

const COMMON_TIMEZONES = [
  "Europe/London",
  "Europe/Berlin",
  "Europe/Paris",
  "Europe/Rome",
  "Europe/Madrid",
  "Europe/Amsterdam",
  "Europe/Brussels",
  "Europe/Vienna",
  "Europe/Zurich",
  "Europe/Stockholm",
  "Europe/Warsaw",
  "Europe/Prague",
  "Europe/Helsinki",
  "Europe/Athens",
  "Europe/Istanbul",
  "America/New_York",
  "America/Chicago",
  "America/Denver",
  "America/Los_Angeles",
  "America/Toronto",
  "Asia/Tokyo",
  "Asia/Shanghai",
  "Asia/Singapore",
  "Asia/Dubai",
  "Australia/Sydney",
] as const;

const HOLIDAY_REGIONS = [
  { code: "AT", name: "Austria" },
  { code: "BE", name: "Belgium" },
  { code: "CH", name: "Switzerland" },
  { code: "CZ", name: "Czech Republic" },
  { code: "DE", name: "Germany" },
  { code: "DK", name: "Denmark" },
  { code: "ES", name: "Spain" },
  { code: "FI", name: "Finland" },
  { code: "FR", name: "France" },
  { code: "GB", name: "United Kingdom" },
  { code: "GR", name: "Greece" },
  { code: "IT", name: "Italy" },
  { code: "NL", name: "Netherlands" },
  { code: "NO", name: "Norway" },
  { code: "PL", name: "Poland" },
  { code: "PT", name: "Portugal" },
  { code: "SE", name: "Sweden" },
  { code: "TR", name: "Turkey" },
  { code: "US", name: "United States" },
  { code: "JP", name: "Japan" },
  { code: "AU", name: "Australia" },
  { code: "SG", name: "Singapore" },
] as const;



interface SettingsFormState {
  timeZone: string;
  workingHoursEnabled: boolean;
  workingDayStart: string;
  workingDayEnd: string;
  weekendsEnabled: boolean;
  publicHolidaysEnabled: boolean;
  publicHolidayRegion: string | null;
}

const DEFAULT_SETTINGS: SettingsFormState = {
  timeZone: "Europe/Berlin",
  workingHoursEnabled: true,
  workingDayStart: "08:00",
  workingDayEnd: "18:00",
  weekendsEnabled: true,
  publicHolidaysEnabled: false,
  publicHolidayRegion: null,
};

function settingsFromApi(s: SchedulingSettingsType): SettingsFormState {
  return {
    timeZone: s.timeZone,
    workingHoursEnabled: s.workingHoursEnabled,
    workingDayStart: s.workingDayStart,
    workingDayEnd: s.workingDayEnd,
    weekendsEnabled: s.weekendsEnabled,
    publicHolidaysEnabled: s.publicHolidaysEnabled,
    publicHolidayRegion: s.publicHolidayRegion,
  };
}

export function SchedulingSettings() {
  const selectedSiteId = useAppStore((s) => s.selectedSiteId);

  const { data: settings, isLoading: settingsLoading } = useSchedulingSettings(selectedSiteId ?? undefined);
  const { data: offTimes = [], isLoading: offTimesLoading } = useOffTimes(selectedSiteId ?? undefined);

  const upsertMutation = useUpsertSchedulingSettings(selectedSiteId ?? "");
  const deleteMutation = useDeleteSchedulingSettings(selectedSiteId ?? "");
  const createOffTimeMutation = useCreateOffTime(selectedSiteId ?? "");
  const updateOffTimeMutation = useUpdateOffTime(selectedSiteId ?? "");
  const deleteOffTimeMutation = useDeleteOffTime(selectedSiteId ?? "");

  const [form, setForm] = useState(DEFAULT_SETTINGS);
  const [offTimeDialogOpen, setOffTimeDialogOpen] = useState(false);
  const [editingOffTime, setEditingOffTime] = useState<OffTimeDefinition | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [saveStatus, setSaveStatus] = useState<"idle" | "saving" | "saved">("idle");

  const debounceRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const formRef = useRef(form);
  formRef.current = form;

  // Seed form once per site when settings arrive
  const [initializedForSite, setInitializedForSite] = useState<string | null>(null);
  if (selectedSiteId && selectedSiteId !== initializedForSite) {
    if (settings) {
      setForm(settingsFromApi(settings));
      setInitializedForSite(selectedSiteId);
    } else if (!settingsLoading) {
      setForm(DEFAULT_SETTINGS);
      setInitializedForSite(selectedSiteId);
    }
  }

  const saveSettings = useCallback(async (formState: SettingsFormState) => {
    if (!selectedSiteId) return;

    // Validate
    if (formState.workingHoursEnabled && formState.workingDayStart >= formState.workingDayEnd) {
      setError("Working day start must be before end.");
      return;
    }
    if (formState.publicHolidaysEnabled && !formState.publicHolidayRegion) {
      setError("Select a region for public holidays.");
      return;
    }

    setError(null);
    setSaveStatus("saving");
    try {
      await upsertMutation.mutateAsync(formState);
      setSaveStatus("saved");
    } catch (e) {
      setError(e instanceof Error ? e.message : "Failed to save settings.");
      setSaveStatus("idle");
    }
  }, [selectedSiteId, upsertMutation]);

  // Clear "saved" indicator after 2s
  useEffect(() => {
    if (saveStatus !== "saved") return;
    const t = setTimeout(() => setSaveStatus("idle"), 2000);
    return () => clearTimeout(t);
  }, [saveStatus]);

  // Cleanup debounce on unmount
  useEffect(() => {
    return () => { if (debounceRef.current) clearTimeout(debounceRef.current); };
  }, []);

  const updateField = <K extends keyof SettingsFormState>(key: K, value: SettingsFormState[K]) => {
    const next = { ...formRef.current, [key]: value };
    setForm(next);
    setError(null);
    setSaveStatus("idle");

    if (debounceRef.current) clearTimeout(debounceRef.current);
    debounceRef.current = setTimeout(() => { void saveSettings(next); }, 600);
  };

  const handleReset = async () => {
    if (!selectedSiteId) return;
    if (debounceRef.current) clearTimeout(debounceRef.current);
    try {
      await deleteMutation.mutateAsync();
      setForm(DEFAULT_SETTINGS);
      setInitializedForSite(null);
      setSaveStatus("saved");
    } catch (e) {
      setError(e instanceof Error ? e.message : "Failed to reset settings.");
    }
  };

  const handleCreateOffTime = () => {
    setEditingOffTime(null);
    setOffTimeDialogOpen(true);
  };

  const handleEditOffTime = (ot: OffTimeDefinition) => {
    setEditingOffTime(ot);
    setOffTimeDialogOpen(true);
  };

  const handleSaveOffTime = async (data: Omit<OffTimeDefinition, "id" | "siteId">) => {
    if (editingOffTime) {
      await updateOffTimeMutation.mutateAsync({
        offTimeId: editingOffTime.id,
        updates: data,
      });
    } else {
      await createOffTimeMutation.mutateAsync(data);
    }
    setOffTimeDialogOpen(false);
    setEditingOffTime(null);
  };

  const handleDeleteOffTime = async (id: string) => {
    await deleteOffTimeMutation.mutateAsync(id);
  };

  if (!selectedSiteId) {
    return (
      <div className="flex items-center justify-center h-64">
        <p className="text-muted-foreground">Select a site to configure scheduling.</p>
      </div>
    );
  }

  if (settingsLoading || offTimesLoading) {
    return (
      <div className="flex items-center justify-center h-64">
        <Loader2 className="h-6 w-6 animate-spin text-muted-foreground" />
      </div>
    );
  }

  return (
    <div className="space-y-6">
      <SettingsPageHeader
        title="Scheduling"
        description="Configure working hours, weekends, holidays, and off-times for this site."
      >
        <div className="flex items-center gap-2 text-sm text-muted-foreground">
          {saveStatus === "saving" && (
            <><Loader2 className="h-3.5 w-3.5 animate-spin" /> Saving...</>
          )}
          {saveStatus === "saved" && (
            <><Check className="h-3.5 w-3.5 text-green-500" /> Saved</>
          )}
        </div>
        <AlertDialog>
          <AlertDialogTrigger asChild>
            <Button variant="outline" size="sm" disabled={!settings}>
              <RotateCcw className="h-4 w-4 mr-2" />
              Reset to Defaults
            </Button>
          </AlertDialogTrigger>
          <AlertDialogContent>
            <AlertDialogHeader>
              <AlertDialogTitle>Reset scheduling settings?</AlertDialogTitle>
              <AlertDialogDescription>
                This will remove all custom scheduling settings for this site.
              </AlertDialogDescription>
            </AlertDialogHeader>
            <AlertDialogFooter>
              <AlertDialogCancel>Cancel</AlertDialogCancel>
              <AlertDialogAction onClick={handleReset}>Reset</AlertDialogAction>
            </AlertDialogFooter>
          </AlertDialogContent>
        </AlertDialog>
      </SettingsPageHeader>

      {error && (
        <Alert variant="destructive">
          <AlertCircle className="h-4 w-4" />
          <AlertDescription>{error}</AlertDescription>
        </Alert>
      )}

      {/* Timezone */}
      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-2">
            <Globe className="h-5 w-5" />
            Timezone
          </CardTitle>
          <CardDescription>
            All scheduling calculations use this timezone.
          </CardDescription>
        </CardHeader>
        <CardContent>
          <Select
            value={form.timeZone}
            onValueChange={(v) => updateField("timeZone", v)}
          >
            <SelectTrigger className="w-64">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              {COMMON_TIMEZONES.map((tz) => (
                <SelectItem key={tz} value={tz}>{tz}</SelectItem>
              ))}
            </SelectContent>
          </Select>
        </CardContent>
      </Card>

      {/* Working Hours */}
      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-2">
            <Clock className="h-5 w-5" />
            Working Hours
          </CardTitle>
          <CardDescription>
            Define the working day. Time outside these hours is not counted as working time.
          </CardDescription>
        </CardHeader>
        <CardContent className="space-y-4">
          <div className="flex items-center justify-between">
            <Label htmlFor="working-hours-enabled">Enable working hours</Label>
            <Switch
              id="working-hours-enabled"
              checked={form.workingHoursEnabled}
              onCheckedChange={(v) => updateField("workingHoursEnabled", v)}
            />
          </div>
          {form.workingHoursEnabled && (
            <div className="flex items-center gap-4">
              <div className="space-y-1">
                <Label htmlFor="working-start">Start</Label>
                <TimePicker
                  id="working-start"
                  value={form.workingDayStart}
                  onChange={(v) => updateField("workingDayStart", v)}
                />
              </div>
              <span className="text-muted-foreground mt-6">to</span>
              <div className="space-y-1">
                <Label htmlFor="working-end">End</Label>
                <TimePicker
                  id="working-end"
                  value={form.workingDayEnd}
                  onChange={(v) => updateField("workingDayEnd", v)}
                />
              </div>
            </div>
          )}
        </CardContent>
      </Card>

      {/* Weekends */}
      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-2">
            <Calendar className="h-5 w-5" />
            Weekends
          </CardTitle>
          <CardDescription>
            When enabled, Saturday and Sunday are excluded from working time.
          </CardDescription>
        </CardHeader>
        <CardContent>
          <div className="flex items-center justify-between">
            <Label htmlFor="weekends-enabled">Exclude weekends</Label>
            <Switch
              id="weekends-enabled"
              checked={!form.weekendsEnabled}
              onCheckedChange={(v) => updateField("weekendsEnabled", !v)}
            />
          </div>
        </CardContent>
      </Card>

      {/* Public Holidays */}
      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-2">
            <Calendar className="h-5 w-5" />
            Public Holidays
          </CardTitle>
          <CardDescription>
            Automatically exclude public holidays based on a country/region.
          </CardDescription>
        </CardHeader>
        <CardContent className="space-y-4">
          <div className="flex items-center justify-between">
            <Label htmlFor="holidays-enabled">Enable public holidays</Label>
            <Switch
              id="holidays-enabled"
              checked={form.publicHolidaysEnabled}
              onCheckedChange={(v) => updateField("publicHolidaysEnabled", v)}
            />
          </div>
          {form.publicHolidaysEnabled && (
            <div className="space-y-1">
              <Label htmlFor="holiday-region">Region</Label>
              <Select
                value={form.publicHolidayRegion ?? ""}
                onValueChange={(v) => updateField("publicHolidayRegion", v || null)}
              >
                <SelectTrigger className="w-64">
                  <SelectValue placeholder="Select region" />
                </SelectTrigger>
                <SelectContent>
                  {HOLIDAY_REGIONS.map((r) => (
                    <SelectItem key={r.code} value={r.code}>
                      {r.name} ({r.code})
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
          )}
        </CardContent>
      </Card>

      {/* Off-Times */}
      <Card>
        <CardHeader>
          <div className="flex items-center justify-between">
            <div>
              <CardTitle className="flex items-center gap-2">
                <Calendar className="h-5 w-5" />
                Off-Times
              </CardTitle>
              <CardDescription>
                Define periods when no work should be scheduled (closures, maintenance, etc.).
              </CardDescription>
            </div>
            <Button size="sm" onClick={handleCreateOffTime}>
              <Plus className="h-4 w-4 mr-1" />
              Add
            </Button>
          </div>
        </CardHeader>
        <CardContent>
          {offTimes.length === 0 ? (
            <p className="text-sm text-muted-foreground">No off-times configured.</p>
          ) : (
            <div className="space-y-2">
              {offTimes.map((ot) => (
                <div
                  key={ot.id}
                  className="flex items-center justify-between p-3 border rounded-lg"
                >
                  <div className="min-w-0 flex-1">
                    <div className="flex items-center gap-2">
                      <span className="font-medium text-sm">{ot.title}</span>
                      <span className="text-xs px-1.5 py-0.5 rounded bg-muted text-muted-foreground">
                        {OFF_TIME_TYPE_LABELS[ot.type]}
                      </span>
                      {ot.isRecurring && (
                        <span className="text-xs px-1.5 py-0.5 rounded bg-blue-100 text-blue-700 dark:bg-blue-900 dark:text-blue-300">
                          Recurring
                        </span>
                      )}
                      {!ot.enabled && (
                        <span className="text-xs px-1.5 py-0.5 rounded bg-yellow-100 text-yellow-700 dark:bg-yellow-900 dark:text-yellow-300">
                          Disabled
                        </span>
                      )}
                    </div>
                    <div className="text-xs text-muted-foreground mt-1">
                      {new Date(ot.startMs).toLocaleDateString()} – {new Date(ot.endMs).toLocaleDateString()}
                      {ot.appliesToAllSpaces ? " · All spaces" : ` · ${ot.spaceIds.length} space(s)`}
                    </div>
                  </div>
                  <div className="flex items-center gap-1">
                    <Button
                      variant="ghost"
                      size="icon"
                      onClick={() => handleEditOffTime(ot)}
                    >
                      <Pencil className="h-4 w-4" />
                    </Button>
                    <AlertDialog>
                      <AlertDialogTrigger asChild>
                        <Button variant="ghost" size="icon">
                          <Trash2 className="h-4 w-4 text-destructive" />
                        </Button>
                      </AlertDialogTrigger>
                      <AlertDialogContent>
                        <AlertDialogHeader>
                          <AlertDialogTitle>Delete off-time</AlertDialogTitle>
                          <AlertDialogDescription>
                            Delete "{ot.title}"? This cannot be undone.
                          </AlertDialogDescription>
                        </AlertDialogHeader>
                        <AlertDialogFooter>
                          <AlertDialogCancel>Cancel</AlertDialogCancel>
                          <AlertDialogAction onClick={() => handleDeleteOffTime(ot.id)}>
                            Delete
                          </AlertDialogAction>
                        </AlertDialogFooter>
                      </AlertDialogContent>
                    </AlertDialog>
                  </div>
                </div>
              ))}
            </div>
          )}
        </CardContent>
      </Card>



      {/* Off-Time Dialog */}
      <OffTimeDialog
        open={offTimeDialogOpen}
        onOpenChange={setOffTimeDialogOpen}
        offTime={editingOffTime}
        onSave={handleSaveOffTime}
      />
    </div>
  );
}
