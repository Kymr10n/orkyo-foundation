import { useCallback, useEffect, useState } from 'react';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@foundation/src/components/ui/card';
import { Input } from '@foundation/src/components/ui/input';
import { Label } from '@foundation/src/components/ui/label';
import { Button } from '@foundation/src/components/ui/button';
import { Switch } from '@foundation/src/components/ui/switch';
import { Badge } from '@foundation/src/components/ui/badge';
import { Separator } from '@foundation/src/components/ui/separator';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@foundation/src/components/ui/select';
import {
  type AdminSettingsResponse,
  getAdminSettings,
  updateAdminSettings,
} from '@foundation/src/lib/api/admin-api';
import { logger } from '@foundation/src/lib/core/logger';
import { CheckCircle2, Loader2, Lock, Save, XCircle } from 'lucide-react';

// Common IANA timezones — use Intl API when available, fall back to curated list
const TIMEZONES: string[] = (() => {
  try {
    return Intl.supportedValuesOf('timeZone');
  } catch {
    return [
      'UTC', 'Europe/London', 'Europe/Berlin', 'Europe/Zurich', 'Europe/Paris',
      'America/New_York', 'America/Chicago', 'America/Denver', 'America/Los_Angeles',
      'Asia/Tokyo', 'Asia/Shanghai', 'Asia/Kolkata', 'Australia/Sydney',
    ];
  }
})();

type RuntimeFields = AdminSettingsResponse['runtime'];

export function SettingsTab() {
  const [data, setData] = useState<AdminSettingsResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');
  const [successMsg, setSuccessMsg] = useState('');

  // Editable runtime fields — initialised from API response
  const [draft, setDraft] = useState<RuntimeFields | null>(null);

  const load = useCallback(async () => {
    try {
      setError('');
      const resp = await getAdminSettings();
      setData(resp);
      setDraft(resp.runtime);
    } catch (err) {
      logger.error('Failed to load admin settings:', err);
      setError('Failed to load settings');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { load(); }, [load]);

  const handleSave = async () => {
    if (!draft || !data) return;

    // Build diff — only send changed values
    const changes: Record<string, string> = {};
    const prev = data.runtime;
    if (draft.defaultTimezone !== prev.defaultTimezone)
      changes.DefaultTimezone = draft.defaultTimezone;
    if (draft.workingHoursStart !== prev.workingHoursStart)
      changes.WorkingHoursStart = draft.workingHoursStart;
    if (draft.workingHoursEnd !== prev.workingHoursEnd)
      changes.WorkingHoursEnd = draft.workingHoursEnd;
    if (draft.holidayProviderEnabled !== prev.holidayProviderEnabled)
      changes.HolidayProviderEnabled = String(draft.holidayProviderEnabled);
    if (draft.brandingName !== prev.brandingName)
      changes.BrandingName = draft.brandingName;
    if (draft.brandingLogoUrl !== prev.brandingLogoUrl)
      changes.BrandingLogoUrl = draft.brandingLogoUrl;

    if (Object.keys(changes).length === 0) return;

    setSaving(true);
    setError('');
    setSuccessMsg('');
    try {
      const result = await updateAdminSettings(changes);
      // Update local state with server response
      setData(d => d ? { ...d, runtime: result.runtime } : d);
      setDraft(result.runtime);
      setSuccessMsg(`Updated ${result.updatedKeys.length} setting(s)`);
      setTimeout(() => setSuccessMsg(''), 4000);
    } catch (err) {
      logger.error('Failed to save settings:', err);
      setError(err instanceof Error ? err.message : 'Failed to save settings');
    } finally {
      setSaving(false);
    }
  };

  const isDirty = (() => {
    if (!draft || !data) return false;
    const prev = data.runtime;
    return (
      draft.defaultTimezone !== prev.defaultTimezone ||
      draft.workingHoursStart !== prev.workingHoursStart ||
      draft.workingHoursEnd !== prev.workingHoursEnd ||
      draft.holidayProviderEnabled !== prev.holidayProviderEnabled ||
      draft.brandingName !== prev.brandingName ||
      draft.brandingLogoUrl !== prev.brandingLogoUrl
    );
  })();

  if (loading) {
    return (
      <Card>
        <CardContent className="py-8">
          <div className="flex items-center justify-center gap-2 text-muted-foreground">
            <Loader2 className="h-4 w-4 animate-spin" />
            Loading settings...
          </div>
        </CardContent>
      </Card>
    );
  }

  if (!data || !draft) {
    return (
      <Card>
        <CardContent className="py-8">
          <div className="text-center text-destructive">{error || 'Failed to load settings'}</div>
        </CardContent>
      </Card>
    );
  }

  return (
    <div className="space-y-6">
      {/* ── General ──────────────────────────────────────── */}
      <Card>
        <CardHeader>
          <CardTitle>General</CardTitle>
          <CardDescription>Platform-wide general settings</CardDescription>
        </CardHeader>
        <CardContent className="space-y-4">
          <div className="grid gap-2">
            <Label htmlFor="timezone">Default Timezone</Label>
            <Select value={draft.defaultTimezone} onValueChange={v => setDraft(d => d && ({ ...d, defaultTimezone: v }))}>
              <SelectTrigger id="timezone" className="w-[320px]">
                <SelectValue />
              </SelectTrigger>
              <SelectContent className="max-h-60">
                {TIMEZONES.map(tz => (
                  <SelectItem key={tz} value={tz}>{tz}</SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>

          <Separator />

          <ReadOnlyField label="Public URL" value={data.deployment.publicUrl} />
          <ReadOnlyField label="API / Auth URL" value={data.deployment.authPublicUrl} />
        </CardContent>
      </Card>

      {/* ── Scheduling ───────────────────────────────────── */}
      <Card>
        <CardHeader>
          <CardTitle>Scheduling</CardTitle>
          <CardDescription>Default working hours and scheduling behavior</CardDescription>
        </CardHeader>
        <CardContent className="space-y-4">
          <div className="grid grid-cols-2 gap-4 max-w-md">
            <div className="space-y-2">
              <Label htmlFor="wh-start">Working Hours Start</Label>
              <Input
                id="wh-start"
                type="time"
                value={draft.workingHoursStart}
                onChange={e => setDraft(d => d && ({ ...d, workingHoursStart: e.target.value }))}
              />
            </div>
            <div className="space-y-2">
              <Label htmlFor="wh-end">Working Hours End</Label>
              <Input
                id="wh-end"
                type="time"
                value={draft.workingHoursEnd}
                onChange={e => setDraft(d => d && ({ ...d, workingHoursEnd: e.target.value }))}
              />
            </div>
          </div>

          <div className="flex items-center gap-3">
            <Switch
              id="holiday"
              checked={draft.holidayProviderEnabled}
              onCheckedChange={v => setDraft(d => d && ({ ...d, holidayProviderEnabled: v }))}
            />
            <Label htmlFor="holiday">Holiday Provider</Label>
          </div>
        </CardContent>
      </Card>

      {/* ── Branding ─────────────────────────────────────── */}
      <Card>
        <CardHeader>
          <CardTitle>Branding</CardTitle>
          <CardDescription>Platform name and visual identity</CardDescription>
        </CardHeader>
        <CardContent className="space-y-4">
          <div className="grid gap-2 max-w-md">
            <Label htmlFor="brand-name">Platform Name</Label>
            <Input
              id="brand-name"
              value={draft.brandingName}
              onChange={e => setDraft(d => d && ({ ...d, brandingName: e.target.value }))}
              maxLength={100}
            />
          </div>
          <div className="grid gap-2 max-w-md">
            <Label htmlFor="brand-logo">Logo URL</Label>
            <Input
              id="brand-logo"
              value={draft.brandingLogoUrl}
              onChange={e => setDraft(d => d && ({ ...d, brandingLogoUrl: e.target.value }))}
              placeholder="https://example.com/logo.svg"
            />
          </div>
        </CardContent>
      </Card>

      {/* ── System Info ──────────────────────────────────── */}
      <Card>
        <CardHeader>
          <CardTitle>System Info</CardTitle>
          <CardDescription>
            Deployment-managed values — these cannot be changed in the UI.
            <span className="block mt-1 text-xs">
              To change these, update the environment configuration and restart.
            </span>
          </CardDescription>
        </CardHeader>
        <CardContent className="space-y-3">
          <InfoRow label="Version" value={data.systemInfo.version} />
          <InfoRow
            label="Database"
            value={data.systemInfo.databaseStatus}
            badge={data.systemInfo.databaseStatus === 'healthy' ? 'success' : 'destructive'}
          />
          <InfoRow
            label="SMTP"
            value={data.systemInfo.smtpConfigured ? `Configured (${data.deployment.smtpHost}:${data.deployment.smtpPort})` : 'Not configured'}
            badge={data.systemInfo.smtpConfigured ? 'success' : 'warning'}
          />
          <InfoRow
            label="Auth Provider"
            value={`${data.systemInfo.authProvider} / ${data.systemInfo.authRealm}`}
          />
          <InfoRow label="File Storage" value={data.deployment.fileStoragePath} />
          <InfoRow label="Log Level" value={data.deployment.logLevel} />
        </CardContent>
      </Card>

      {/* ── Save bar ─────────────────────────────────────── */}
      {(isDirty || error || successMsg) && (
        <div className="sticky bottom-4 flex items-center gap-3 rounded-lg border bg-card p-4 shadow-lg">
          {error && <p className="text-sm text-destructive flex-1">{error}</p>}
          {successMsg && (
            <p className="text-sm text-green-600 dark:text-green-400 flex items-center gap-1 flex-1">
              <CheckCircle2 className="h-4 w-4" /> {successMsg}
            </p>
          )}
          {!error && !successMsg && <div className="flex-1" />}
          <Button variant="outline" disabled={saving} onClick={() => setDraft(data.runtime)}>
            Discard
          </Button>
          <Button disabled={!isDirty || saving} onClick={handleSave}>
            {saving ? <Loader2 className="h-4 w-4 mr-2 animate-spin" /> : <Save className="h-4 w-4 mr-2" />}
            Save changes
          </Button>
        </div>
      )}
    </div>
  );
}

// ── Helper components ────────────────────────────────────────────────────

function ReadOnlyField({ label, value }: { label: string; value: string }) {
  return (
    <div className="grid gap-1">
      <Label className="text-muted-foreground text-xs flex items-center gap-1">
        <Lock className="h-3 w-3" /> {label}
      </Label>
      <p className="text-sm font-mono bg-muted px-3 py-2 rounded-md">{value || '—'}</p>
      <p className="text-xs text-muted-foreground">Managed via environment configuration</p>
    </div>
  );
}

function InfoRow({
  label,
  value,
  badge,
}: {
  label: string;
  value: string;
  badge?: 'success' | 'destructive' | 'warning';
}) {
  return (
    <div className="flex items-center justify-between py-1">
      <span className="text-sm text-muted-foreground">{label}</span>
      <span className="text-sm font-medium flex items-center gap-2">
        {value}
        {badge === 'success' && (
          <Badge variant="outline" className="text-green-600 border-green-300">
            <CheckCircle2 className="h-3 w-3 mr-1" /> OK
          </Badge>
        )}
        {badge === 'destructive' && (
          <Badge variant="destructive">
            <XCircle className="h-3 w-3 mr-1" /> Error
          </Badge>
        )}
        {badge === 'warning' && (
          <Badge variant="outline" className="text-amber-600 border-amber-300">
            Warning
          </Badge>
        )}
      </span>
    </div>
  );
}
