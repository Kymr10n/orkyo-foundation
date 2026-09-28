import { useState, type ReactNode } from "react";
import { toast } from "sonner";
import { CalendarIcon, Copy, Check, Plus, Trash2 } from "lucide-react";
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
import { FeatureUpsell } from "@foundation/src/components/ui/FeatureUpsell";
import { FormDialog } from "@foundation/src/components/ui/FormDialog";
import { Input } from "@foundation/src/components/ui/input";
import { Label } from "@foundation/src/components/ui/label";
import { LoadingSpinner } from "@foundation/src/components/ui/LoadingSpinner";
import { OrkyoDataTable } from "@foundation/src/components/ui/OrkyoDataTable";
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
import { formatDateDisplay, formatLocalized } from "@foundation/src/lib/formatters";
import { formatDateForInput } from "@foundation/src/lib/utils";
import { useAuth } from "@foundation/src/contexts/AuthContext";
import { useEntityFormDialog, type SaveMutation } from "@foundation/src/hooks/useEntityFormDialog";
import { useTableUrlState } from "@foundation/src/hooks/useTableUrlState";
import type { ColumnDef } from "@foundation/src/components/ui/OrkyoDataTable";
import { SettingsPageHeader } from "../SettingsPageHeader";

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

export function fromDateOnly(value: string): Date | undefined {
  const [year, month, day] = value.split("-").map(Number);
  if (!year || !month || !day) return undefined;
  return new Date(year, month - 1, day);
}

export function getPresetExpiry(days: number): string {
  return formatDateForInput(addLocalDays(new Date(), days));
}

function formatExpiryLabel(dateOnly: string): string {
  const date = fromDateOnly(dateOnly);
  if (!date) return "";
  return formatLocalized(date, { month: "short", day: "2-digit", year: "numeric" });
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

const TOKEN_STATUS_LABEL: Record<TokenStatus, string> = {
  revoked: "Revoked",
  expired: "Expired",
  active: "Active",
};

const TOKEN_STATUS_TINT: Record<TokenStatus, string> = {
  revoked: "disabled",
  expired: "inactive",
  active: "active",
};

function TokenStatusBadge({ token }: { token: TokenSummaryLike }) {
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
    navigator.clipboard.writeText(text).then(
      () => {
        setCopied(true);
        setTimeout(() => setCopied(false), 2000);
      },
      // The browser can refuse (permission denied, document not focused).
      () => toast.error("Could not copy — copy the token manually"),
    );
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
  const today = fromDateOnly(formatDateForInput(new Date())) ?? new Date();

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
                  onSelect={(date) => onCustomChange(date ? formatDateForInput(date) : "")}
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

function RawTokenDialog({ token, onClose, warning }: RawTokenDialogProps) {
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

/** A token-class revoke mutation from hooks/useApiTokens (it owns the toast and the refresh). */
type RevokeMutation = SaveMutation<void, string>;

interface RevokeTokenDialogProps<T extends TokenSummaryLike> {
  token: T | null;
  onOpenChange: (open: boolean) => void;
  mutation: RevokeMutation;
}

function RevokeTokenDialog<T extends TokenSummaryLike>({
  token,
  onOpenChange,
  mutation,
}: RevokeTokenDialogProps<T>) {
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
        if (token) mutation.mutate(token.id, { onSuccess: () => onOpenChange(false) });
      }}
    />
  );
}

// ── Table ────────────────────────────────────────────────────────────────────

/** The columns every token table shares. A screen can splice in its own (e.g. scopes). */
function buildTokenColumns<T extends TokenSummaryLike>(
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
        <span className="text-sm text-muted-foreground">{formatDateDisplay(row.original.createdAtUtc, "—")}</span>
      ),
    },
    {
      id: "lastUsed",
      accessorFn: (r) => r.lastUsedAtUtc ?? "",
      header: "Last used",
      meta: { filter: { type: "date" } },
      cell: ({ row }) => (
        <span className="text-sm text-muted-foreground">{formatDateDisplay(row.original.lastUsedAtUtc, "—")}</span>
      ),
    },
    {
      id: "expires",
      accessorFn: (r) => r.expiresAtUtc ?? "",
      header: "Expires",
      meta: { filter: { type: "date" } },
      cell: ({ row }) => (
        <span className="text-sm text-muted-foreground">{formatDateDisplay(row.original.expiresAtUtc, "—")}</span>
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
            <RevokeTokenButton token={token} onRevoke={onRevoke} />
          </div>
        ) : null;
      },
    },
  ];
}

/** Phone presentation: name + status/prefix stacked, revoke trailing. */
function renderTokenCard<T extends TokenSummaryLike>(
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
          Created {formatDateDisplay(token.createdAtUtc, "—")} · Last used {formatDateDisplay(token.lastUsedAtUtc, "—")}
        </p>
      </div>
      {token.isActive && <RevokeTokenButton token={token} onRevoke={onRevoke} />}
    </div>
  );
}

/** The trash icon of a table row or a card: stops propagation so the row click stays quiet. */
function RevokeTokenButton<T extends TokenSummaryLike>({
  token,
  onRevoke,
}: {
  token: T;
  onRevoke: (token: T) => void;
}) {
  return (
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
  );
}

// ── Create ───────────────────────────────────────────────────────────────────

/** Name and expiry: what creating any token asks for. */
interface TokenForm {
  name: string;
  expiryMode: ExpiryMode;
  customExpiresAt: string;
}

/** The request fields every token class takes. */
interface TokenRequestBase {
  name: string;
  expiresAt?: string;
}

interface CreateTokenDialogProps<TExtra extends object, TRequest> {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  onCreated: (rawToken: string) => void;
  title: string;
  description: string;
  namePlaceholder: string;
  defaultExpiry: ExpiryMode;
  /** The token class's create mutation from hooks/useApiTokens. */
  mutation: SaveMutation<{ rawToken: string }, TRequest>;
  /** The screen's own fields beyond name and expiry (e.g. an access level), with defaults. */
  initialExtra: TExtra;
  renderExtra?: (form: TForm<TExtra>, set: (patch: Partial<TExtra>) => void) => ReactNode;
  toRequest: (base: TokenRequestBase, form: TForm<TExtra>) => TRequest;
}

type TForm<TExtra> = TokenForm & TExtra;

/**
 * Name a token, pick its expiry, create it. The raw secret goes to `onCreated`, which shows it
 * once. A failure stays in the dialog, inline.
 */
export function CreateTokenDialog<TExtra extends object, TRequest>({
  open,
  onOpenChange,
  onCreated,
  title,
  description,
  namePlaceholder,
  defaultExpiry,
  mutation,
  initialExtra,
  renderExtra,
  toRequest,
}: CreateTokenDialogProps<TExtra, TRequest>) {
  // Create-only, so there is never an entity: the form reseeds each time the dialog opens.
  const emptyForm = () => ({ name: "", expiryMode: defaultExpiry, customExpiresAt: "", ...initialExtra });
  const { form, set, error, submit, isSubmitting } = useEntityFormDialog<
    never,
    TForm<TExtra>,
    { rawToken: string },
    TRequest
  >({
    open,
    onOpenChange,
    entity: null,
    emptyForm,
    toForm: emptyForm,
    mutation,
    toVariables: (f) => {
      const expiresAt = resolveExpiry(f.expiryMode, f.customExpiresAt);
      return toRequest({ name: f.name, ...(expiresAt ? { expiresAt } : {}) }, f);
    },
    onSaved: (created) => onCreated(created.rawToken),
  });
  const expiresAt = resolveExpiry(form.expiryMode, form.customExpiresAt);

  return (
    <FormDialog
      open={open}
      onOpenChange={onOpenChange}
      title={title}
      description={description}
      onSubmit={submit}
      isSubmitting={isSubmitting}
      submitLabel="Create token"
      submitDisabled={!(form.name.trim() && (form.expiryMode !== "custom" || !!expiresAt))}
      error={error}
    >
      <div className="space-y-1.5">
        <Label htmlFor="token-name">Name</Label>
        <Input
          id="token-name"
          placeholder={namePlaceholder}
          value={form.name}
          onChange={(e) => set({ name: e.target.value } as Partial<TForm<TExtra>>)}
          autoFocus
        />
      </div>
      {renderExtra?.(form, (patch) => set(patch as Partial<TForm<TExtra>>))}
      <ExpiryFields
        mode={form.expiryMode}
        onModeChange={(expiryMode) => set({ expiryMode } as Partial<TForm<TExtra>>)}
        customExpiresAt={form.customExpiresAt}
        onCustomChange={(customExpiresAt) => set({ customExpiresAt } as Partial<TForm<TExtra>>)}
      />
    </FormDialog>
  );
}

// ── Page ─────────────────────────────────────────────────────────────────────

interface TokenSettingsPageProps<T extends TokenSummaryLike> {
  /** When set, the locked state shows a CTA linking here (e.g. the plans page). */
  upgradeHref?: string;
  /** The API-access entitlement; the list query runs only with it. */
  apiAccessAllowed: boolean;
  tokens: { data?: T[]; isLoading: boolean; error: unknown; refetch: () => unknown };
  title: string;
  description: string;
  upsell: { title: string; description: string; points: ReactNode };
  unavailableMessage: string;
  loadErrorMessage: string;
  emptyMessage: string;
  /** The URL-state key of the table's sort and filters. */
  tableKey: string;
  extraColumns?: ColumnDef<T>[];
  cardSubtitle?: (token: T) => ReactNode;
  quickStart: ReactNode;
  renderCreateDialog: (props: {
    open: boolean;
    onOpenChange: (open: boolean) => void;
    onCreated: (rawToken: string) => void;
  }) => ReactNode;
  rawTokenWarning: ReactNode;
  revokeMutation: RevokeMutation;
}

/**
 * One API-token management screen: entitlement gate, list, create, show-once, revoke. Each token
 * class passes only what differs — its copy, its extra column, its create fields and its hooks.
 */
export function TokenSettingsPage<T extends TokenSummaryLike>({
  upgradeHref,
  apiAccessAllowed,
  tokens,
  title,
  description,
  upsell,
  unavailableMessage,
  loadErrorMessage,
  emptyMessage,
  tableKey,
  extraColumns,
  cardSubtitle,
  quickStart,
  renderCreateDialog,
  rawTokenWarning,
  revokeMutation,
}: TokenSettingsPageProps<T>) {
  const { isLoading: authLoading } = useAuth();
  const [createOpen, setCreateOpen] = useState(false);
  const [rawToken, setRawToken] = useState<string | null>(null);
  const [revokeTarget, setRevokeTarget] = useState<T | null>(null);

  const columns = buildTokenColumns<T>(setRevokeTarget, extraColumns);
  // Header sort/filter state lives in the URL: bookmarkable, shareable, Back-safe.
  const tableUrlState = useTableUrlState(tableKey, columns);

  if (authLoading) {
    return <LoadingSpinner fullScreen={false} className="py-12" />;
  }

  if (!apiAccessAllowed) {
    // Paid-tier gate. Rather than silently redirecting, keep the user on the page and explain
    // the feature and the upgrade path. With no upgrade target (e.g. Community, which has no
    // plans), a plain unavailable notice.
    if (upgradeHref) {
      return (
        <FeatureUpsell title={upsell.title} description={upsell.description} upgradeHref={upgradeHref}>
          {upsell.points}
        </FeatureUpsell>
      );
    }
    return (
      <Alert>
        <AlertDescription>{unavailableMessage}</AlertDescription>
      </Alert>
    );
  }

  if (tokens.isLoading) {
    return <LoadingSpinner fullScreen={false} className="py-12" />;
  }

  return (
    <div className="space-y-6">
      <SettingsPageHeader title={title} description={description}>
        <Button size="sm" onClick={() => setCreateOpen(true)} className="gap-1.5">
          <Plus className="h-4 w-4" />
          New token
        </Button>
      </SettingsPageHeader>

      <OrkyoDataTable
        {...tableUrlState}
        columns={columns}
        data={tokens.data ?? []}
        error={tokens.error ? loadErrorMessage : null}
        onRetry={() => void tokens.refetch()}
        emptyMessage={emptyMessage}
        renderCard={(token) => renderTokenCard(token, setRevokeTarget, cardSubtitle?.(token))}
      />

      {quickStart}

      {renderCreateDialog({ open: createOpen, onOpenChange: setCreateOpen, onCreated: setRawToken })}
      <RawTokenDialog token={rawToken} onClose={() => setRawToken(null)} warning={rawTokenWarning} />
      <RevokeTokenDialog
        token={revokeTarget}
        onOpenChange={(open) => !open && setRevokeTarget(null)}
        mutation={revokeMutation}
      />
    </div>
  );
}
