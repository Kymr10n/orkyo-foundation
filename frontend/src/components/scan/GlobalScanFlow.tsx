import { useState } from 'react';
import { QrScannerDialog } from '@foundation/src/components/scan/QrScannerDialog';
import { Button } from '@foundation/src/components/ui/button';
import { Combobox } from '@foundation/src/components/ui/combobox';
import { ErrorAlert } from '@foundation/src/components/ui/ErrorAlert';
import { FormDialog } from '@foundation/src/components/ui/FormDialog';
import { Label } from '@foundation/src/components/ui/label';
import { ScrollableDialogBody } from '@foundation/src/components/ui/dialog';
import { errorMessage } from '@foundation/src/hooks/mutation-utils';
import { useCanEdit } from '@foundation/src/hooks/usePermissions';
import { useLinkResourceScanCode, useScanLinkCandidates } from '@foundation/src/hooks/useResourceScanCodes';
import { lookupScanCode } from '@foundation/src/lib/api/resource-scan-codes-api';
import { useUiActionsStore } from '@foundation/src/store/ui-actions-store';

interface GlobalScanFlowProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

/** What the last scan found, when it did not simply open a resource. */
type Outcome =
  | { kind: 'unknown'; code: string }
  | { kind: 'type_disabled' }
  | { kind: 'error' };

const MESSAGES = {
  unknown: 'This QR code is not linked to a resource.',
  type_disabled: 'Scanning is off for this resource type.',
  error: 'The scanned code could not be checked. Try again.',
} as const;

/**
 * The top-bar Scan action (docs/qr-resource-linking-spec.md §5.3). A known code opens the
 * resource's status sheet. An unknown code lets an Editor link it to a resource on the spot,
 * which is the fast path for tagging a whole workshop.
 */
export function GlobalScanFlow({ open, onOpenChange }: GlobalScanFlowProps) {
  const canEdit = useCanEdit();
  const openResourceStatus = useUiActionsStore((s) => s.openResourceStatus);
  const link = useLinkResourceScanCode({ inlineErrors: true });
  const [outcome, setOutcome] = useState<Outcome | null>(null);
  const [selectedId, setSelectedId] = useState('');
  const [linkError, setLinkError] = useState<string | null>(null);
  const linking = outcome?.kind === 'unknown' && canEdit;
  const candidates = useScanLinkCandidates(linking);

  const handleScan = async (code: string) => {
    onOpenChange(false);
    setSelectedId('');
    setLinkError(null);
    try {
      const result = await lookupScanCode(code);
      if (result.status === 'linked' && result.resource) {
        setOutcome(null);
        openResourceStatus(result.resource.id);
      } else {
        setOutcome(result.status === 'unknown' ? { kind: 'unknown', code } : { kind: 'type_disabled' });
      }
    } catch {
      setOutcome({ kind: 'error' });
    }
  };

  const scanAgain = () => {
    setOutcome(null);
    onOpenChange(true);
  };

  return (
    <>
      <QrScannerDialog
        open={open}
        onOpenChange={onOpenChange}
        title="Scan QR code"
        description="Point the camera at the QR sticker on a resource."
        onScan={(code) => void handleScan(code)}
      />

      {linking && outcome.kind === 'unknown' && (
        <FormDialog
          open
          onOpenChange={(next) => !next && setOutcome(null)}
          title="Link QR code"
          description="This QR code is not linked to a resource yet. Select the resource that carries the sticker."
          error={linkError}
          isSubmitting={link.isPending}
          submitLabel="Link"
          submittingLabel="Linking…"
          submitDisabled={!selectedId}
          onSubmit={async () => {
            setLinkError(null);
            try {
              await link.mutateAsync({ resourceId: selectedId, code: outcome.code });
              setOutcome(null);
              openResourceStatus(selectedId);
            } catch (e) {
              setLinkError(errorMessage(e));
            }
          }}
        >
          <div className="space-y-2">
            <Label htmlFor="scan-link-resource">Resource</Label>
            <Combobox
              id="scan-link-resource"
              value={selectedId}
              onChange={setSelectedId}
              options={candidates}
              placeholder="Select a resource…"
              searchPlaceholder="Search resources…"
              emptyText="No resource of a type with QR codes turned on."
            />
          </div>
        </FormDialog>
      )}

      {outcome && !linking && (
        <FormDialog
          footer={null}
          open
          onOpenChange={(next) => !next && setOutcome(null)}
          title="Scan result"
        >
          <ScrollableDialogBody className="space-y-4 px-6 pb-6">
            {outcome.kind === 'error' ? (
              <ErrorAlert message={MESSAGES.error} />
            ) : (
              <p className="text-sm">{MESSAGES[outcome.kind]}</p>
            )}
            {/* The dialog's own X closes it; the one action here is the likely next step. */}
            <div className="flex justify-end">
              <Button onClick={scanAgain}>Scan again</Button>
            </div>
          </ScrollableDialogBody>
        </FormDialog>
      )}
    </>
  );
}
