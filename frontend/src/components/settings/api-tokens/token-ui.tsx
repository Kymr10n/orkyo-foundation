import { useState, type ReactNode } from "react";
import { useMutation } from "@tanstack/react-query";
import { toast } from "sonner";
import { CalendarIcon, Copy, Check, Trash2 } from "lucide-react";
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
import { ConfirmDialog } from "@foundation/src/components/ui/ConfirmDialog";
import { Label } from "@foundation/src/components/ui/label";
import { StatusBadge } from "@foundation/src/components/ui/status-badge";
import { Calendar } from "@foundation/src/components/ui/calendar";
import { Popover, PopoverContent, PopoverTrigger } from "@foundation/src/components/ui/popover";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@foundation/src/components/ui/select";
import { formatLocalized } from "@foundation/src/lib/formatters";
import type { ColumnDef } from "@foundation/src/components/ui/OrkyoDataTable";

/**
 * The pieces shared by every API-token management screen.
 *
 * Two token classes exist on purpose — read-only reporting tokens and write-capable API access
 * tokens — and they must stay separate in storage, auth and audit. Their *management UI*, however,
 * is the same job in both cases: name it, pick an expiry, copy the secret once, revoke it later.
 * That part lives here so the two screens cannot drift, while each screen keeps the copy and the
 * quick-start that make its own trust level clear.
 */

// ── The shape both token summaries share ─────────────────────────────────────

export interface TokenSummaryLike {
  id: string;
  name: string;
  tokenPrefix: string;
  createdAtUtc: string;
  lastUsedAtUtc: string | null;
  expiresAtUtc: string | null;
  revokedAtUtc: string | null;
  isActive: boolean;
}

// ── Expiry helpers ───────────────────────────────────────────────────────────

export type ExpiryMode = "7" | "30" | "60" | "90" | "custom" | "none";

export const EXPIRY_PRESETS: { value: ExpiryMode; days: number; label: string }[] = [
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

export function toDateOnly(date: Date): string {
  const pad = (value: number) => String(value).padStart(2, "0");
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

export function fromDateOnly(value: string): Date | undefined {
  const [year, month, day] = value.split("-").map(Number);
  if (!year || !month || !day) return undefined;
  return new Date(year, month - 1, day);
}

export function getPresetExpiry(days: number): string {
  return toDateOnly(addLocalDays(new Date(), days));
}

export function formatExpiryLabel(dateOnly: string): string {
  const date = fromDateOnly(dateOnly);
  if (!date) return "";
  return formatLocalized(date, { month: "short", day: "2-digit", year: "numeric" });
}

export function formatDate(iso: string | null): string {
  if (!iso) return "—";
  return formatLocalized(new Date(iso), { year: "numeric", month: "short", day: "numeric" });
}

/** Resolves the picker's state to the date string the API takes ("" means no expiry). */
export function resolveExpiry(mode: ExpiryMode, customExpiresAt: string): string {
  if (mode === "custom") return customExpiresAt;
  const preset = EXPIRY_PRESETS.find((p) => p.value === mode);
  return preset ? getPresetExpiry(preset.days) : "";
}

// ── Status ───────────────────────────────────────────────────────────────────

type TokenStatus = "revoked" | "expired" | "active";

export function tokenStatus(token: TokenSummaryLike): TokenStatus {
  if (token.revokedAtUtc) return "revoked";
  if (token.expiresAtUtc && new Date(token.expiresAtUtc) < new Date()) return "expired";
  return "active";
}

export const TOKEN_STATUS_LABEL: Record<TokenStatus, string> = {
  revoked: "Revoked",
  expired: "Expired",
  active: "Active",
};

const TOKEN_STATUS_TINT: Record<TokenStatus, string> = {
  revoked: "disabled",
  expired: "inactive",
  active: "active",
};

export function TokenStatusBadge({ token }: { token: TokenSummaryLike }) {
  const status = tokenStatus(token);
  return <StatusBadge status={TOKEN_STATUS_TINT[status]} label={TOKEN_STATUS_LABEL[status]} />;
}

// ── Copy ─────────────────────────────────────────────────────────────────────

export function CopyButton({ text }: { text: string }) {
  const [copied, setCopied] = useState(false);

  function handleCopy() {
    // navigator.clipboard is only exposed in secure contexts (HTTPS or localhost);
    // Community self-hosts may be reached over plain HTTP on a LAN. The token stays
    // visible in the dialog/table, so the user can still copy it manually.
    if (!navigator.clipboard?.writeText) {
      toast.error("Clipboard unavailable — copy the token manually");
      return;
    }
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

// ── Expiry picker ────────────────────────────────────────────────────────────

interface ExpiryFieldsProps {
  mode: ExpiryMode;
  onModeChange: (mode: ExpiryMode) => void;
  customExpiresAt: string;
  onCustomChange: (value: string) => void;
}

export function ExpiryFields({
  mode,
  onModeChange,
  customExpiresAt,
  onCustomChange,
}: ExpiryFieldsProps) {
  const selectedCustomDate = customExpiresAt ? fromDateOnly(customExpiresAt) : undefined;
  const today = fromDateOnly(toDateOnly(new Date())) ?? new Date();

  return (
    <div className="space-y-1.5">
      <div className="grid gap-3 sm:grid-cols-[220px_1fr] sm:items-start">
        <div className="space-y-1.5">
          <Label htmlFor="token-expiration">Expiration</Label>
          <Select value={mode} onValueChange={(value) => onModeChange(value as ExpiryMode)}>
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
        {mode === "custom" && (
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
                  onSelect={(date) => onCustomChange(date ? toDateOnly(date) : "")}
                  disabled={(date) => date < today}
                  autoFocus
                />
              </PopoverContent>
            </Popover>
          </div>
        )}
      </div>
      <p className="text-xs text-muted-foreground">
        {mode === "none"
          ? "The token will not expire automatically"
          : "The token will expire on the selected date"}
      </p>
    </div>
  );
}

// ── One-time secret reveal ───────────────────────────────────────────────────

interface RawTokenDialogProps {
  token: string | null;
  onClose: () => void;
  /** What someone holding this token can do — differs per credential class. */
  warning: ReactNode;
}

export function RawTokenDialog({ token, onClose, warning }: RawTokenDialogProps) {
  return (
    <Dialog open={!!token} onOpenChange={() => onClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Token created</DialogTitle>
          <DialogDescription>Copy this token now. It will not be shown again.</DialogDescription>
        </DialogHeader>
        <div className="bg-muted rounded-md p-3 font-mono text-sm break-all select-all">
          {token}
        </div>
        <Alert>
          <AlertDescription>{warning}</AlertDescription>
        </Alert>
        <DialogFooter className="gap-2">
          <CopyButton text={token ?? ""} />
          <Button onClick={onClose}>Done</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

// ── Revoke ───────────────────────────────────────────────────────────────────

interface RevokeTokenDialogProps<T extends TokenSummaryLike> {
  token: T | null;
  onOpenChange: (open: boolean) => void;
  revokeFn: (id: string) => Promise<void>;
  invalidates: readonly unknown[];
}

export function RevokeTokenDialog<T extends TokenSummaryLike>({
  token,
  onOpenChange,
  revokeFn,
  invalidates,
}: RevokeTokenDialogProps<T>) {
  const mutation = useMutation({
    mutationFn: (id: string) => revokeFn(id),
    meta: {
      successMessage: "Token revoked",
      errorMessage: "Failed to revoke token. Please try again.",
      invalidates: [invalidates],
    },
    onSuccess: () => onOpenChange(false),
  });

  return (
    <ConfirmDialog
      open={!!token}
      onOpenChange={onOpenChange}
      title={`Revoke "${token?.name}"?`}
      description="It will stop working immediately. Any integration using it will lose access."
      confirmLabel="Revoke"
      destructive
      isPending={mutation.isPending}
      onConfirm={() => {
        if (token) mutation.mutate(token.id);
      }}
    />
  );
}

// ── Table ────────────────────────────────────────────────────────────────────

/** The columns every token table shares. A screen can splice in its own (e.g. scopes). */
export function buildTokenColumns<T extends TokenSummaryLike>(
  onRevoke: (token: T) => void,
  extraColumns: ColumnDef<T>[] = [],
): ColumnDef<T>[] {
  return [
    {
      accessorKey: "name",
      header: "Name",
      meta: { filter: { type: "text" } },
      cell: ({ row }) => <span className="font-medium">{row.original.name}</span>,
    },
    {
      id: "prefix",
      header: "Prefix",
      cell: ({ row }) => (
        <span className="font-mono text-sm text-muted-foreground">{row.original.tokenPrefix}…</span>
      ),
    },
    ...extraColumns,
    {
      id: "status",
      accessorFn: (r) => tokenStatus(r),
      header: "Status",
      meta: {
        filter: { type: "enum", getLabel: (v) => TOKEN_STATUS_LABEL[v as TokenStatus] ?? v },
      },
      cell: ({ row }) => <TokenStatusBadge token={row.original} />,
    },
    {
      id: "created",
      accessorFn: (r) => r.createdAtUtc ?? "",
      header: "Created",
      meta: { filter: { type: "date" } },
      cell: ({ row }) => (
        <span className="text-sm text-muted-foreground">{formatDate(row.original.createdAtUtc)}</span>
      ),
    },
    {
      id: "lastUsed",
      accessorFn: (r) => r.lastUsedAtUtc ?? "",
      header: "Last used",
      meta: { filter: { type: "date" } },
      cell: ({ row }) => (
        <span className="text-sm text-muted-foreground">{formatDate(row.original.lastUsedAtUtc)}</span>
      ),
    },
    {
      id: "expires",
      accessorFn: (r) => r.expiresAtUtc ?? "",
      header: "Expires",
      meta: { filter: { type: "date" } },
      cell: ({ row }) => (
        <span className="text-sm text-muted-foreground">{formatDate(row.original.expiresAtUtc)}</span>
      ),
    },
    {
      id: "actions",
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
              onClick={(e) => {
                e.stopPropagation();
                onRevoke(token);
              }}
              aria-label={`Revoke ${token.name}`}
            >
              <Trash2 className="h-4 w-4" />
            </Button>
          </div>
        ) : null;
      },
    },
  ];
}

/** Phone presentation: name + status/prefix stacked, revoke trailing. */
export function renderTokenCard<T extends TokenSummaryLike>(
  token: T,
  onRevoke: (token: T) => void,
  subtitle?: ReactNode,
) {
  return (
    <div className="flex items-start justify-between gap-2">
      <div className="min-w-0 space-y-1">
        <div className="flex items-center gap-2 min-w-0">
          <span className="font-medium truncate">{token.name}</span>
          <TokenStatusBadge token={token} />
        </div>
        <p className="font-mono text-xs text-muted-foreground truncate">{token.tokenPrefix}…</p>
        {subtitle}
        <p className="text-xs text-muted-foreground truncate">
          Created {formatDate(token.createdAtUtc)} · Last used {formatDate(token.lastUsedAtUtc)}
        </p>
      </div>
      {token.isActive && (
        <Button
          variant="ghost"
          size="icon"
          className="h-8 w-8 text-muted-foreground hover:text-destructive"
          onClick={(e) => {
            e.stopPropagation();
            onRevoke(token);
          }}
          aria-label={`Revoke ${token.name}`}
        >
          <Trash2 className="h-4 w-4" />
        </Button>
      )}
    </div>
  );
}
