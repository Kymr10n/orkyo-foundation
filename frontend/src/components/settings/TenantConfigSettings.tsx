/**
 * Tenant Configuration Settings Component
 *
 * Displays admin-configurable parameters grouped by category.
 * Supports inline editing with validation, save, and reset-to-default.
 * Only visible to tenant admins / owners / break-glass users.
 */

import { useState, useEffect, useMemo, useCallback } from "react";
import {
  Save,
  Loader2,
  AlertCircle,
  Check,
} from "lucide-react";
import { Button } from "@foundation/src/components/ui/button";
import { Alert, AlertDescription } from "@foundation/src/components/ui/alert";
import { Separator } from "@foundation/src/components/ui/separator";
import { useAuth } from "@foundation/src/contexts/AuthContext";
import {
  useTenantSettings,
  useUpdateTenantSettings,
  useResetTenantSetting,
} from "@foundation/src/hooks/useTenantSettings";
import type { TenantSettingDescriptor, SettingScope } from "@foundation/src/lib/api/tenant-settings-api";
import { validate } from "./tenant-config-helpers";
import { CategoryCard } from "./CategoryCard";
import { SettingsPageHeader } from "./SettingsPageHeader";

// ── Main Component ──────────────────────────────────────────────────

interface TenantConfigSettingsProps {
  /** When set, targets a specific tenant (site-admin context).
   *  Omit to use the current auth tenant context. */
  tenantSlug?: string;
  /** Filter settings to a specific scope. Omit to show all settings. */
  scope?: SettingScope;
}

export function TenantConfigSettings({ tenantSlug, scope }: TenantConfigSettingsProps = {}) {
  const { membership } = useAuth();
  const isAdmin =
    // Site-level scope rendered on AdminPage (already behind RequireAuth guard)
    scope === "site" ||
    // Site-admin context (tenantSlug provided) — already guarded by RequireAuth
    !!tenantSlug ||
    membership?.isTenantAdmin ||
    membership?.isOwner ||
    membership?.isBreakGlass;

  // Site scope → pass null to omit tenant header; otherwise use tenantSlug (or undefined for current tenant)
  const effectiveSlug: string | null | undefined = scope === "site" ? null : tenantSlug;

  const { data, isLoading, error: fetchError } = useTenantSettings(effectiveSlug);
  const updateMutation = useUpdateTenantSettings(effectiveSlug);
  const resetMutation = useResetTenantSetting(effectiveSlug);

  // Local edit state — seeded from server values
  const [editValues, setEditValues] = useState<Record<string, string>>({});
  const [resettingKey, setResettingKey] = useState<string | null>(null);
  const [saveSuccess, setSaveSuccess] = useState(false);

  // Seed edit values when data loads
  useEffect(() => {
    if (data?.settings) {
      const initial: Record<string, string> = {};
      for (const s of data.settings) {
        initial[s.key] = s.currentValue;
      }
      setEditValues(initial);
    }
  }, [data]);

  // Filter by scope if specified, then group by category
  const visibleSettings = useMemo(() => {
    if (!data?.settings) return [];
    return scope ? data.settings.filter((s) => s.scope === scope) : data.settings;
  }, [data, scope]);

  const grouped = useMemo(() => {
    const map = new Map<string, TenantSettingDescriptor[]>();
    for (const s of visibleSettings) {
      const list = map.get(s.category) ?? [];
      list.push(s);
      map.set(s.category, list);
    }
    return map;
  }, [visibleSettings]);

  // Compute dirty keys (only for visible settings)
  const dirtyKeys = useMemo(() => {
    return visibleSettings.filter((s) => {
      const edit = editValues[s.key];
      return edit !== undefined && edit !== s.currentValue;
    });
  }, [visibleSettings, editValues]);

  const hasErrors = useMemo(() => {
    return dirtyKeys.some((s) => validate(s, editValues[s.key]) !== null);
  }, [dirtyKeys, editValues]);

  const handleChange = useCallback((key: string, value: string) => {
    setEditValues((prev) => ({ ...prev, [key]: value }));
    setSaveSuccess(false);
  }, []);

  const handleSave = useCallback(async () => {
    if (dirtyKeys.length === 0 || hasErrors) return;

    const updates: Record<string, string> = {};
    for (const s of dirtyKeys) {
      updates[s.key] = editValues[s.key];
    }

    try {
      await updateMutation.mutateAsync(updates);
      setSaveSuccess(true);
      setTimeout(() => setSaveSuccess(false), 3000);
    } catch {
      // error is available via updateMutation.error
    }
  }, [dirtyKeys, editValues, hasErrors, updateMutation]);

  const handleReset = useCallback(
    async (key: string) => {
      setResettingKey(key);
      try {
        await resetMutation.mutateAsync(key);
        // Update local edit value to default after reset
        const descriptor = data?.settings.find((s) => s.key === key);
        if (descriptor) {
          setEditValues((prev) => ({
            ...prev,
            [key]: descriptor.defaultValue,
          }));
        }
      } catch {
        // error available via resetMutation.error
      } finally {
        setResettingKey(null);
      }
    },
    [resetMutation, data],
  );

  // ── Guard: admin only ───────────────────────────────────────────

  if (!isAdmin) {
    return (
      <Alert>
        <AlertCircle className="h-4 w-4" />
        <AlertDescription>
          Only tenant administrators can manage configuration settings.
        </AlertDescription>
      </Alert>
    );
  }

  // ── Loading ─────────────────────────────────────────────────────

  if (isLoading) {
    return (
      <div className="flex items-center justify-center py-12">
        <Loader2 className="h-8 w-8 animate-spin text-muted-foreground" />
      </div>
    );
  }

  if (fetchError) {
    return (
      <Alert variant="destructive">
        <AlertCircle className="h-4 w-4" />
        <AlertDescription>
          Failed to load configuration settings. Please try again.
        </AlertDescription>
      </Alert>
    );
  }

  // ── Render ──────────────────────────────────────────────────────

  return (
    <div className="space-y-6">
      {/* Header */}
      <SettingsPageHeader
        title="Configuration"
        description={
          <>
            {visibleSettings.length} configurable parameters across{" "}
            {grouped.size} categories.
          </>
        }
      >
        {saveSuccess && (
          <span className="flex items-center gap-1 text-sm text-green-600">
            <Check className="h-4 w-4" />
            Saved
          </span>
        )}
        {updateMutation.error && (
          <span className="text-sm text-destructive">
            {(updateMutation.error).message || "Save failed"}
          </span>
        )}
        <Button
          onClick={handleSave}
          disabled={
            dirtyKeys.length === 0 ||
            hasErrors ||
            updateMutation.isPending
          }
          size="sm"
        >
          {updateMutation.isPending ? (
            <Loader2 className="h-4 w-4 animate-spin mr-2" />
          ) : (
            <Save className="h-4 w-4 mr-2" />
          )}
          Save{dirtyKeys.length > 0 ? ` (${dirtyKeys.length})` : ""}
        </Button>
      </SettingsPageHeader>

      <Separator />

      {/* Category cards */}
      {Array.from(grouped.entries()).map(([category, settings]) => (
        <CategoryCard
          key={category}
          category={category}
          settings={settings}
          editValues={editValues}
          onChange={handleChange}
          onReset={handleReset}
          resettingKey={resettingKey}
        />
      ))}
    </div>
  );
}
