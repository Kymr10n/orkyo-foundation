import { FeatureKeys } from "@foundation/contracts/plans";
import { useFeatureEnabled } from "@foundation/src/hooks/useFeatureEnabled";
import { Bot, TriangleAlert } from "lucide-react";
import { Alert, AlertDescription } from "@foundation/src/components/ui/alert";
import { StatusBadge } from "@foundation/src/components/ui/status-badge";
import type { ColumnDef } from "@foundation/src/components/ui/OrkyoDataTable";
import {
  grantsWrite,
  API_SCOPES,
  type ApiScope,
  type ApiAccessTokenSummary,
  type CreateApiAccessTokenRequest,
} from "@foundation/src/lib/api/api-access-tokens-api";
import {
  useApiAccessTokens,
  useCreateApiAccessToken,
  useRevokeApiAccessToken,
} from "@foundation/src/hooks/useApiTokens";
import { CopyButton, CreateTokenDialog, TokenSettingsPage } from "./api-tokens/token-ui";

/** What each access level means, in the terms the person granting it thinks in. */
const ACCESS_LEVELS = [
  {
    id: "read" as const,
    scopes: [API_SCOPES.scheduleRead],
    label: "Read only",
    detail: "Can see requests, resources and conflicts. Cannot change anything.",
  },
  {
    id: "write" as const,
    scopes: [API_SCOPES.scheduleRead, API_SCOPES.scheduleWrite],
    label: "Read and write",
    detail: "Can also reschedule work, book resources, auto-schedule a site, create requests "
      + "and block resource time — as an Editor can.",
  },
];

type AccessLevel = (typeof ACCESS_LEVELS)[number]["id"];

/** The access choice, and the warning once write is chosen. */
function AccessFields({ level, onChange }: { level: AccessLevel; onChange: (level: AccessLevel) => void }) {
  return (
    <>
      <fieldset className="space-y-1.5">
        <legend className="text-sm font-medium leading-none">Access</legend>
        <div className="flex flex-col gap-2 pt-1.5">
          {ACCESS_LEVELS.map((option) => (
            <label
              key={option.id}
              className="flex cursor-pointer items-start gap-3 rounded-md border p-3 hover:bg-muted/50"
            >
              <input
                type="radio"
                name="api-token-access"
                className="mt-1"
                checked={level === option.id}
                onChange={() => onChange(option.id)}
              />
              <span className="min-w-0">
                <span className="block text-sm font-medium">{option.label}</span>
                <span className="block text-sm text-muted-foreground">{option.detail}</span>
              </span>
            </label>
          ))}
        </div>
      </fieldset>

      {level === "write" && (
        <Alert>
          <TriangleAlert className="h-4 w-4" />
          <AlertDescription>
            Anyone holding this token can create work, reschedule and reassign it, auto-schedule a
            whole site, and mark resources unavailable. Give it only to a service you control, and
            revoke it when you are done.
          </AlertDescription>
        </Alert>
      )}
    </>
  );
}

function AccessBadge({ scopes }: { scopes: string }) {
  return grantsWrite(scopes) ? (
    <StatusBadge status="warning" label="Read & write" />
  ) : (
    <StatusBadge status="inactive" label="Read only" />
  );
}

function McpQuickStart() {
  const url = `${window.location.origin}/api/mcp`;
  return (
    <div className="rounded-lg border bg-card p-4 space-y-3">
      <div className="flex items-center gap-2 text-sm font-medium">
        <Bot className="h-4 w-4 text-muted-foreground" />
        Connect an AI assistant
      </div>
      <p className="text-sm text-muted-foreground">
        This organization speaks the Model Context Protocol, so any MCP-compatible client can read and
        manage its schedule. Point the client at this server URL and authenticate with a token above.
      </p>
      <div className="bg-muted rounded-md p-2 font-mono text-xs break-all flex items-center justify-between gap-2">
        <span>{url}</span>
        <CopyButton text={url} />
      </div>
      <ol className="text-sm text-muted-foreground space-y-1.5 list-decimal list-inside">
        <li>Create a token above and copy it.</li>
        <li>Add the server URL to your MCP client.</li>
        <li>
          Set the header <code className="text-xs bg-muted px-1 rounded">Authorization</code> →{" "}
          <code className="text-xs bg-muted px-1 rounded">Bearer &lt;your-token&gt;</code>
        </li>
        <li>
          With a read-only token the assistant can list sites, requests, resources and conflicts,
          see the critical path and dependencies, analyse capacity and bottlenecks, and compute an
          auto-schedule proposal without applying it.
        </li>
        <li>
          A read-and-write token can also apply that proposal, reschedule work, book resources,
          create requests, link them, and block resource time.
        </li>
      </ol>
    </div>
  );
}

interface PlatformApiSettingsProps {
  /** When set, the locked state shows a CTA linking here (e.g. the plans page). */
  upgradeHref?: string;
}

/** Write-capable API tokens for AI assistants and services. The shared token screen, plus access. */
export function PlatformApiSettings({ upgradeHref }: PlatformApiSettingsProps = {}) {
  // Same entitlement as the reporting API: programmatic access is one product capability.
  const apiAccessAllowed = useFeatureEnabled(FeatureKeys.ApiAccess);
  const tokens = useApiAccessTokens(apiAccessAllowed);
  const createMutation = useCreateApiAccessToken();
  const revokeMutation = useRevokeApiAccessToken();

  // The access column is what this table has that the reporting one does not: whether a token can
  // change the schedule is the first thing worth seeing in a list of them.
  const accessColumn: ColumnDef<ApiAccessTokenSummary> = {
    id: "access",
    accessorFn: (r) => (grantsWrite(r.scopes) ? "Read & write" : "Read only"),
    header: "Access",
    meta: { filter: { type: "enum" } },
    cell: ({ row }) => <AccessBadge scopes={row.original.scopes} />,
  };

  return (
    <TokenSettingsPage
      upgradeHref={upgradeHref}
      apiAccessAllowed={apiAccessAllowed}
      tokens={tokens}
      title="API & AI access"
      description="Manage tokens that let an AI assistant or automated service read and manage this organization's schedule."
      upsell={{
        title: "API & AI access",
        description:
          "Available on Professional and Enterprise plans. Let an AI assistant or automated service read and manage your schedule.",
        points: (
          <ul className="list-disc list-inside space-y-1.5 text-sm text-muted-foreground">
            <li>Connect any MCP-compatible AI assistant to your organization</li>
            <li>Read-only or read-and-write tokens you can revoke anytime</li>
            <li>Every change goes through the same rules and conflict checks your team does</li>
          </ul>
        ),
      }}
      unavailableMessage="API access is not available for this organization."
      loadErrorMessage="Failed to load API tokens."
      emptyMessage="No API tokens yet. Create one to connect an AI assistant."
      tableKey="api-tokens"
      extraColumns={[accessColumn]}
      cardSubtitle={(token) => <AccessBadge scopes={token.scopes} />}
      quickStart={<McpQuickStart />}
      renderCreateDialog={(props) => (
        <CreateTokenDialog<{ level: AccessLevel }, CreateApiAccessTokenRequest>
          {...props}
          title="Create API token"
          description="Connects an AI assistant or automated service to this organization's schedule. It will be shown once — copy it before closing."
          namePlaceholder="e.g. Planning assistant"
          defaultExpiry="90"
          mutation={createMutation}
          // Defaults to read-only: granting write is a decision someone should make on purpose.
          initialExtra={{ level: "read" }}
          renderExtra={(form, set) => <AccessFields level={form.level} onChange={(level) => set({ level })} />}
          toRequest={(base, form) => ({
            ...base,
            scopes: ACCESS_LEVELS.find((l) => l.id === form.level)!.scopes as ApiScope[],
          })}
        />
      )}
      rawTokenWarning="Store this token securely. Anyone with it can act on this organization's schedule — with a read-and-write token, that includes creating, rescheduling and reassigning work."
      revokeMutation={revokeMutation}
    />
  );
}
