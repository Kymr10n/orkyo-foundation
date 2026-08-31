import { useState } from "react";
import { useQuery, useMutation } from "@tanstack/react-query";
import { useAuth } from "@foundation/src/contexts/AuthContext";
import { useReportingApiAvailable } from "@foundation/src/hooks/useReportingApiAvailable";
import { Plus, Bot, TriangleAlert } from "lucide-react";
import { LoadingSpinner } from "@foundation/src/components/ui/LoadingSpinner";
import { FeatureUpsell } from "@foundation/src/components/ui/FeatureUpsell";
import { Alert, AlertDescription } from "@foundation/src/components/ui/alert";
import { Button } from "@foundation/src/components/ui/button";
import { FormDialog } from "@foundation/src/components/ui/FormDialog";
import { Input } from "@foundation/src/components/ui/input";
import { Label } from "@foundation/src/components/ui/label";
import { StatusBadge } from "@foundation/src/components/ui/status-badge";
import { SettingsPageHeader } from "./SettingsPageHeader";
import { OrkyoDataTable, type ColumnDef } from "@foundation/src/components/ui/OrkyoDataTable";
import {
  listApiAccessTokens,
  createApiAccessToken,
  revokeApiAccessToken,
  grantsWrite,
  API_SCOPES,
  type ApiScope,
  type ApiAccessTokenSummary,
} from "@foundation/src/lib/api/api-access-tokens-api";
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

function AccessBadge({ scopes }: { scopes: string }) {
  return grantsWrite(scopes) ? (
    <StatusBadge status="warning" label="Read & write" />
  ) : (
    <StatusBadge status="inactive" label="Read only" />
  );
}

interface CreateTokenDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  onCreated: (rawToken: string) => void;
}

function CreateTokenDialog({ open, onOpenChange, onCreated }: CreateTokenDialogProps) {
  const [name, setName] = useState("");
  const [level, setLevel] = useState<"read" | "write">("read");
  const [expiryMode, setExpiryMode] = useState<ExpiryMode>("90");
  const [customExpiresAt, setCustomExpiresAt] = useState("");
  const expiresAt = resolveExpiry(expiryMode, customExpiresAt);

  // Reset the form each time the dialog opens — render-phase, not an effect.
  const [syncedOpen, setSyncedOpen] = useState(open);
  if (syncedOpen !== open) {
    setSyncedOpen(open);
    if (open) {
      setName("");
      // Defaults to read-only: granting write is a decision someone should make on purpose.
      setLevel("read");
      setExpiryMode("90");
      setCustomExpiresAt("");
    }
  }

  const mutation = useMutation({
    mutationFn: () =>
      createApiAccessToken({
        name,
        scopes: ACCESS_LEVELS.find((l) => l.id === level)!.scopes as ApiScope[],
        ...(expiresAt ? { expiresAt } : {}),
      }),
    meta: {
      errorMessage: "Failed to create token. Please try again.",
      invalidates: [qk.apiAccessTokens.all()],
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
      title="Create API token"
      description="Connects an AI assistant or automated service to this workspace's schedule. It will be shown once — copy it before closing."
      onSubmit={() => mutation.mutate()}
      isSubmitting={mutation.isPending}
      submitLabel="Create token"
      submitDisabled={!(name.trim() && (expiryMode !== "custom" || !!expiresAt))}
    >
      <div className="space-y-1.5">
        <Label htmlFor="api-token-name">Name</Label>
        <Input
          id="api-token-name"
          placeholder="e.g. Planning assistant"
          value={name}
          onChange={(e) => setName(e.target.value)}
          autoFocus
        />
      </div>

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
                onChange={() => setLevel(option.id)}
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

      <ExpiryFields
        mode={expiryMode}
        onModeChange={setExpiryMode}
        customExpiresAt={customExpiresAt}
        onCustomChange={setCustomExpiresAt}
      />
    </FormDialog>
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
        This workspace speaks the Model Context Protocol, so any MCP-compatible client can read and
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

export function PlatformApiSettings({ upgradeHref }: PlatformApiSettingsProps = {}) {
  // Same entitlement as the reporting API: programmatic access is one product capability.
  const { isLoading: authLoading } = useAuth();
  const apiAccessAllowed = useReportingApiAvailable();

  const [createOpen, setCreateOpen] = useState(false);
  const [rawToken, setRawToken] = useState<string | null>(null);
  const [revokeTarget, setRevokeTarget] = useState<ApiAccessTokenSummary | null>(null);

  const { data: tokens = [], isLoading, error } = useQuery({
    queryKey: qk.apiAccessTokens.all(),
    queryFn: listApiAccessTokens,
    enabled: apiAccessAllowed,
  });

  // The access column is what this table has that the reporting one does not: whether a token can
  // change the schedule is the first thing worth seeing in a list of them.
  const accessColumn: ColumnDef<ApiAccessTokenSummary> = {
    id: "access",
    accessorFn: (r) => (grantsWrite(r.scopes) ? "Read & write" : "Read only"),
    header: "Access",
    meta: { filter: { type: "enum" } },
    cell: ({ row }) => <AccessBadge scopes={row.original.scopes} />,
  };

  const columns = buildTokenColumns<ApiAccessTokenSummary>(setRevokeTarget, [accessColumn]);
  const tableUrlState = useTableUrlState("api-tokens", columns);

  if (authLoading) {
    return (
      <div className="py-12">
        <LoadingSpinner fullScreen={false} />
      </div>
    );
  }

  if (!apiAccessAllowed) {
    if (upgradeHref) {
      return (
        <FeatureUpsell
          title="API & AI access"
          description="Available on Professional and Enterprise plans. Let an AI assistant or automated service read and manage your schedule."
          upgradeHref={upgradeHref}
        >
          <ul className="list-disc list-inside space-y-1.5 text-sm text-muted-foreground">
            <li>Connect any MCP-compatible AI assistant to your workspace</li>
            <li>Read-only or read-and-write tokens you can revoke anytime</li>
            <li>Every change goes through the same rules and conflict checks your team does</li>
          </ul>
        </FeatureUpsell>
      );
    }

    return (
      <Alert>
        <AlertDescription>API access is not available for this workspace.</AlertDescription>
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
        <AlertDescription>Failed to load API tokens. Please try again.</AlertDescription>
      </Alert>
    );
  }

  return (
    <div className="space-y-6">
      <SettingsPageHeader
        title="API & AI access"
        description="Manage tokens that let an AI assistant or automated service read and manage this workspace's schedule."
      >
        <Button size="sm" onClick={() => setCreateOpen(true)} className="gap-1.5">
          <Plus className="h-4 w-4" />
          New token
        </Button>
      </SettingsPageHeader>

      {tokens.length === 0 ? (
        <div className="rounded-lg border border-dashed p-8 text-center text-muted-foreground text-sm">
          No API tokens yet. Create one to connect an AI assistant.
        </div>
      ) : (
        <OrkyoDataTable
          {...tableUrlState}
          columns={columns}
          data={tokens}
          renderCard={(token) =>
            renderTokenCard(token, setRevokeTarget, <AccessBadge scopes={token.scopes} />)
          }
        />
      )}

      <McpQuickStart />

      <CreateTokenDialog
        open={createOpen}
        onOpenChange={setCreateOpen}
        onCreated={(t) => setRawToken(t)}
      />
      <RawTokenDialog
        token={rawToken}
        onClose={() => setRawToken(null)}
        warning="Store this token securely. Anyone with it can act on this workspace's schedule — with a read-and-write token, that includes creating, rescheduling and reassigning work."
      />
      <RevokeTokenDialog
        token={revokeTarget}
        onOpenChange={(open) => !open && setRevokeTarget(null)}
        revokeFn={revokeApiAccessToken}
        invalidates={qk.apiAccessTokens.all()}
      />
    </div>
  );
}
