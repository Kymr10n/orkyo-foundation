import { useState } from 'react';
import { FormDialog } from '@foundation/src/components/ui/FormDialog';
import { Label } from '@foundation/src/components/ui/label';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@foundation/src/components/ui/select';
import type { ResourceInfo } from '@foundation/src/lib/api/resources-api';
import { useSites } from '@foundation/src/hooks/useSites';
import { useMoveResourceSite } from '@foundation/src/hooks/useResources';
import { errorMessage } from '@foundation/src/hooks/mutation-utils';

interface MoveResourceSiteDialogProps {
  resource: ResourceInfo;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

/**
 * One-field shortcut for the Home Site change the full edit dialog buries below the fold.
 * Sends only `homeSiteId`; the backend leaves every absent field alone. Mounted per resource
 * by the list, so the form state seeds once from the row it was opened for.
 */
export function MoveResourceSiteDialog({ resource, open, onOpenChange }: MoveResourceSiteDialogProps) {
  const { data: sites = [] } = useSites();
  const currentSiteId = resource.homeSiteId ?? '';
  const [siteId, setSiteId] = useState(currentSiteId);
  const [error, setError] = useState<string | null>(null);

  const move = useMoveResourceSite(resource);

  const submit = () =>
    move.mutate(
      { siteId, siteName: sites.find((s) => s.id === siteId)?.name ?? '' },
      {
        onSuccess: () => onOpenChange(false),
        onError: (err) => setError(errorMessage(err)),
      },
    );

  return (
    <FormDialog
      open={open}
      onOpenChange={onOpenChange}
      size="sm"
      title={`Move "${resource.name}" to another site`}
      description="Only the home site changes. Existing assignments are kept."
      onSubmit={submit}
      isSubmitting={move.isPending}
      submitLabel="Move"
      submitDisabled={siteId === '' || siteId === currentSiteId}
      error={error}
    >
      <div className="space-y-2">
        <Label htmlFor="move-site">Site</Label>
        <Select value={siteId} onValueChange={setSiteId}>
          <SelectTrigger id="move-site">
            <SelectValue placeholder="Select a site" />
          </SelectTrigger>
          <SelectContent>
            {sites.map((s) => (
              <SelectItem key={s.id} value={s.id}>
                {s.name}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>
    </FormDialog>
  );
}
