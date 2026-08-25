import { useState } from "react";
import { Bot, Check, Gauge, KeyRound, Loader2, Trash2, X } from "lucide-react";
import { toast } from "sonner";
import { Alert, AlertDescription } from "@foundation/src/components/ui/alert";
import { Button } from "@foundation/src/components/ui/button";
import { FeatureUpsell } from "@foundation/src/components/ui/FeatureUpsell";
import { Input } from "@foundation/src/components/ui/input";
import { Label } from "@foundation/src/components/ui/label";
import { LoadingSpinner } from "@foundation/src/components/ui/LoadingSpinner";
import { SettingsPageHeader } from "@foundation/src/components/settings/SettingsPageHeader";
import { ConfirmDialog } from "@foundation/src/components/ui/ConfirmDialog";
import { useAiAssistantAvailable } from "@foundation/src/hooks/useAiAssistantAvailable";
import {
  useAiAllowances,
  useAiCredential,
  useAiDailyLimits,
  useDeleteAiCredential,
  useRevokeAiAllowance,
  useSaveAiAllowance,
  useSaveAiCredential,
  useSaveAiDailyLimits,
  useTestAiCredential,
} from "@foundation/src/hooks/useAiAssistant";
import type { AiDailyLimits, AiUserAllowance } from "@foundation/src/lib/api/ai-api";

export interface AiAssistantSettingsProps {
  /** Where to send someone whose plan does not include the assistant. Omitted in Community. */
  upgradeHref?: string;
}

/**
 * Administration for the AI assistant: the workspace's own provider key, and who may
 * spend it.
 *
 * Two things are deliberate here. The key is write-only — once saved, this page shows a
 * four-character hint and nothing more, because the only reader is the server. And
 * access is deny-by-default: members appear in the table with no access until an admin
 * grants them a monthly budget.
 */
export function AiAssistantSettings({ upgradeHref }: AiAssistantSettingsProps = {}) {
  const entitled = useAiAssistantAvailable();

  const { data: credential, isLoading: credentialLoading } = useAiCredential(entitled);
  const { data: allowances, isLoading: allowancesLoading } = useAiAllowances(entitled);
  const { data: limits, isLoading: limitsLoading, isError: limitsError } = useAiDailyLimits(entitled);

  const saveCredential = useSaveAiCredential();
  const deleteCredential = useDeleteAiCredential();
  const testCredential = useTestAiCredential();

  const [apiKey, setApiKey] = useState("");
  const [removeOpen, setRemoveOpen] = useState(false);

  if (!entitled) {
    if (upgradeHref) {
      return (
        <FeatureUpsell
          title="AI assistant"
          description="Available on Professional and Enterprise plans. Ask questions about your schedule and get guided help resolving conflicts."
          upgradeHref={upgradeHref}
        >
          <ul className="list-disc list-inside space-y-1.5 text-sm text-muted-foreground">
            <li>Ask about requests, resources and conflicts in plain language</li>
            <li>Guided conflict resolution, with every change confirmed by you</li>
            <li>Uses your own Anthropic API key, with a token budget per person</li>
          </ul>
        </FeatureUpsell>
      );
    }

    return (
      <Alert>
        <AlertDescription>
          The AI assistant is not available for this workspace.
        </AlertDescription>
      </Alert>
    );
  }

  if (credentialLoading) {
    return (
      <div className="py-12">
        <LoadingSpinner fullScreen={false} />
      </div>
    );
  }

  const handleSave = async () => {
    const trimmed = apiKey.trim();
    if (!trimmed) return;
    try {
      await saveCredential.mutateAsync(trimmed);
      setApiKey("");
      toast.success("AI key saved.");
    } catch {
      toast.error("That key was not accepted. Check that it is an Anthropic API key.");
    }
  };

  const handleTest = async () => {
    const result = await testCredential.mutateAsync();
    if (result.ok) {
      toast.success("The key works.");
      return;
    }
    toast.error(
      result.reason === "invalid_key"
        ? "The provider rejected this key."
        : result.reason === "model_unavailable"
          ? "The key works, but this account cannot use the configured model."
          : "Could not reach the provider. Check the network and try again."
    );
  };

  const handleRemove = async () => {
    await deleteCredential.mutateAsync();
    setRemoveOpen(false);
    toast.success("AI key removed. The assistant is switched off for this workspace.");
  };

  return (
    <div className="space-y-8">
      <SettingsPageHeader
        title="AI assistant"
        description="Connect your own Anthropic API key, then choose who may use the assistant and how much they may spend."
      />

      <section className="space-y-4">
        <div className="flex items-center gap-2 text-sm font-medium">
          <KeyRound className="h-4 w-4" />
          API key
        </div>

        {credential?.configured ? (
          <div className="rounded-lg border p-4 space-y-3">
            <div className="flex flex-wrap items-center justify-between gap-3">
              <div className="space-y-1">
                <p className="text-sm">
                  A key ending <code className="bg-muted px-1 rounded">{credential.keyHint}</code> is
                  configured.
                </p>
                <p className="text-xs text-muted-foreground">
                  {credential.lastVerifiedAt
                    ? `Last checked ${new Date(credential.lastVerifiedAt).toLocaleString()}.`
                    : "Not checked yet."}
                </p>
              </div>
              <div className="flex gap-2">
                <Button
                  size="sm"
                  variant="outline"
                  onClick={handleTest}
                  disabled={testCredential.isPending}
                >
                  {testCredential.isPending ? (
                    <Loader2 className="h-4 w-4 animate-spin" />
                  ) : (
                    <Check className="h-4 w-4" />
                  )}
                  Test connection
                </Button>
                <Button size="sm" variant="outline" onClick={() => setRemoveOpen(true)}>
                  <Trash2 className="h-4 w-4" />
                  Remove
                </Button>
              </div>
            </div>
            <p className="text-xs text-muted-foreground">
              The key itself is never shown again. Save a new key to replace it.
            </p>
          </div>
        ) : (
          <Alert>
            <AlertDescription>
              No key is configured, so the assistant is switched off for everyone in this
              workspace.
            </AlertDescription>
          </Alert>
        )}

        <div className="space-y-2 max-w-xl">
          <Label htmlFor="ai-api-key">
            {credential?.configured ? "Replace the key" : "Anthropic API key"}
          </Label>
          <div className="flex gap-2">
            <Input
              id="ai-api-key"
              type="password"
              autoComplete="off"
              placeholder="sk-ant-..."
              value={apiKey}
              onChange={(e) => setApiKey(e.target.value)}
            />
            <Button onClick={handleSave} disabled={!apiKey.trim() || saveCredential.isPending}>
              {saveCredential.isPending && <Loader2 className="h-4 w-4 animate-spin" />}
              Save
            </Button>
          </div>
          <p className="text-xs text-muted-foreground">
            Chat messages and the workspace data the assistant reads are sent to Anthropic
            under your own agreement with them. Usage is billed to this key.
          </p>
        </div>
      </section>

      <section className="space-y-4">
        <div className="flex items-center gap-2 text-sm font-medium">
          <Bot className="h-4 w-4" />
          Who can use it
        </div>
        <p className="text-sm text-muted-foreground">
          Administrators can always use the assistant. Everyone else needs an allowance.
          Leave the limit empty for no limit, or set it to 0 to stop someone without
          removing their access.
        </p>

        {allowancesLoading ? (
          <div className="py-8">
            <LoadingSpinner fullScreen={false} />
          </div>
        ) : (
          <AllowanceTable rows={allowances ?? []} />
        )}
      </section>

      <section className="space-y-4">
        <div className="flex items-center gap-2 text-sm font-medium">
          <Gauge className="h-4 w-4" />
          Daily limits
        </div>
        <p className="text-sm text-muted-foreground">
          A ceiling on interactions rather than tokens, useful when many people share one
          login. Both counts reset at the start of each day, in UTC. Leave a field empty
          for no limit — most workspaces need neither.
        </p>

        {limitsLoading ? (
          <div className="py-8">
            <LoadingSpinner fullScreen={false} />
          </div>
        ) : limitsError || !limits ? (
          // Never fall back to empty fields here. Empty means "no limit", so a form that
          // rendered blank after a failed read would look exactly like a workspace that
          // has none — and one Save would then clear the limits that are really set.
          <Alert>
            <AlertDescription>
              The daily limits could not be loaded. Reload the page to try again.
            </AlertDescription>
          </Alert>
        ) : (
          <DailyLimitsForm limits={limits} />
        )}
      </section>

      <ConfirmDialog
        open={removeOpen}
        onOpenChange={setRemoveOpen}
        title="Remove the AI key?"
        description="The assistant stops working for everyone in this workspace until a new key is saved."
        confirmLabel="Remove key"
        destructive
        onConfirm={handleRemove}
      />
    </div>
  );
}

/**
 * The workspace's two daily interaction ceilings.
 *
 * An empty field means no limit, which is what every workspace starts with — the same
 * convention the token allowance above uses, so the two read alike. The fields are only
 * submitted together: they are one row on the server, and saving one while showing a
 * stale value for the other would silently overwrite it.
 */
/** Matches SaveAiDailyLimitsRequestValidator, so the two cannot drift apart silently. */
const MAX_DAILY_TURNS = 10_000;

function DailyLimitsForm({ limits }: { limits: AiDailyLimits }) {
  const save = useSaveAiDailyLimits();
  const [perUser, setPerUser] = useState(limits.userDailyTurns?.toString() ?? "");
  const [perWorkspace, setPerWorkspace] = useState(limits.tenantDailyTurns?.toString() ?? "");

  /**
   * An empty field means no limit. Anything else has to be a whole number in range —
   * returning null for garbage would read as "clear the limit", which is the one outcome
   * nobody types by accident. Bounds match the server's validator so a mistake is caught
   * here with a useful message rather than coming back as a generic 400.
   */
  const parse = (value: string): { ok: true; value: number | null } | { ok: false } => {
    const trimmed = value.trim();
    if (trimmed === "") return { ok: true, value: null };
    const n = Number(trimmed);
    if (!Number.isInteger(n) || n < 1 || n > MAX_DAILY_TURNS) return { ok: false };
    return { ok: true, value: n };
  };

  const handleSave = async () => {
    const user = parse(perUser);
    const workspace = parse(perWorkspace);
    if (!user.ok || !workspace.ok) {
      toast.error(`A daily limit must be a whole number from 1 to ${MAX_DAILY_TURNS}. Leave it empty for no limit.`);
      return;
    }

    try {
      // Both fields go together: they are one row on the server, so sending one while
      // showing a stale value for the other would overwrite it silently.
      await save.mutateAsync({ userDailyTurns: user.value, tenantDailyTurns: workspace.value });
      toast.success("Daily limits saved");
    } catch {
      toast.error("Could not save the daily limits");
    }
  };

  return (
    <div className="rounded-lg border p-4 space-y-4">
      <div className="grid gap-4 sm:grid-cols-2">
        <div className="space-y-1.5">
          <Label htmlFor="ai-per-user">Interactions per person each day</Label>
          <Input
            id="ai-per-user"
            type="number"
            min={1}
            max={MAX_DAILY_TURNS}
            inputMode="numeric"
            placeholder="No limit"
            value={perUser}
            onChange={(e) => setPerUser(e.target.value)}
          />
          <p className="text-xs text-muted-foreground">
            Counted for each person separately. On a shared login, each visitor gets their
            own count.
          </p>
        </div>

        <div className="space-y-1.5">
          <Label htmlFor="ai-per-workspace">Interactions for the whole workspace each day</Label>
          <Input
            id="ai-per-workspace"
            type="number"
            min={1}
            max={MAX_DAILY_TURNS}
            inputMode="numeric"
            placeholder="No limit"
            value={perWorkspace}
            onChange={(e) => setPerWorkspace(e.target.value)}
          />
          <p className="text-xs text-muted-foreground">
            Everyone's interactions together. Reached first, it stops the assistant for
            everyone until tomorrow.
          </p>
        </div>
      </div>

      <Button onClick={handleSave} disabled={save.isPending}>
        {save.isPending && <Loader2 className="h-4 w-4 animate-spin" />}
        Save limits
      </Button>
    </div>
  );
}

/** One row per workspace member, with an editable monthly token limit. */
function AllowanceTable({ rows }: { rows: AiUserAllowance[] }) {
  if (rows.length === 0) {
    return (
      <div className="rounded-lg border border-dashed p-8 text-center text-muted-foreground text-sm">
        No members yet.
      </div>
    );
  }

  return (
    <div className="rounded-lg border divide-y">
      {rows.map((row) => (
        <AllowanceRow key={row.userId} row={row} />
      ))}
    </div>
  );
}

function AllowanceRow({ row }: { row: AiUserAllowance }) {
  const save = useSaveAiAllowance();
  const revoke = useRevokeAiAllowance();
  const [draft, setDraft] = useState(
    row.monthlyTokenLimit === null ? "" : String(row.monthlyTokenLimit)
  );

  const handleGrant = async () => {
    const trimmed = draft.trim();
    const limit = trimmed === "" ? null : Number(trimmed);
    if (limit !== null && (!Number.isFinite(limit) || limit < 0)) {
      toast.error("A token limit must be zero or more.");
      return;
    }
    await save.mutateAsync({ userId: row.userId, monthlyTokenLimit: limit });
    toast.success("Allowance updated.");
  };

  const handleRevoke = async () => {
    await revoke.mutateAsync(row.userId);
    setDraft("");
    toast.success("Access removed.");
  };

  return (
    <div className="flex flex-wrap items-center gap-3 p-3">
      <div className="min-w-0 flex-1">
        <p className="text-sm truncate">{row.displayName || row.email || row.userId}</p>
        <p className="text-xs text-muted-foreground">
          {row.granted
            ? `${row.usedTotalTokens.toLocaleString()} tokens this month over ${row.usedTurns} ${row.usedTurns === 1 ? "conversation" : "conversations"}`
            : "No access"}
        </p>
      </div>

      <div className="flex items-center gap-2">
        <Input
          className="w-36"
          inputMode="numeric"
          placeholder="No limit"
          aria-label={`Monthly token limit for ${row.displayName || row.email || "this member"}`}
          value={draft}
          onChange={(e) => setDraft(e.target.value)}
        />
        <Button size="sm" variant="outline" onClick={handleGrant} disabled={save.isPending}>
          {row.granted ? "Update" : "Grant"}
        </Button>
        {row.granted && (
          <Button
            size="sm"
            variant="ghost"
            onClick={handleRevoke}
            disabled={revoke.isPending}
            aria-label={`Remove assistant access for ${row.displayName || row.email || "this member"}`}
          >
            <X className="h-4 w-4" />
          </Button>
        )}
      </div>
    </div>
  );
}
