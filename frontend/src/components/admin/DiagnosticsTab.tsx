import { useCallback, useEffect, useState } from 'react';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@foundation/src/components/ui/card';
import { Badge } from '@foundation/src/components/ui/badge';
import { Separator } from '@foundation/src/components/ui/separator';
import { Button } from '@foundation/src/components/ui/button';
import {
  type DiagnosticsResponse,
  getAdminDiagnostics,
} from '@foundation/src/lib/api/admin-api';
import { logger } from '@foundation/src/lib/core/logger';
import {
  Activity,
  CheckCircle2,
  Database,
  Globe,
  Loader2,
  Mail,
  RefreshCw,
  Server,
  Shield,
  XCircle,
} from 'lucide-react';

function StatusBadgeIcon({ status }: { status: string }) {
  const isGood = ['healthy', 'connected', 'configured', 'running'].includes(status);
  const isBad = ['unreachable', 'not-configured'].includes(status);

  if (isGood) return <Badge variant="default" className="bg-green-600"><CheckCircle2 className="mr-1 h-3 w-3" />{status}</Badge>;
  if (isBad) return <Badge variant="destructive"><XCircle className="mr-1 h-3 w-3" />{status}</Badge>;
  return <Badge variant="secondary">{status}</Badge>;
}

function InfoRow({ label, value }: { label: string; value: React.ReactNode }) {
  return (
    <div className="flex items-center justify-between py-2">
      <span className="text-sm text-muted-foreground">{label}</span>
      <span className="text-sm font-medium">{value}</span>
    </div>
  );
}

export function DiagnosticsTab() {
  const [data, setData] = useState<DiagnosticsResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [lastRefreshed, setLastRefreshed] = useState<Date | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError('');
    try {
      const result = await getAdminDiagnostics();
      setData(result);
      setLastRefreshed(new Date());
    } catch (err) {
      logger.error('Failed to load diagnostics', err);
      setError('Failed to load diagnostics');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { void load(); }, [load]);

  if (loading && !data) {
    return (
      <div className="flex items-center justify-center py-12">
        <Loader2 className="h-6 w-6 animate-spin text-muted-foreground" />
      </div>
    );
  }

  if (error && !data) {
    return (
      <div className="rounded-md bg-destructive/10 p-4 text-destructive text-sm">
        {error}
      </div>
    );
  }

  if (!data) return null;

  return (
    <div className="space-y-6">
      {/* Header with refresh */}
      <div className="flex items-center justify-between">
        <div>
          <h3 className="text-lg font-medium">Platform Diagnostics</h3>
          {lastRefreshed && (
            <p className="text-xs text-muted-foreground">
              Last refreshed: {lastRefreshed.toLocaleTimeString()}
            </p>
          )}
        </div>
        <Button variant="outline" size="sm" onClick={load} disabled={loading}>
          <RefreshCw className={`mr-2 h-4 w-4 ${loading ? 'animate-spin' : ''}`} />
          Refresh
        </Button>
      </div>

      {/* Version & Build */}
      <Card>
        <CardHeader className="pb-3">
          <CardTitle className="flex items-center gap-2 text-base">
            <Server className="h-4 w-4" />
            Application
          </CardTitle>
        </CardHeader>
        <CardContent>
          <InfoRow label="Version" value={data.version} />
          <Separator />
          <InfoRow label="Build" value={<code className="text-xs">{data.build}</code>} />
          <Separator />
          <InfoRow label="Deployment Mode" value={data.deploymentMode} />
        </CardContent>
      </Card>

      {/* Database */}
      <Card>
        <CardHeader className="pb-3">
          <CardTitle className="flex items-center gap-2 text-base">
            <Database className="h-4 w-4" />
            Database
          </CardTitle>
          <CardDescription>PostgreSQL control plane status</CardDescription>
        </CardHeader>
        <CardContent>
          <InfoRow label="Status" value={<StatusBadgeIcon status={data.database.status} />} />
          <Separator />
          <InfoRow label="Migrations Applied" value={data.database.migrationsApplied} />
          <Separator />
          <InfoRow label="Active Tenants" value={data.database.tenantCount} />
        </CardContent>
      </Card>

      {/* Auth */}
      <Card>
        <CardHeader className="pb-3">
          <CardTitle className="flex items-center gap-2 text-base">
            <Shield className="h-4 w-4" />
            Authentication
          </CardTitle>
          <CardDescription>Identity provider connectivity</CardDescription>
        </CardHeader>
        <CardContent>
          <InfoRow label="Status" value={<StatusBadgeIcon status={data.auth.status} />} />
          <Separator />
          <InfoRow label="Provider" value={data.auth.provider} />
          <Separator />
          <InfoRow label="Realm" value={data.auth.realm} />
        </CardContent>
      </Card>

      {/* SMTP */}
      <Card>
        <CardHeader className="pb-3">
          <CardTitle className="flex items-center gap-2 text-base">
            <Mail className="h-4 w-4" />
            Email (SMTP)
          </CardTitle>
        </CardHeader>
        <CardContent>
          <InfoRow label="Status" value={<StatusBadgeIcon status={data.smtp.status} />} />
          {data.smtp.host && (
            <>
              <Separator />
              <InfoRow label="Host" value={data.smtp.host} />
            </>
          )}
        </CardContent>
      </Card>

      {/* Worker */}
      <Card>
        <CardHeader className="pb-3">
          <CardTitle className="flex items-center gap-2 text-base">
            <Activity className="h-4 w-4" />
            Background Worker
          </CardTitle>
          <CardDescription>Lifecycle jobs and background processing</CardDescription>
        </CardHeader>
        <CardContent>
          <InfoRow label="Status" value={<StatusBadgeIcon status={data.worker.status} />} />
          {data.worker.lastActivity && (
            <>
              <Separator />
              <InfoRow
                label="Last Activity"
                value={new Date(data.worker.lastActivity).toLocaleString()}
              />
            </>
          )}
        </CardContent>
      </Card>

      {/* Modules */}
      <Card>
        <CardHeader className="pb-3">
          <CardTitle className="flex items-center gap-2 text-base">
            <Globe className="h-4 w-4" />
            Modules
          </CardTitle>
          <CardDescription>Optional platform capabilities</CardDescription>
        </CardHeader>
        <CardContent>
          <InfoRow
            label="Observability (OTEL)"
            value={
              data.modules.observability
                ? <Badge variant="default" className="bg-green-600">Enabled</Badge>
                : <Badge variant="secondary">Disabled</Badge>
            }
          />
          <Separator />
          <InfoRow
            label="Log Aggregation (Loki)"
            value={
              data.modules.logAggregation
                ? <Badge variant="default" className="bg-green-600">Enabled</Badge>
                : <Badge variant="secondary">Disabled</Badge>
            }
          />
        </CardContent>
      </Card>
    </div>
  );
}
