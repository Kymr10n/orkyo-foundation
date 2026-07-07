import { FormDialog } from "@foundation/src/components/ui/FormDialog";
import { FormField } from "@foundation/src/components/ui/FormField";
import { Input } from "@foundation/src/components/ui/input";
import { Textarea } from "@foundation/src/components/ui/textarea";
import type { Site } from "@foundation/src/lib/api/site-api";
import { isValidSlug } from "@foundation/src/lib/utils";
import { useState } from "react";
import { useCreateSite } from "@foundation/src/hooks/useSites";

interface CreateSiteDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  onSuccess: (site: Site) => void;
}

export function CreateSiteDialog({
  open,
  onOpenChange,
  onSuccess,
}: CreateSiteDialogProps) {
  const createMutation = useCreateSite();
  const [code, setCode] = useState("");
  const [name, setName] = useState("");
  const [description, setDescription] = useState("");
  const [address, setAddress] = useState("");
  const [error, setError] = useState<string | null>(null);
  const isSubmitting = createMutation.isPending;

  const resetForm = () => {
    setCode("");
    setName("");
    setDescription("");
    setAddress("");
    setError(null);
  };

  const handleSubmit = async () => {
    setError(null);

    // Validation
    if (!code.trim()) {
      setError("Code is required");
      return;
    }

    if (!isValidSlug(code)) {
      setError(
        "Code must contain only alphanumeric characters, underscores, and hyphens",
      );
      return;
    }

    if (!name.trim()) {
      setError("Name is required");
      return;
    }

    try {
      const newSite = await createMutation.mutateAsync({
        code: code.trim(),
        name: name.trim(),
        description: description.trim() || undefined,
        address: address.trim() || undefined,
      });
      onSuccess(newSite);
      resetForm();
      onOpenChange(false);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to create site");
    }
  };

  const handleOpenChange = (newOpen: boolean) => {
    if (!newOpen && !isSubmitting) {
      resetForm();
    }
    onOpenChange(newOpen);
  };

  return (
    <FormDialog
      open={open}
      onOpenChange={handleOpenChange}
      title="Create Site"
      onSubmit={handleSubmit}
      isSubmitting={isSubmitting}
      submitLabel="Create Site"
      submittingLabel="Creating..."
      error={error}
      dirty={!!(code || name || description || address)}
    >
      <FormField
        htmlFor="code"
        label="Code"
        required
        help="Unique identifier (alphanumeric, underscores, hyphens only)"
      >
        <Input
          id="code"
          placeholder="e.g., MAIN-01, WAREHOUSE-A"
          value={code}
          onChange={(e) => setCode(e.target.value)}
          disabled={isSubmitting}
          className="font-mono"
        />
      </FormField>

      <FormField htmlFor="name" label="Name" required>
        <Input
          id="name"
          placeholder="e.g., Main Production Facility"
          value={name}
          onChange={(e) => setName(e.target.value)}
          disabled={isSubmitting}
        />
      </FormField>

      <FormField htmlFor="description" label="Description">
        <Textarea
          id="description"
          placeholder="Optional description of the site"
          value={description}
          onChange={(e) => setDescription(e.target.value)}
          disabled={isSubmitting}
          rows={3}
        />
      </FormField>

      <FormField htmlFor="address" label="Address">
        <Textarea
          id="address"
          placeholder="Physical address of the site"
          value={address}
          onChange={(e) => setAddress(e.target.value)}
          disabled={isSubmitting}
          rows={2}
        />
      </FormField>
    </FormDialog>
  );
}
