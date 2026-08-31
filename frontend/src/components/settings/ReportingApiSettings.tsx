import { useState } from "react";
import { useQuery, useMutation } from "@tanstack/react-query";
import { useAuth } from "@foundation/src/contexts/AuthContext";
import { useReportingApiAvailable } from "@foundation/src/hooks/useReportingApiAvailable";
import { Plus, Key } from "lucide-react";
import { LoadingSpinner } from "@foundation/src/components/ui/LoadingSpinner";
import { FeatureUpsell } from "@foundation/src/components/ui/FeatureUpsell";
import { Alert, AlertDescription } from "@foundation/src/components/ui/alert";
import { Button } from "@foundation/src/components/ui/button";
import { FormDialog } from "@foundation/src/components/ui/FormDialog";
import { Input } from "@foundation/src/components/ui/input";
import { Label } from "@foundation/src/components/ui/label";
import { SettingsPageHeader } from "./SettingsPageHeader";
import { OrkyoDataTable } from "@foundation/src/components/ui/OrkyoDataTable";
import {
  listReportingTokens,
  createReportingToken,
  revokeReportingToken,
  type ReportingTokenSummary,
} from "@foundation/src/lib/api/reporting-tokens-api";
import { qk } from "@foundation/src/lib/api/query-keys";
import { useTableUrlState } from "@foundation/src/hooks/useTableUrlState";
import {
  CopyButton,
  ExpiryFields,
  RawTokenDialog,
  RevokeTokenDialog,
  buildTokenColumns,
  renderTokenCard,
  resolveExpiry,
  type ExpiryMode,
} from "./api-tokens/token-ui";

interface CreateTokenDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  onCreated: (rawToken: string) => void;
}

function CreateTokenDialog({ open, onOpenChange, onCreated }: CreateTokenDialogProps) {
  const [name, setName] = useState("");
  const [expiryMode, setExpiryMode] = useState<ExpiryMode>("7");
  const [customExpiresAt, setCustomExpiresAt] = useState("");
  const expiresAt = resolveExpiry(expiryMode, customExpiresAt);

  // Reset the form each time the dialog opens — render-phase, not an effect (see useEntityFormDialog.ts).
  const [syncedOpen, setSyncedOpen] = useState(open);
  if (syncedOpen !== open) {
    setSyncedOpen(open);
    if (open) {
      setName("");
      setExpiryMode("7");
      setCustomExpiresAt("");
    }
  }

  const mutation = useMutation({
    mutationFn: () => createReportingToken({ name, ...(expiresAt ? { expiresAt } : {}) }),
    meta: {
      errorMessage: "Failed to create token. Please try again.",
      invalidates: [qk.reportingTokens.all()],
    },
    onSuccess: (result) => {
      onOpenChange(false);
      onCreated(result.rawToken);
    },
  });

  return (
    <FormDialog
      open={open}
      onOpenChange={onOpenChange}
      title="Create Reporting Token"
      description="This token grants read-only access to reporting data for this workspace. It will be shown once — copy it before closing."
      onSubmit={() => mutation.mutate()}
      isSubmitting={mutation.isPending}
      submitLabel="Create token"
      submitDisabled={!(name.trim() && (expiryMode !== "custom" || !!expiresAt))}
    >
      <div className="space-y-1.5">
        <Label htmlFor="token-name">Name</Label>
        <Input
          id="token-name"
          placeholder="e.g. Power BI Dashboard"
          value={name}
          onChange={(e) => setName(e.target.value)}
          autoFocus
        />
      </div>
      <ExpiryFields
        mode={expiryMode}
        onModeChange={setExpiryMode}
        customExpiresAt={customExpiresAt}
        onCustomChange={setCustomExpiresAt}
      />
    </FormDialog>
  );
}

function PowerBiQuickStart() {
  const url = `${window.location.origin}/api/reporting/v1/`;
  return (
    <div className="rounded-lg border bg-card p-4 space-y-3">
      <div className="flex items-center gap-2 text-sm font-medium">
        <Key className="h-4 w-4 text-muted-foreground" />
        Power BI quick-start
      </div>
      <ol className="text-sm text-muted-foreground space-y-1.5 list-decimal list-inside">
        <li>Create a token above and copy it.</li>
        <li>In Power BI Desktop: Get Data → Web → Advanced.</li>
        <li>Set URL to an endpoint, e.g.:</li>
      </ol>
      <div className="bg-muted rounded-md p-2 font-mono text-xs break-all flex items-center justify-between gap-2">
        <span>{url}allocations</span>
        <CopyButton text={`${url}allocations`} />
      </div>
      <ol className="text-sm text-muted-foreground space-y-1.5 list-decimal list-inside" start={4}>
        <li>Add HTTP header: <code className="text-xs bg-muted px-1 rounded">Authorization</code> → <code className="text-xs bg-muted px-1 rounded">Bearer &lt;your-token&gt;</code></li>
        <li>Load and build your report. Use <code className="text-xs bg-muted px-1 rounded">updatedSince</code> for incremental refresh.</li>
      </ol>
    </div>
  );
}

interface ReportingApiSettingsProps {
  /** When set, the locked state shows a CTA linking here (e.g. the plans page). */
  upgradeHref?: string;
}

export function ReportingApiSettings({ upgradeHref }: ReportingApiSettingsProps = {}) {
  // Paid-tier gate: reporting API keys require API access (Professional+).
  const { isLoading: authLoading } = useAuth();
  const apiAccessAllowed = useReportingApiAvailable();

  const [createOpen, setCreateOpen] = useState(false);
  const [rawToken, setRawToken] = useState<string | null>(null);
  const [revokeTarget, setRevokeTarget] = useState<ReportingTokenSummary | null>(null);

  const { data: tokens = [], isLoading, error } = useQuery({
    queryKey: qk.reportingTokens.all(),
    queryFn: listReportingTokens,
    enabled: apiAccessAllowed,
  });

  const columns = buildTokenColumns<ReportingTokenSummary>(setRevokeTarget);

  // Header sort/filter state lives in the URL: bookmarkable, shareable, Back-safe.
  const tableUrlState = useTableUrlState("tokens", columns);

  if (authLoading) {
    return (
      <div className="py-12">
        <LoadingSpinner fullScreen={false} />
      </div>
    );
  }

  if (!apiAccessAllowed) {
    // Paid-tier gate. Rather than silently redirecting, keep the user on the page and
    // explain the feature + upgrade path. When no upgrade target is provided (e.g.
    // Community, which has no plans), fall back to a plain unavailable notice.
    if (upgradeHref) {
      return (
        <FeatureUpsell
          title="Reporting API"
          description="Available on Professional and Enterprise plans. Connect BI tools to your workspace data with read-only API tokens."
          upgradeHref={upgradeHref}
        >
          <ul className="list-disc list-inside space-y-1.5 text-sm text-muted-foreground">
            <li>Connect Power BI, Excel, or Metabase to your workspace data</li>
            <li>Read-only, scoped API tokens you can revoke anytime</li>
            <li>Incremental refresh via <code className="text-xs bg-muted px-1 rounded">updatedSince</code></li>
          </ul>
        </FeatureUpsell>
      );
    }

    return (
      <Alert>
        <AlertDescription>
          Reporting API access is not available for this workspace.
        </AlertDescription>
      </Alert>
    );
  }

  if (isLoading) {
    return (
      <div className="py-12">
        <LoadingSpinner fullScreen={false} />
      </div>
    );
  }

  if (error) {
    return (
      <Alert variant="destructive">
        <AlertDescription>Failed to load reporting tokens. Please try again.</AlertDescription>
      </Alert>
    );
  }

  return (
    <div className="space-y-6">
      <SettingsPageHeader
        title="Reporting API"
        description="Manage API tokens for connecting BI tools (Power BI, Excel, Metabase) to your workspace data."
      >
        <Button size="sm" onClick={() => setCreateOpen(true)} className="gap-1.5">
          <Plus className="h-4 w-4" />
          New token
        </Button>
      </SettingsPageHeader>

      {tokens.length === 0 ? (
        <div className="rounded-lg border border-dashed p-8 text-center text-muted-foreground text-sm">
          No reporting tokens yet. Create one to connect a BI tool.
        </div>
      ) : (
        <OrkyoDataTable
          {...tableUrlState}
          columns={columns}
          data={tokens}
          renderCard={(token) => renderTokenCard(token, setRevokeTarget)}
        />
      )}

      <PowerBiQuickStart />

      <CreateTokenDialog
        open={createOpen}
        onOpenChange={setCreateOpen}
        onCreated={(t) => setRawToken(t)}
      />
      <RawTokenDialog
        token={rawToken}
        onClose={() => setRawToken(null)}
        warning="Store this token securely. Anyone with it can read your workspace's reporting data."
      />
      <RevokeTokenDialog
        token={revokeTarget}
        onOpenChange={(open) => !open && setRevokeTarget(null)}
        revokeFn={revokeReportingToken}
        invalidates={qk.reportingTokens.all()}
      />
    </div>
  );
}
