import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import type { Site } from "@/lib/api/site-api";
import { isValidSlug } from "@/lib/utils";
import { useState } from "react";
import { useCreateSite } from "@/hooks/useSites";
import { ErrorAlert } from "@/components/ui/ErrorAlert";
import { DialogFormFooter } from "@/components/ui/DialogFormFooter";

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

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
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
    <Dialog open={open} onOpenChange={handleOpenChange}>
      <DialogContent className="sm:max-w-[500px]">
        <DialogHeader>
          <DialogTitle>Create Site</DialogTitle>
        </DialogHeader>

        <form onSubmit={handleSubmit}>
          <div className="space-y-4 py-4">
            {/* Code */}
            <div className="space-y-2">
              <Label htmlFor="code">
                Code <span className="text-destructive">*</span>
              </Label>
              <Input
                id="code"
                placeholder="e.g., MAIN-01, WAREHOUSE-A"
                value={code}
                onChange={(e) => setCode(e.target.value)}
                disabled={isSubmitting}
                className="font-mono"
              />
              <p className="text-xs text-muted-foreground">
                Unique identifier (alphanumeric, underscores, hyphens only)
              </p>
            </div>

            {/* Name */}
            <div className="space-y-2">
              <Label htmlFor="name">
                Name <span className="text-destructive">*</span>
              </Label>
              <Input
                id="name"
                placeholder="e.g., Main Production Facility"
                value={name}
                onChange={(e) => setName(e.target.value)}
                disabled={isSubmitting}
              />
            </div>

            {/* Description */}
            <div className="space-y-2">
              <Label htmlFor="description">Description</Label>
              <Textarea
                id="description"
                placeholder="Optional description of the site"
                value={description}
                onChange={(e) => setDescription(e.target.value)}
                disabled={isSubmitting}
                rows={3}
              />
            </div>

            {/* Address */}
            <div className="space-y-2">
              <Label htmlFor="address">Address</Label>
              <Textarea
                id="address"
                placeholder="Physical address of the site"
                value={address}
                onChange={(e) => setAddress(e.target.value)}
                disabled={isSubmitting}
                rows={2}
              />
            </div>

            {/* Error Message */}
            <ErrorAlert message={error} />
          </div>

          <DialogFormFooter
            onCancel={() => handleOpenChange(false)}
            isSubmitting={isSubmitting}
            submitLabel="Create Site"
            submittingLabel="Creating..."
          />
        </form>
      </DialogContent>
    </Dialog>
  );
}
