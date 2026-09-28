import { FormDialog } from "@foundation/src/components/ui/FormDialog";
import { FormField } from "@foundation/src/components/ui/FormField";
import { Input } from "@foundation/src/components/ui/input";
import { Label } from "@foundation/src/components/ui/label";
import { Textarea } from "@foundation/src/components/ui/textarea";
import { useSaveSite } from "@foundation/src/hooks/useSites";
import { useEntityFormDialog } from "@foundation/src/hooks/useEntityFormDialog";
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

/** Rules the dialog states as a message rather than a disabled Save button. */
function validate(form: FormState): string | null {
  if (!form.code.trim()) return "Code is required";
  if (!isValidSlug(form.code)) {
    return "Code must contain only alphanumeric characters, underscores, and hyphens";
  }
  if (!form.name.trim()) return "Name is required";
  return null;
}

export function SiteEditDialog({ site, open, onOpenChange, onSaved }: SiteEditDialogProps) {
  const mutation = useSaveSite();
  const { form, setForm, isDirty, error, submit, isSubmitting } = useEntityFormDialog({
    open,
    onOpenChange,
    entity: site,
    emptyForm: () => empty,
    toForm: fromSite,
    mutation,
    validate,
    toVariables: (f: FormState, s: Site | null) => {
      const data = {
        code: f.code.trim(),
        name: f.name.trim(),
        description: f.description.trim() || undefined,
        address: f.address.trim() || undefined,
      };
      return s ? { id: s.id, data } : { id: null, data };
    },
    onSaved,
  });

  return (
    <FormDialog
      open={open}
      onOpenChange={onOpenChange}
      title={site ? "Edit Site" : "Create Site"}
      onSubmit={submit}
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
