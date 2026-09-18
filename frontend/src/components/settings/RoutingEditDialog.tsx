import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { ArrowDown, ArrowUp, Plus, Trash2 } from "lucide-react";
import { FormDialog } from "@foundation/src/components/ui/FormDialog";
import { FormField } from "@foundation/src/components/ui/FormField";
import { Button } from "@foundation/src/components/ui/button";
import { Input } from "@foundation/src/components/ui/input";
import { Label } from "@foundation/src/components/ui/label";
import { Textarea } from "@foundation/src/components/ui/textarea";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@foundation/src/components/ui/select";
import { getTemplates } from "@foundation/src/lib/api/template-api";
import { qk } from "@foundation/src/lib/api/query-keys";
import { useCreateRouting, useUpdateRouting } from "@foundation/src/hooks/useRoutings";
import { errorMessage } from "@foundation/src/hooks/mutation-utils";
import type { Routing, RoutingStepRequest } from "@foundation/src/types/routings";

interface RoutingEditDialogProps {
  /** null = create mode. */
  routing: Routing | null;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

/** One row of the steps table; the step number is its position. Minutes stay as typed. */
interface StepForm {
  operationTemplateId: string;
  setupMinutes: string;
  runMinutesPerUnit: string;
  lagMinutesAfter: string;
}

interface FormState {
  name: string;
  description: string;
  steps: StepForm[];
}

const emptyStep: StepForm = {
  operationTemplateId: "",
  setupMinutes: "0",
  runMinutesPerUnit: "0",
  lagMinutesAfter: "0",
};

const empty: FormState = { name: "", description: "", steps: [{ ...emptyStep }] };

function fromRouting(routing: Routing): FormState {
  return {
    name: routing.name,
    description: routing.description ?? "",
    steps: [...routing.steps]
      .sort((a, b) => a.stepNo - b.stepNo)
      .map((s) => ({
        operationTemplateId: s.operationTemplateId,
        setupMinutes: String(s.setupMinutes),
        runMinutesPerUnit: String(s.runMinutesPerUnit),
        lagMinutesAfter: String(s.lagMinutesAfter),
      })),
  };
}

/** Whole minutes, never negative; anything else is a validation error, not a silent 0. */
function minutes(raw: string): number | null {
  if (!/^\d+$/.test(raw.trim())) return null;
  return parseInt(raw, 10);
}

/** @internal Exported for unit testing. Returns the payload steps or the first problem. */
export function toStepRequests(steps: StepForm[]): { steps: RoutingStepRequest[] } | { error: string } {
  if (steps.length === 0) return { error: "A routing needs at least one step" };
  const out: RoutingStepRequest[] = [];
  for (let i = 0; i < steps.length; i++) {
    const s = steps[i];
    const n = i + 1;
    if (!s.operationTemplateId) return { error: `Step ${n}: choose an operation` };
    const setup = minutes(s.setupMinutes);
    const run = minutes(s.runMinutesPerUnit);
    const lag = minutes(s.lagMinutesAfter);
    if (setup === null || run === null || lag === null) {
      return { error: `Step ${n}: minutes must be whole numbers` };
    }
    if (setup + run === 0) {
      return { error: `Step ${n}: setup or run time per unit must be positive` };
    }
    out.push({
      stepNo: n,
      operationTemplateId: s.operationTemplateId,
      setupMinutes: setup,
      runMinutesPerUnit: run,
      lagMinutesAfter: lag,
    });
  }
  return { steps: out };
}

/**
 * A routing is the ordered list of operations a part goes through. Each operation is a
 * request template that names the resource type it needs; the times here are this part's,
 * so one "Mill" template serves every part that is milled.
 */
export function RoutingEditDialog({ routing, open, onOpenChange }: RoutingEditDialogProps) {
  const [form, setForm] = useState<FormState>(empty);
  const [baseline, setBaseline] = useState<FormState>(empty);
  const [error, setError] = useState<string | null>(null);

  const createMutation = useCreateRouting();
  const updateMutation = useUpdateRouting();
  const isSubmitting = routing ? updateMutation.isPending : createMutation.isPending;

  const { data: templates = [] } = useQuery({
    queryKey: qk.templates("request"),
    queryFn: () => getTemplates("request"),
    enabled: open,
  });

  // Reseed when the dialog opens or swaps routing while open (render-phase, see SiteEditDialog).
  const [synced, setSynced] = useState<{ open: boolean; routing: Routing | null } | null>(null);
  if (synced?.open !== open || synced.routing !== routing) {
    setSynced({ open, routing });
    if (open) {
      setError(null);
      const next = routing ? fromRouting(routing) : empty;
      setForm(next);
      setBaseline(next);
    }
  }

  const isDirty = JSON.stringify(form) !== JSON.stringify(baseline);

  const setStep = (index: number, patch: Partial<StepForm>) =>
    setForm((f) => ({
      ...f,
      steps: f.steps.map((s, i) => (i === index ? { ...s, ...patch } : s)),
    }));

  const addStep = () => setForm((f) => ({ ...f, steps: [...f.steps, { ...emptyStep }] }));

  const removeStep = (index: number) =>
    setForm((f) => ({ ...f, steps: f.steps.filter((_, i) => i !== index) }));

  const moveStep = (index: number, delta: -1 | 1) =>
    setForm((f) => {
      const target = index + delta;
      if (target < 0 || target >= f.steps.length) return f;
      const steps = [...f.steps];
      [steps[index], steps[target]] = [steps[target], steps[index]];
      return { ...f, steps };
    });

  const handleSubmit = async () => {
    setError(null);
    if (!form.name.trim()) {
      setError("Name is required");
      return;
    }
    const converted = toStepRequests(form.steps);
    if ("error" in converted) {
      setError(converted.error);
      return;
    }
    const request = {
      name: form.name.trim(),
      description: form.description.trim() || undefined,
      steps: converted.steps,
    };
    try {
      if (routing) await updateMutation.mutateAsync({ id: routing.id, request });
      else await createMutation.mutateAsync(request);
      onOpenChange(false);
    } catch (err) {
      setError(errorMessage(err));
    }
  };

  return (
    <FormDialog
      open={open}
      onOpenChange={onOpenChange}
      title={routing ? "Edit Routing" : "Create Routing"}
      description="The operations a part goes through, in order. Times are per part; quantity is set when a work order is created."
      onSubmit={handleSubmit}
      isSubmitting={isSubmitting}
      submitLabel={routing ? "Save Changes" : "Create Routing"}
      submittingLabel={routing ? undefined : "Creating..."}
      error={error}
      dirty={isDirty}
      size="lg"
    >
      <FormField htmlFor="routing-name" label="Name" required>
        <Input
          id="routing-name"
          placeholder="e.g., Bracket BR-100"
          value={form.name}
          onChange={(e) => setForm({ ...form, name: e.target.value })}
          disabled={isSubmitting}
          autoFocus={!routing}
        />
      </FormField>

      <FormField htmlFor="routing-description" label="Description">
        <Textarea
          id="routing-description"
          placeholder="Optional description"
          value={form.description}
          onChange={(e) => setForm({ ...form, description: e.target.value })}
          disabled={isSubmitting}
          rows={2}
        />
      </FormField>

      <div className="space-y-3">
        <div className="flex items-center justify-between">
          <Label>Steps</Label>
          <Button type="button" variant="outline" size="sm" onClick={addStep} disabled={isSubmitting}>
            <Plus className="h-4 w-4 mr-1" />
            Add step
          </Button>
        </div>

        {templates.length === 0 && (
          <p className="text-sm text-muted-foreground">
            No request templates yet. Create one per operation under Templates first.
          </p>
        )}

        <div className="space-y-2">
          {form.steps.map((step, index) => {
            const n = index + 1;
            return (
              <div
                key={index}
                data-testid={`routing-step-${n}`}
                className="grid grid-cols-[auto_1fr] gap-x-3 gap-y-2 rounded-lg border p-3 sm:grid-cols-[auto_minmax(0,2fr)_repeat(3,minmax(0,1fr))_auto] sm:items-end"
              >
                <span className="pb-2 text-sm font-medium text-muted-foreground">{n}.</span>

                <div className="space-y-1">
                  <Label htmlFor={`step-${n}-operation`} className="text-xs">Operation</Label>
                  <Select
                    value={step.operationTemplateId}
                    onValueChange={(v) => setStep(index, { operationTemplateId: v })}
                    disabled={isSubmitting}
                  >
                    <SelectTrigger id={`step-${n}-operation`}>
                      <SelectValue placeholder="Choose a template" />
                    </SelectTrigger>
                    <SelectContent>
                      {templates.map((t) => (
                        <SelectItem key={t.id} value={t.id}>
                          {t.name}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                </div>

                <div className="col-start-2 space-y-1 sm:col-start-auto">
                  <Label htmlFor={`step-${n}-setup`} className="text-xs">Setup (min)</Label>
                  <Input
                    id={`step-${n}-setup`}
                    inputMode="numeric"
                    value={step.setupMinutes}
                    onChange={(e) => setStep(index, { setupMinutes: e.target.value })}
                    disabled={isSubmitting}
                  />
                </div>

                <div className="col-start-2 space-y-1 sm:col-start-auto">
                  <Label htmlFor={`step-${n}-run`} className="text-xs">Run per unit (min)</Label>
                  <Input
                    id={`step-${n}-run`}
                    inputMode="numeric"
                    value={step.runMinutesPerUnit}
                    onChange={(e) => setStep(index, { runMinutesPerUnit: e.target.value })}
                    disabled={isSubmitting}
                  />
                </div>

                <div className="col-start-2 space-y-1 sm:col-start-auto">
                  <Label htmlFor={`step-${n}-lag`} className="text-xs">Lag after (min)</Label>
                  <Input
                    id={`step-${n}-lag`}
                    inputMode="numeric"
                    value={step.lagMinutesAfter}
                    onChange={(e) => setStep(index, { lagMinutesAfter: e.target.value })}
                    disabled={isSubmitting}
                  />
                </div>

                <div className="col-start-2 flex justify-end gap-1 sm:col-start-auto">
                  <Button
                    type="button"
                    variant="ghost"
                    size="icon"
                    onClick={() => moveStep(index, -1)}
                    disabled={isSubmitting || index === 0}
                    aria-label={`Move step ${n} up`}
                  >
                    <ArrowUp className="h-4 w-4" />
                  </Button>
                  <Button
                    type="button"
                    variant="ghost"
                    size="icon"
                    onClick={() => moveStep(index, 1)}
                    disabled={isSubmitting || index === form.steps.length - 1}
                    aria-label={`Move step ${n} down`}
                  >
                    <ArrowDown className="h-4 w-4" />
                  </Button>
                  <Button
                    type="button"
                    variant="ghost"
                    size="icon"
                    onClick={() => removeStep(index)}
                    disabled={isSubmitting}
                    aria-label={`Remove step ${n}`}
                  >
                    <Trash2 className="h-4 w-4 text-destructive" />
                  </Button>
                </div>
              </div>
            );
          })}
        </div>
      </div>
    </FormDialog>
  );
}
