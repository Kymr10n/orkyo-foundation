import { FeatureKeys } from "@foundation/contracts/plans";
import { useFeatureEnabled } from "@foundation/src/hooks/useFeatureEnabled";
import { Key } from "lucide-react";
import type { CreateReportingTokenRequest } from "@foundation/src/lib/api/reporting-tokens-api";
import {
  useCreateReportingToken,
  useReportingTokens,
  useRevokeReportingToken,
} from "@foundation/src/hooks/useApiTokens";
import { CopyButton, CreateTokenDialog, TokenSettingsPage } from "./api-tokens/token-ui";

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

/** Read-only reporting tokens for BI tools. The shared token screen with this class's copy. */
export function ReportingApiSettings({ upgradeHref }: ReportingApiSettingsProps = {}) {
  // Paid-tier gate: reporting API keys require API access (Professional+).
  const apiAccessAllowed = useFeatureEnabled(FeatureKeys.ApiAccess);
  const tokens = useReportingTokens(apiAccessAllowed);
  const createMutation = useCreateReportingToken();
  const revokeMutation = useRevokeReportingToken();

  return (
    <TokenSettingsPage
      upgradeHref={upgradeHref}
      apiAccessAllowed={apiAccessAllowed}
      tokens={tokens}
      title="Reporting API"
      description="Manage API tokens for connecting BI tools (Power BI, Excel, Metabase) to your organization data."
      upsell={{
        title: "Reporting API",
        description:
          "Available on Professional and Enterprise plans. Connect BI tools to your organization data with read-only API tokens.",
        points: (
          <ul className="list-disc list-inside space-y-1.5 text-sm text-muted-foreground">
            <li>Connect Power BI, Excel, or Metabase to your organization data</li>
            <li>Read-only, scoped API tokens you can revoke anytime</li>
            <li>Incremental refresh via <code className="text-xs bg-muted px-1 rounded">updatedSince</code></li>
          </ul>
        ),
      }}
      unavailableMessage="Reporting API access is not available for this organization."
      loadErrorMessage="Failed to load reporting tokens."
      emptyMessage="No reporting tokens yet. Create one to connect a BI tool."
      tableKey="tokens"
      quickStart={<PowerBiQuickStart />}
      renderCreateDialog={(props) => (
        <CreateTokenDialog<object, CreateReportingTokenRequest>
          {...props}
          title="Create Reporting Token"
          description="This token grants read-only access to reporting data for this organization. It will be shown once — copy it before closing."
          namePlaceholder="e.g. Power BI Dashboard"
          defaultExpiry="7"
          mutation={createMutation}
          initialExtra={{}}
          toRequest={(base) => base}
        />
      )}
      rawTokenWarning="Store this token securely. Anyone with it can read your organization's reporting data."
      revokeMutation={revokeMutation}
    />
  );
}
