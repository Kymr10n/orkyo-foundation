import { useEffect, useState } from "react";
import { FormDialog } from "@foundation/src/components/ui/FormDialog";
import { FormField } from "@foundation/src/components/ui/FormField";
import { Input } from "@foundation/src/components/ui/input";
import { Label } from "@foundation/src/components/ui/label";
import { Textarea } from "@foundation/src/components/ui/textarea";
import { useCreateSite, useUpdateSite } from "@foundation/src/hooks/useSites";
import type { Site } from "@foundation/src/lib/api/site-api";
import { isValidSlug } from "@foundation/src/lib/utils";

interface SiteEditDialogProps {
  site: Site | null;
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** Optional: invoked with the saved entity on successful create or update. */
  onSaved?: (site: Site) => void;
}

interface FormState {
  code: string;
  name: string;
  description: string;
  address: string;
}

const empty: FormState = { code: "", name: "", description: "", address: "" };

function fromSite(site: Site): FormState {
  return {
    code: site.code,
    name: site.name,
    description: site.description ?? "",
    address: site.address ?? "",
  };
}

export function SiteEditDialog({ site, open, onOpenChange, onSaved }: SiteEditDialogProps) {
  const [form, setForm] = useState<FormState>(empty);
  // Snapshot of the form as last synced; the dirty guard compares against it.
  const [baseline, setBaseline] = useState<FormState>(empty);
  const [error, setError] = useState<string | null>(null);

  const createMutation = useCreateSite();
  const updateMutation = useUpdateSite();
  const isSubmitting = site ? updateMutation.isPending : createMutation.isPending;

  useEffect(() => {
    if (!open) return;
    setError(null);
    const next = site ? fromSite(site) : empty;
    setForm(next);
    setBaseline(next);
  }, [site, open]);

  const isDirty = JSON.stringify(form) !== JSON.stringify(baseline);

  const handleSubmit = async () => {
    setError(null);

    if (!form.code.trim()) {
      setError("Code is required");
      return;
    }
    if (!isValidSlug(form.code)) {
      setError("Code must contain only alphanumeric characters, underscores, and hyphens");
      return;
    }
    if (!form.name.trim()) {
      setError("Name is required");
      return;
    }

    try {
      const data = {
        code: form.code.trim(),
        name: form.name.trim(),
        description: form.description.trim() || undefined,
        address: form.address.trim() || undefined,
      };
      const saved = site
        ? await updateMutation.mutateAsync({ id: site.id, data })
        : await createMutation.mutateAsync(data);
      onSaved?.(saved);
      onOpenChange(false);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to save site");
    }
  };

  return (
    <FormDialog
      open={open}
      onOpenChange={onOpenChange}
      title={site ? "Edit Site" : "Create Site"}
      onSubmit={handleSubmit}
      isSubmitting={isSubmitting}
      submitLabel={site ? "Save Changes" : "Create Site"}
      submittingLabel={site ? undefined : "Creating..."}
      error={error}
      dirty={isDirty}
    >
      {site ? (
        <div className="space-y-2">
          <Label htmlFor="code">Code</Label>
          <Input id="code" value={form.code} disabled className="font-mono bg-muted" />
          <p className="text-xs text-muted-foreground">Code cannot be changed after creation</p>
        </div>
      ) : (
        <FormField
          htmlFor="code"
          label="Code"
          required
          help="Unique identifier (alphanumeric, underscores, hyphens only)"
        >
          <Input
            id="code"
            placeholder="e.g., MAIN-01, WAREHOUSE-A"
            value={form.code}
            onChange={(e) => setForm({ ...form, code: e.target.value })}
            disabled={isSubmitting}
            className="font-mono"
          />
        </FormField>
      )}

      <FormField htmlFor="name" label="Name" required>
        <Input
          id="name"
          placeholder="e.g., Main Production Facility"
          value={form.name}
          onChange={(e) => setForm({ ...form, name: e.target.value })}
          disabled={isSubmitting}
          autoFocus={!site}
        />
      </FormField>

      <FormField htmlFor="description" label="Description">
        <Textarea
          id="description"
          placeholder="Optional description of the site"
          value={form.description}
          onChange={(e) => setForm({ ...form, description: e.target.value })}
          disabled={isSubmitting}
          rows={3}
        />
      </FormField>

      <FormField htmlFor="address" label="Address">
        <Textarea
          id="address"
          placeholder="Physical address of the site"
          value={form.address}
          onChange={(e) => setForm({ ...form, address: e.target.value })}
          disabled={isSubmitting}
          rows={2}
        />
      </FormField>
    </FormDialog>
  );
}
