import { useEffect, useState } from "react";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { useAuth } from "@foundation/src/contexts/AuthContext";
import { useReportingApiAvailable } from "@foundation/src/hooks/useReportingApiAvailable";
import { CalendarIcon, Loader2, Plus, Trash2, Copy, Check, Key } from "lucide-react";
import { LoadingSpinner } from "@foundation/src/components/ui/LoadingSpinner";
import { FeatureUpsell } from "@foundation/src/components/ui/FeatureUpsell";
import { Alert, AlertDescription } from "@foundation/src/components/ui/alert";
import { Button } from "@foundation/src/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@foundation/src/components/ui/dialog";
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from "@foundation/src/components/ui/alert-dialog";
import { Input } from "@foundation/src/components/ui/input";
import { Label } from "@foundation/src/components/ui/label";
import { Badge } from "@foundation/src/components/ui/badge";
import { Calendar } from "@foundation/src/components/ui/calendar";
import { Popover, PopoverContent, PopoverTrigger } from "@foundation/src/components/ui/popover";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@foundation/src/components/ui/select";
import { SettingsPageHeader } from "./SettingsPageHeader";
import { OrkyoDataTable, type ColumnDef } from "@foundation/src/components/ui/OrkyoDataTable";
import {
  listReportingTokens,
  createReportingToken,
  revokeReportingToken,
  type ReportingTokenSummary,
} from "@foundation/src/lib/api/reporting-tokens-api";

type ExpiryMode = "7" | "30" | "60" | "90" | "custom" | "none";

const EXPIRY_PRESETS: { value: ExpiryMode; days: number; label: string }[] = [
  { value: "7", days: 7, label: "7 days" },
  { value: "30", days: 30, label: "30 days" },
  { value: "60", days: 60, label: "60 days" },
  { value: "90", days: 90, label: "90 days" },
];

function addLocalDays(date: Date, days: number): Date {
  const next = new Date(date);
  next.setDate(next.getDate() + days);
  return next;
}

function toDateOnly(date: Date): string {
  const pad = (value: number) => String(value).padStart(2, "0");
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

function fromDateOnly(value: string): Date | undefined {
  const [year, month, day] = value.split("-").map(Number);
  if (!year || !month || !day) return undefined;
  return new Date(year, month - 1, day);
}

function getPresetExpiry(days: number): string {
  return toDateOnly(addLocalDays(new Date(), days));
}

function formatExpiryLabel(dateOnly: string): string {
  const date = fromDateOnly(dateOnly);
  if (!date) return "";
  return date.toLocaleDateString(undefined, {
    month: "short",
    day: "2-digit",
    year: "numeric",
  });
}

function formatDate(iso: string | null): string {
  if (!iso) return "—";
  return new Date(iso).toLocaleDateString(undefined, {
    year: "numeric",
    month: "short",
    day: "numeric",
  });
}

function TokenStatusBadge({ token }: { token: ReportingTokenSummary }) {
  if (token.revokedAtUtc) return <Badge variant="destructive">Revoked</Badge>;
  if (token.expiresAtUtc && new Date(token.expiresAtUtc) < new Date())
    return <Badge variant="secondary">Expired</Badge>;
  return <Badge variant="default">Active</Badge>;
}

function CopyButton({ text }: { text: string }) {
  const [copied, setCopied] = useState(false);

  function handleCopy() {
    navigator.clipboard.writeText(text).then(() => {
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    });
  }

  return (
    <Button variant="outline" size="sm" onClick={handleCopy} className="gap-1.5">
      {copied ? <Check className="h-3.5 w-3.5" /> : <Copy className="h-3.5 w-3.5" />}
      {copied ? "Copied" : "Copy"}
    </Button>
  );
}

interface CreateTokenDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  onCreated: (rawToken: string) => void;
}

function CreateTokenDialog({ open, onOpenChange, onCreated }: CreateTokenDialogProps) {
  const queryClient = useQueryClient();
  const [name, setName] = useState("");
  const [expiryMode, setExpiryMode] = useState<ExpiryMode>("7");
  const [customExpiresAt, setCustomExpiresAt] = useState("");
  const selectedPreset = EXPIRY_PRESETS.find((preset) => preset.value === expiryMode);
  const expiresAt = expiryMode === "custom"
    ? customExpiresAt
    : selectedPreset
      ? getPresetExpiry(selectedPreset.days)
      : "";
  const selectedCustomDate = customExpiresAt ? fromDateOnly(customExpiresAt) : undefined;
  const today = fromDateOnly(toDateOnly(new Date())) ?? new Date();

  useEffect(() => {
    if (!open) return;
    setName("");
    setExpiryMode("7");
    setCustomExpiresAt("");
  }, [open]);

  const mutation = useMutation({
    mutationFn: () => createReportingToken({
      name,
      ...(expiresAt ? { expiresAt } : {}),
    }),
    onSuccess: (result) => {
      queryClient.invalidateQueries({ queryKey: ["reporting-tokens"] });
      onOpenChange(false);
      setName("");
      setExpiryMode("7");
      setCustomExpiresAt("");
      onCreated(result.rawToken);
    },
    onError: () => {
      toast.error("Failed to create token. Please try again.");
    },
  });

  function handleSubmit(e: React.SubmitEvent) {
    e.preventDefault();
    if (name.trim() && (expiryMode !== "custom" || expiresAt)) mutation.mutate();
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Create Reporting Token</DialogTitle>
          <DialogDescription>
            This token grants read-only access to reporting data for this workspace. It will be
            shown once — copy it before closing.
          </DialogDescription>
        </DialogHeader>
        <form onSubmit={handleSubmit} className="space-y-4">
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
          <div className="space-y-1.5">
            <div className="grid gap-3 sm:grid-cols-[220px_1fr] sm:items-start">
              <div className="space-y-1.5">
                <Label htmlFor="token-expiration">Expiration</Label>
                <Select value={expiryMode} onValueChange={(value) => setExpiryMode(value as ExpiryMode)}>
                  <SelectTrigger id="token-expiration" className="h-9 min-w-[220px]">
                    <CalendarIcon className="mr-2 h-4 w-4" />
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent className="min-w-[220px]">
                    {EXPIRY_PRESETS.map((preset) => (
                      <SelectItem key={preset.value} value={preset.value}>
                        {preset.label} ({formatExpiryLabel(getPresetExpiry(preset.days))})
                      </SelectItem>
                    ))}
                    <SelectItem value="custom">Custom</SelectItem>
                    <SelectItem value="none">No expiration</SelectItem>
                  </SelectContent>
                </Select>
              </div>
              {expiryMode === "custom" && (
                <div className="space-y-1.5">
                  <Label htmlFor="token-custom-expires">Select date *</Label>
                  <Popover>
                    <PopoverTrigger asChild>
                      <Button
                        id="token-custom-expires"
                        type="button"
                        variant="outline"
                        className="h-9 w-full justify-start text-left font-normal"
                      >
                        {customExpiresAt ? formatExpiryLabel(customExpiresAt) : "dd . mm . yyyy"}
                        <CalendarIcon className="ml-auto h-4 w-4 opacity-70" />
                      </Button>
                    </PopoverTrigger>
                    <PopoverContent className="w-auto p-0" align="start">
                      <Calendar
                        mode="single"
                        selected={selectedCustomDate}
                        onSelect={(date) => setCustomExpiresAt(date ? toDateOnly(date) : "")}
                        disabled={(date) => date < today}
                        autoFocus
                      />
                    </PopoverContent>
                  </Popover>
                </div>
              )}
            </div>
            <p className="text-xs text-muted-foreground">
              {expiryMode === "none"
                ? "The token will not expire automatically"
                : "The token will expire on the selected date"}
            </p>
          </div>
          <DialogFooter>
            <Button type="button" variant="ghost" onClick={() => onOpenChange(false)}>
              Cancel
            </Button>
            <Button
              type="submit"
              disabled={!name.trim() || (expiryMode === "custom" && !expiresAt) || mutation.isPending}
            >
              {mutation.isPending && <Loader2 className="h-4 w-4 mr-1.5 animate-spin" />}
              Create token
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}

interface RawTokenDialogProps {
  token: string | null;
  onClose: () => void;
}

function RawTokenDialog({ token, onClose }: RawTokenDialogProps) {
  return (
    <Dialog open={!!token} onOpenChange={() => onClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Token created</DialogTitle>
          <DialogDescription>
            Copy this token now. It will not be shown again.
          </DialogDescription>
        </DialogHeader>
        <div className="bg-muted rounded-md p-3 font-mono text-sm break-all select-all">
          {token}
        </div>
        <Alert>
          <AlertDescription>
            Store this token securely. Anyone with it can read your workspace's reporting data.
          </AlertDescription>
        </Alert>
        <DialogFooter className="gap-2">
          <CopyButton text={token ?? ""} />
          <Button onClick={onClose}>Done</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

interface RevokeDialogProps {
  token: ReportingTokenSummary | null;
  onOpenChange: (open: boolean) => void;
}

function RevokeDialog({ token, onOpenChange }: RevokeDialogProps) {
  const queryClient = useQueryClient();

  const mutation = useMutation({
    mutationFn: (id: string) => revokeReportingToken(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["reporting-tokens"] });
      onOpenChange(false);
      toast.success("Token revoked.");
    },
    onError: () => {
      toast.error("Failed to revoke token. Please try again.");
    },
  });

  return (
    <AlertDialog open={!!token} onOpenChange={onOpenChange}>
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>Revoke token?</AlertDialogTitle>
          <AlertDialogDescription>
            <strong>{token?.name}</strong> will stop working immediately. Any integration using it
            will lose access.
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel>Cancel</AlertDialogCancel>
          <AlertDialogAction
            onClick={() => token && mutation.mutate(token.id)}
            disabled={mutation.isPending}
            className="bg-destructive text-destructive-foreground hover:bg-destructive/90"
          >
            {mutation.isPending && <Loader2 className="h-4 w-4 mr-1.5 animate-spin" />}
            Revoke
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
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

interface ReportingApiPageProps {
  /** When set, the locked state shows a CTA linking here (e.g. the plans page). */
  upgradeHref?: string;
}

export function ReportingApiPage({ upgradeHref }: ReportingApiPageProps = {}) {
  // Paid-tier gate: reporting API keys require API access (Professional+).
  const { isLoading: authLoading } = useAuth();
  const apiAccessAllowed = useReportingApiAvailable();

  const [createOpen, setCreateOpen] = useState(false);
  const [rawToken, setRawToken] = useState<string | null>(null);
  const [revokeTarget, setRevokeTarget] = useState<ReportingTokenSummary | null>(null);

  const { data: tokens = [], isLoading, error } = useQuery({
    queryKey: ["reporting-tokens"],
    queryFn: listReportingTokens,
    enabled: apiAccessAllowed,
  });

  const columns: ColumnDef<ReportingTokenSummary>[] = [
    {
      accessorKey: 'name',
      header: 'Name',
      cell: ({ row }) => <span className="font-medium">{row.original.name}</span>,
    },
    {
      id: 'prefix',
      header: 'Prefix',
      cell: ({ row }) => (
        <span className="font-mono text-sm text-muted-foreground">{row.original.tokenPrefix}…</span>
      ),
    },
    {
      id: 'status',
      header: 'Status',
      cell: ({ row }) => <TokenStatusBadge token={row.original} />,
    },
    {
      id: 'created',
      header: 'Created',
      cell: ({ row }) => (
        <span className="text-sm text-muted-foreground">{formatDate(row.original.createdAtUtc)}</span>
      ),
    },
    {
      id: 'lastUsed',
      header: 'Last used',
      cell: ({ row }) => (
        <span className="text-sm text-muted-foreground">{formatDate(row.original.lastUsedAtUtc)}</span>
      ),
    },
    {
      id: 'expires',
      header: 'Expires',
      cell: ({ row }) => (
        <span className="text-sm text-muted-foreground">{formatDate(row.original.expiresAtUtc)}</span>
      ),
    },
    {
      id: 'actions',
      header: () => null,
      size: 56,
      cell: ({ row }) => {
        const token = row.original;
        return token.isActive ? (
          <div className="flex justify-end">
            <Button
              variant="ghost"
              size="icon"
              className="h-8 w-8 text-muted-foreground hover:text-destructive"
              onClick={(e) => { e.stopPropagation(); setRevokeTarget(token); }}
              aria-label={`Revoke ${token.name}`}
            >
              <Trash2 className="h-4 w-4" />
            </Button>
          </div>
        ) : null;
      },
    },
  ];

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
          columns={columns}
          data={tokens}
          filterColumn="name"
          filterPlaceholder="Search tokens..."
        />
      )}

      <PowerBiQuickStart />

      <CreateTokenDialog
        open={createOpen}
        onOpenChange={setCreateOpen}
        onCreated={(t) => setRawToken(t)}
      />
      <RawTokenDialog token={rawToken} onClose={() => setRawToken(null)} />
      <RevokeDialog token={revokeTarget} onOpenChange={(open) => !open && setRevokeTarget(null)} />
    </div>
  );
}
