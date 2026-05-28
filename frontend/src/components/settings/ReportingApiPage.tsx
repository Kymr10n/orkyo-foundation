import { useState } from "react";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { Loader2, Plus, Trash2, Copy, Check, Key } from "lucide-react";
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
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@foundation/src/components/ui/table";
import { SettingsPageHeader } from "./SettingsPageHeader";
import {
  listReportingTokens,
  createReportingToken,
  revokeReportingToken,
  type ReportingTokenSummary,
} from "@foundation/src/lib/api/reporting-tokens-api";

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
  const [expiresAt, setExpiresAt] = useState("");

  const mutation = useMutation({
    mutationFn: () =>
      createReportingToken({ name, ...(expiresAt ? { expiresAt } : {}) }),
    onSuccess: (result) => {
      queryClient.invalidateQueries({ queryKey: ["reporting-tokens"] });
      onOpenChange(false);
      setName("");
      setExpiresAt("");
      onCreated(result.rawToken);
    },
    onError: () => {
      toast.error("Failed to create token. Please try again.");
    },
  });

  function handleSubmit(e: React.SubmitEvent) {
    e.preventDefault();
    if (name.trim()) mutation.mutate();
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
            <Label htmlFor="token-expires">Expiry date (optional)</Label>
            <Input
              id="token-expires"
              type="date"
              value={expiresAt}
              min={new Date().toISOString().slice(0, 10)}
              onChange={(e) => setExpiresAt(e.target.value)}
            />
          </div>
          <DialogFooter>
            <Button type="button" variant="ghost" onClick={() => onOpenChange(false)}>
              Cancel
            </Button>
            <Button type="submit" disabled={!name.trim() || mutation.isPending}>
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

export function ReportingApiPage() {
  const [createOpen, setCreateOpen] = useState(false);
  const [rawToken, setRawToken] = useState<string | null>(null);
  const [revokeTarget, setRevokeTarget] = useState<ReportingTokenSummary | null>(null);

  const { data: tokens = [], isLoading, error } = useQuery({
    queryKey: ["reporting-tokens"],
    queryFn: listReportingTokens,
  });

  if (isLoading) {
    return (
      <div className="flex items-center justify-center py-12">
        <Loader2 className="h-8 w-8 animate-spin text-muted-foreground" />
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
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Name</TableHead>
              <TableHead>Prefix</TableHead>
              <TableHead>Status</TableHead>
              <TableHead>Created</TableHead>
              <TableHead>Last used</TableHead>
              <TableHead>Expires</TableHead>
              <TableHead className="w-10" />
            </TableRow>
          </TableHeader>
          <TableBody>
            {tokens.map((token) => (
              <TableRow key={token.id}>
                <TableCell className="font-medium">{token.name}</TableCell>
                <TableCell className="font-mono text-sm text-muted-foreground">
                  {token.tokenPrefix}…
                </TableCell>
                <TableCell>
                  <TokenStatusBadge token={token} />
                </TableCell>
                <TableCell className="text-sm text-muted-foreground">
                  {formatDate(token.createdAtUtc)}
                </TableCell>
                <TableCell className="text-sm text-muted-foreground">
                  {formatDate(token.lastUsedAtUtc)}
                </TableCell>
                <TableCell className="text-sm text-muted-foreground">
                  {formatDate(token.expiresAtUtc)}
                </TableCell>
                <TableCell>
                  {token.isActive && (
                    <Button
                      variant="ghost"
                      size="icon"
                      className="h-8 w-8 text-muted-foreground hover:text-destructive"
                      onClick={() => setRevokeTarget(token)}
                      aria-label={`Revoke ${token.name}`}
                    >
                      <Trash2 className="h-4 w-4" />
                    </Button>
                  )}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
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
