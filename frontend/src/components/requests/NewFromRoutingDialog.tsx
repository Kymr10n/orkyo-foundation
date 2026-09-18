import { useState } from "react";
import { FormDialog } from "@foundation/src/components/ui/FormDialog";
import { FormField } from "@foundation/src/components/ui/FormField";
import { Input } from "@foundation/src/components/ui/input";
import { DateTimePicker } from "@foundation/src/components/ui/date-time-picker";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@foundation/src/components/ui/select";
import { useInstantiateRouting, useRoutings } from "@foundation/src/hooks/useRoutings";
import { useIsMultiSite, useSites } from "@foundation/src/hooks/useSites";
import { errorMessage } from "@foundation/src/hooks/mutation-utils";
import { canHaveChildren } from "@foundation/src/domain/request-tree";
import type { Request } from "@foundation/src/types/requests";
import type { InstantiateRoutingRequest } from "@foundation/src/types/routings";

interface NewFromRoutingDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** Requests on the page; the non-leaf ones are offered as the work order's parent. */
  requests: Request[];
  /** The page's site filter, so the work order lands where the planner is looking. */
  defaultSiteId?: string | null;
}

/** Radix Select rejects an empty item value, so "none" needs a stand-in. */
const NONE = "__none__";

interface FormState {
  routingId: string;
  name: string;
  siteId: string;
  quantity: string;
  earliestStart: string;
  latestEnd: string;
  parentRequestId: string;
}

/** A local "YYYY-MM-DDTHH:mm" from the picker, or nothing, as an ISO instant. */
function toIso(local: string): string | undefined {
  return local ? new Date(local).toISOString() : undefined;
}

/** @internal Exported for unit testing. The request body, or the first problem with the form. */
export function buildInstantiateRequest(
  form: FormState,
): { request: InstantiateRoutingRequest } | { error: string } {
  if (!form.routingId) return { error: "Choose a routing" };
  if (!form.name.trim()) return { error: "Name is required" };
  if (!/^\d+$/.test(form.quantity.trim()) || parseInt(form.quantity, 10) < 1) {
    return { error: "Quantity must be a whole number of at least 1" };
  }
  const earliestStartTs = toIso(form.earliestStart);
  const latestEndTs = toIso(form.latestEnd);
  if (earliestStartTs && latestEndTs && latestEndTs <= earliestStartTs) {
    return { error: "Latest end must be after earliest start" };
  }
  return {
    request: {
      name: form.name.trim(),
      siteId: form.siteId || undefined,
      quantity: parseInt(form.quantity, 10),
      earliestStartTs,
      latestEndTs,
      parentRequestId: form.parentRequestId || undefined,
    },
  };
}

/**
 * Creates a work order from a routing: one container request holding one leaf per
 * operation, chained finish-to-start, each sized as setup + run × quantity. The result is
 * ordinary requests, so auto-schedule and the plan view treat them like any other.
 */
export function NewFromRoutingDialog({
  open,
  onOpenChange,
  requests,
  defaultSiteId,
}: NewFromRoutingDialogProps) {
  const empty: FormState = {
    routingId: "",
    name: "",
    siteId: defaultSiteId ?? "",
    quantity: "1",
    earliestStart: "",
    latestEnd: "",
    parentRequestId: "",
  };
  const [form, setForm] = useState<FormState>(empty);
  const [error, setError] = useState<string | null>(null);

  const { data: routings = [] } = useRoutings();
  const { data: sites = [] } = useSites();
  const isMultiSite = useIsMultiSite();
  const instantiate = useInstantiateRouting();

  // Reseed on open (render-phase, see SiteEditDialog).
  const [syncedOpen, setSyncedOpen] = useState(false);
  if (syncedOpen !== open) {
    setSyncedOpen(open);
    if (open) {
      setError(null);
      setForm(empty);
    }
  }

  const isDirty = JSON.stringify(form) !== JSON.stringify(empty);
  const parents = requests.filter((r) => canHaveChildren(r.planningMode));

  const chooseRouting = (routingId: string) => {
    // The routing's name is the usual work-order name; keep what the planner typed already.
    const routing = routings.find((r) => r.id === routingId);
    setForm((f) => ({
      ...f,
      routingId,
      name: f.name.trim() ? f.name : (routing?.name ?? ""),
    }));
  };

  const handleSubmit = async () => {
    setError(null);
    const built = buildInstantiateRequest(form);
    if ("error" in built) {
      setError(built.error);
      return;
    }
    try {
      await instantiate.mutateAsync({ id: form.routingId, request: built.request });
      onOpenChange(false);
    } catch (err) {
      setError(errorMessage(err));
    }
  };

  return (
    <FormDialog
      open={open}
      onOpenChange={onOpenChange}
      title="New from routing"
      description="Creates one request per operation, chained in order, sized as setup plus run time × quantity."
      onSubmit={handleSubmit}
      isSubmitting={instantiate.isPending}
      submitLabel="Create work order"
      submittingLabel="Creating..."
      error={error}
      dirty={isDirty}
    >
      <FormField htmlFor="routing" label="Routing" required>
        <Select value={form.routingId} onValueChange={chooseRouting} disabled={instantiate.isPending}>
          <SelectTrigger id="routing">
            <SelectValue placeholder={routings.length ? "Choose a routing" : "No routings defined yet"} />
          </SelectTrigger>
          <SelectContent>
            {routings.map((r) => (
              <SelectItem key={r.id} value={r.id}>
                {r.name}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </FormField>

      <FormField htmlFor="work-order-name" label="Name" required>
        <Input
          id="work-order-name"
          placeholder="e.g., WO-2026-0142"
          value={form.name}
          onChange={(e) => setForm({ ...form, name: e.target.value })}
          disabled={instantiate.isPending}
        />
      </FormField>

      <FormField htmlFor="quantity" label="Quantity" required help="Each step takes setup time plus run time per unit × quantity.">
        <Input
          id="quantity"
          inputMode="numeric"
          value={form.quantity}
          onChange={(e) => setForm({ ...form, quantity: e.target.value })}
          disabled={instantiate.isPending}
          className="w-32"
        />
      </FormField>

      {isMultiSite && (
        <FormField htmlFor="site" label="Site">
          <Select
            value={form.siteId || NONE}
            onValueChange={(v) => setForm({ ...form, siteId: v === NONE ? "" : v })}
            disabled={instantiate.isPending}
          >
            <SelectTrigger id="site">
              <SelectValue placeholder="Any site" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value={NONE}>
                <span className="text-muted-foreground">Any site</span>
              </SelectItem>
              {sites.map((site) => (
                <SelectItem key={site.id} value={site.id}>
                  {site.name}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </FormField>
      )}

      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
        <FormField htmlFor="earliest-start" label="Earliest start">
          <DateTimePicker
            id="earliest-start"
            value={form.earliestStart}
            onChange={(v) => setForm({ ...form, earliestStart: v })}
            disabled={instantiate.isPending}
          />
        </FormField>
        <FormField htmlFor="latest-end" label="Latest end" help="Every step inherits this window.">
          <DateTimePicker
            id="latest-end"
            value={form.latestEnd}
            onChange={(v) => setForm({ ...form, latestEnd: v })}
            disabled={instantiate.isPending}
          />
        </FormField>
      </div>

      {parents.length > 0 && (
        <FormField htmlFor="parent" label="Parent">
          <Select
            value={form.parentRequestId || NONE}
            onValueChange={(v) => setForm({ ...form, parentRequestId: v === NONE ? "" : v })}
            disabled={instantiate.isPending}
          >
            <SelectTrigger id="parent">
              <SelectValue placeholder="Top level" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value={NONE}>
                <span className="text-muted-foreground">Top level</span>
              </SelectItem>
              {parents.map((p) => (
                <SelectItem key={p.id} value={p.id}>
                  {p.name}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </FormField>
      )}
    </FormDialog>
  );
}
