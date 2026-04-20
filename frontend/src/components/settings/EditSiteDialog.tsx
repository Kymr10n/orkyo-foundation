import {
    Dialog,
    DialogContent,
    DialogHeader,
    DialogTitle,
} from "@/components/ui/dialog";
import { ErrorAlert } from "@/components/ui/ErrorAlert";
import { DialogFormFooter } from "@/components/ui/DialogFormFooter";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import { useUpdateSite } from "@/hooks/useSites";
import type { Site } from "@/lib/api/site-api";
import { useEffect, useState } from "react";

interface EditSiteDialogProps {
  site: Site;
  open: boolean;
  onOpenChange: (open: boolean) => void;
  onSuccess: (site: Site) => void;
}

export function EditSiteDialog({
  site,
  open,
  onOpenChange,
  onSuccess,
}: EditSiteDialogProps) {
  const updateMutation = useUpdateSite();
  const [name, setName] = useState(site.name);
  const [description, setDescription] = useState(site.description || "");
  const [address, setAddress] = useState(site.address || "");
  const [error, setError] = useState<string | null>(null);
  const isSubmitting = updateMutation.isPending;

  useEffect(() => {
    if (open) {
      setName(site.name);
      setDescription(site.description || "");
      setAddress(site.address || "");
      setError(null);
    }
  }, [open, site]);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError(null);

    if (!name.trim()) {
      setError("Name is required");
      return;
    }

    try {
      const updatedSite = await updateMutation.mutateAsync({
        id: site.id,
        data: {
          code: site.code,  // Required by backend, must include existing code
          name: name.trim(),
          description: description.trim() || undefined,
          address: address.trim() || undefined,
        },
      });
      onSuccess(updatedSite);
      onOpenChange(false);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to update site");
    }
  };

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-[500px]">
        <DialogHeader>
          <DialogTitle>Edit Site</DialogTitle>
        </DialogHeader>

        <form onSubmit={handleSubmit}>
          <div className="space-y-4 py-4">
            {/* Code (read-only) */}
            <div className="space-y-2">
              <Label htmlFor="code">Code</Label>
              <Input
                id="code"
                value={site.code}
                disabled
                className="font-mono bg-muted"
              />
              <p className="text-xs text-muted-foreground">
                Code cannot be changed after creation
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
            onCancel={() => onOpenChange(false)}
            isSubmitting={isSubmitting}
            submitLabel="Save Changes"
          />
        </form>
      </DialogContent>
    </Dialog>
  );
}
