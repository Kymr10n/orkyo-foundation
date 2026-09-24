import { useState } from 'react';
import { QrCode, ScanLine, X } from 'lucide-react';
import { QrScannerDialog } from '@foundation/src/components/scan/QrScannerDialog';
import { Button } from '@foundation/src/components/ui/button';
import { ConfirmDialog } from '@foundation/src/components/ui/ConfirmDialog';
import { ErrorAlert } from '@foundation/src/components/ui/ErrorAlert';
import { useCanEdit } from '@foundation/src/hooks/usePermissions';
import {
  useLinkResourceScanCode,
  useResourceScanCodes,
  useUnlinkResourceScanCode,
} from '@foundation/src/hooks/useResourceScanCodes';
import { lookupScanCode } from '@foundation/src/lib/api/resource-scan-codes-api';

interface ResourceScanCodesSectionProps {
  resourceId: string;
}

/** A code another resource carries, waiting for the user to confirm the move. */
interface PendingMove {
  code: string;
  /** Null when that resource's type has scanning off: the server does not name it. */
  ownerName: string | null;
}

/**
 * The QR stickers linked to one resource, inside its edit dialog
 * (docs/qr-resource-linking-spec.md §5.2). Every button is `type="button"`: the section
 * sits inside the dialog's form, and a plain button would submit it.
 */
export function ResourceScanCodesSection({ resourceId }: ResourceScanCodesSectionProps) {
  const canEdit = useCanEdit();
  const { data: codes = [], isLoading } = useResourceScanCodes(resourceId);
  const link = useLinkResourceScanCode();
  const unlink = useUnlinkResourceScanCode(resourceId);
  const [scannerOpen, setScannerOpen] = useState(false);
  const [pendingMove, setPendingMove] = useState<PendingMove | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const handleScan = async (code: string) => {
    setScannerOpen(false);
    setNotice(null);
    setError(null);
    try {
      const result = await lookupScanCode(code);
      if (result.status === 'unknown') {
        link.mutate({ resourceId, code });
      } else if (result.status === 'linked' && result.resource?.id === resourceId) {
        setNotice('This QR code is already linked to this resource.');
      } else {
        setPendingMove({ code, ownerName: result.resource?.name ?? null });
      }
    } catch {
      setError('The scanned code could not be checked. Try again.');
    }
  };

  return (
    <div className="space-y-3 border-t pt-4">
      <div className="flex items-center justify-between gap-2">
        <h3 className="text-sm font-medium">QR codes</h3>
        {canEdit && (
          <Button type="button" variant="outline" size="sm" onClick={() => setScannerOpen(true)} disabled={link.isPending}>
            <ScanLine className="mr-2 h-4 w-4" />
            Scan to link
          </Button>
        )}
      </div>

      <ErrorAlert message={error} />
      {notice && <p className="text-muted-foreground text-sm" role="status">{notice}</p>}

      {!isLoading && codes.length === 0 && (
        <p className="text-muted-foreground text-sm">No QR code is linked to this resource.</p>
      )}

      {codes.length > 0 && (
        <ul className="divide-y rounded-md border">
          {codes.map((c) => (
            <li key={c.id} className="flex items-center gap-2 px-3 py-2">
              <QrCode className="text-muted-foreground h-4 w-4 shrink-0" aria-hidden="true" />
              <span className="min-w-0 flex-1 truncate font-mono text-xs" title={c.code}>{c.code}</span>
              {canEdit && (
                <Button
                  type="button"
                  variant="ghost"
                  size="icon"
                  aria-label={`Remove QR code ${c.code}`}
                  onClick={() => unlink.mutate(c.id)}
                  disabled={unlink.isPending}
                >
                  <X className="h-4 w-4" />
                </Button>
              )}
            </li>
          ))}
        </ul>
      )}

      <QrScannerDialog
        open={scannerOpen}
        onOpenChange={setScannerOpen}
        title="Scan QR code"
        description="Point the camera at the sticker on this resource."
        onScan={(code) => void handleScan(code)}
      />

      <ConfirmDialog
        open={pendingMove !== null}
        onOpenChange={(open) => !open && setPendingMove(null)}
        title="Move QR code?"
        description={
          pendingMove?.ownerName
            ? `This QR code is linked to "${pendingMove.ownerName}". Move it to this resource?`
            : 'This QR code is linked to another resource. Move it to this resource?'
        }
        confirmLabel="Move link"
        isPending={link.isPending}
        onConfirm={async () => {
          if (!pendingMove) return;
          await link.mutateAsync({ resourceId, code: pendingMove.code, move: true }).catch(() => undefined);
          setPendingMove(null);
        }}
      />
    </div>
  );
}
