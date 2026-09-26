import { useState } from 'react';
import { toast } from 'sonner';
import { QrScannerDialog } from '@foundation/src/components/scan/QrScannerDialog';
import { Combobox, type ComboboxOption } from '@foundation/src/components/ui/combobox';
import { FormDialog } from '@foundation/src/components/ui/FormDialog';
import { Label } from '@foundation/src/components/ui/label';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@foundation/src/components/ui/select';
import { errorMessage } from '@foundation/src/hooks/mutation-utils';
import { useCanEdit } from '@foundation/src/hooks/usePermissions';
import {
  useLinkResourceScanCode,
  useScanCodeLookup,
  useScanLinkCandidates,
} from '@foundation/src/hooks/useResourceScanCodes';
import { useUiActionsStore } from '@foundation/src/store/ui-actions-store';

interface GlobalScanFlowProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

/** The type filter's "no filter" value. A type key is `[a-z][a-z0-9_]*`, so it cannot collide. */
const ALL_TYPES = '*';

/**
 * The top-bar Scan action (docs/qr-resource-linking-spec.md §5.3). A known code opens the
 * resource's status sheet. An unknown code lets an Editor link it to a resource on the spot,
 * which is the fast path for tagging a whole workshop.
 */
export function GlobalScanFlow({ open, onOpenChange }: GlobalScanFlowProps) {
  const canEdit = useCanEdit();
  const openResourceStatus = useUiActionsStore((s) => s.openResourceStatus);
  const lookup = useScanCodeLookup();
  const link = useLinkResourceScanCode();
  /** An unknown code an Editor is about to link. */
  const [linkCode, setLinkCode] = useState<string | null>(null);
  const [selectedId, setSelectedId] = useState('');
  const [typeFilter, setTypeFilter] = useState(ALL_TYPES);
  const { types, candidates } = useScanLinkCandidates(linkCode !== null);

  // A whole workshop is a long list; the type is how a person on the floor narrows it.
  const shown = typeFilter === ALL_TYPES ? candidates : candidates.filter((c) => c.typeKey === typeFilter);
  const options: ComboboxOption[] = shown.map((c) => ({
    id: c.id,
    // The site is part of the label, so the search box finds "break north" too.
    label: c.siteName ? `${c.name} (${c.typeName}) · ${c.siteName}` : `${c.name} (${c.typeName})`,
  }));
  const filterTypeName = types.find((t) => t.key === typeFilter)?.displayName;

  const changeTypeFilter = (next: string) => {
    setTypeFilter(next);
    // A selection the new filter hides would still be linked on submit; drop it instead.
    if (selectedId && next !== ALL_TYPES && !candidates.some((c) => c.id === selectedId && c.typeKey === next)) {
      setSelectedId('');
    }
  };

  const handleScan = (code: string) => {
    onOpenChange(false);
    lookup.mutate(code, {
      onSuccess: (result) => {
        if (result.status === 'linked' && result.resource) return openResourceStatus(result.resource.id);
        if (result.status === 'unknown' && canEdit) {
          setSelectedId('');
          setTypeFilter(ALL_TYPES);
          link.reset();
          return setLinkCode(code);
        }
        toast(
          result.status === 'unknown'
            ? 'This QR code is not linked to a resource.'
            : 'Scanning is off for this resource type.',
          { action: { label: 'Scan again', onClick: () => onOpenChange(true) } },
        );
      },
    });
  };

  return (
    <>
      <QrScannerDialog
        open={open}
        onOpenChange={onOpenChange}
        title="Scan QR code"
        description="Point the camera at the QR sticker on a resource."
        onScan={handleScan}
      />

      {linkCode !== null && (
        <FormDialog
          open
          onOpenChange={(next) => !next && setLinkCode(null)}
          title="Link QR code"
          description="This QR code is not linked to a resource yet. Select the resource that carries the sticker."
          error={link.error ? errorMessage(link.error) : null}
          isSubmitting={link.isPending}
          submitLabel="Link"
          submittingLabel="Linking…"
          submitDisabled={!selectedId}
          onSubmit={() =>
            link.mutate(
              { resourceId: selectedId, code: linkCode },
              {
                onSuccess: () => {
                  setLinkCode(null);
                  openResourceStatus(selectedId);
                },
              },
            )
          }
        >
          {/* One type to choose from is nothing to choose; the filter appears from two up. */}
          {types.length > 1 && (
            <div className="space-y-2">
              <Label htmlFor="scan-link-type">Resource type</Label>
              <Select value={typeFilter} onValueChange={changeTypeFilter}>
                <SelectTrigger id="scan-link-type">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value={ALL_TYPES}>All types</SelectItem>
                  {types.map((t) => (
                    <SelectItem key={t.key} value={t.key}>
                      {t.displayName}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
          )}
          <div className="space-y-2">
            <Label htmlFor="scan-link-resource">Resource</Label>
            <Combobox
              id="scan-link-resource"
              value={selectedId}
              onChange={setSelectedId}
              options={options}
              placeholder="Select a resource…"
              searchPlaceholder="Search resources…"
              emptyText={
                filterTypeName ? `No ${filterTypeName} resource found.` : 'No resource of a type with QR codes turned on.'
              }
            />
          </div>
        </FormDialog>
      )}
    </>
  );
}
