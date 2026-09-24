import { useEffect, useEffectEvent, useRef, useState } from 'react';
import { Flashlight, FlashlightOff } from 'lucide-react';
import { Button } from '@foundation/src/components/ui/button';
import { ErrorAlert } from '@foundation/src/components/ui/ErrorAlert';
import { FormDialog } from '@foundation/src/components/ui/FormDialog';
import { ScrollableDialogBody } from '@foundation/src/components/ui/dialog';
import type { QrDecoderControls } from '@foundation/src/lib/scan/qr-decoder';

interface QrScannerDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  title: string;
  description?: string;
  /** Called once with the decoded text. The camera is already off; the caller decides what follows. */
  onScan: (code: string) => void;
}

/** The camera error, as one sentence a user can act on. */
export function cameraErrorMessage(error: unknown): string {
  const name = error instanceof Error || error instanceof DOMException ? error.name : '';
  if (name === 'NotAllowedError' || name === 'SecurityError')
    return 'Camera access is blocked. Allow the camera for this site in the browser settings, then try again.';
  if (name === 'NotFoundError' || name === 'OverconstrainedError') return 'No camera found on this device.';
  if (name === 'NotReadableError') return 'The camera is in use by another application.';
  return 'The camera could not start.';
}

/** Why scanning cannot start on this page at all, or null when it can. */
function unsupportedReason(): string | null {
  if (!window.isSecureContext) return 'Scanning needs HTTPS. Open Orkyo over a secure connection to use the camera.';
  if (!navigator.mediaDevices?.getUserMedia) return 'This browser cannot use a camera.';
  return null;
}

/**
 * Live camera view that decodes one QR code (docs/qr-resource-linking-spec.md §5.1).
 * The decoder loads only when the dialog opens. The camera stops at the first code and
 * whenever the dialog closes.
 */
export function QrScannerDialog({ open, onOpenChange, title, description, onScan }: QrScannerDialogProps) {
  return (
    <FormDialog footer={null} open={open} onOpenChange={onOpenChange} title={title} description={description}>
      {/* Dialog content unmounts on close, so every open starts a fresh camera session. */}
      <ScannerView onScan={onScan} onCancel={() => onOpenChange(false)} />
    </FormDialog>
  );
}

function ScannerView({ onScan, onCancel }: { onScan: (code: string) => void; onCancel: () => void }) {
  const videoRef = useRef<HTMLVideoElement>(null);
  const controlsRef = useRef<QrDecoderControls | null>(null);
  const [unsupported] = useState(unsupportedReason);
  const [cameraError, setCameraError] = useState<string | null>(null);
  const [starting, setStarting] = useState(unsupported === null);
  const [torchAvailable, setTorchAvailable] = useState(false);
  const [torchOn, setTorchOn] = useState(false);
  const emitScan = useEffectEvent((code: string) => onScan(code));
  const error = unsupported ?? cameraError;

  useEffect(() => {
    if (unsupported) return;
    let cancelled = false;
    let done = false;
    const stop = () => {
      controlsRef.current?.stop();
      controlsRef.current = null;
    };

    void (async () => {
      try {
        const video = videoRef.current;
        if (!video) return;
        const { startQrDecoder } = await import('@foundation/src/lib/scan/qr-decoder');
        if (cancelled) return;
        const controls = await startQrDecoder(video, (text) => {
          if (done) return;
          done = true;
          stop();
          emitScan(text);
        });
        if (cancelled || done) {
          controls.stop();
          return;
        }
        controlsRef.current = controls;
        setTorchAvailable(controls.setTorch !== undefined);
      } catch (e) {
        if (!cancelled) setCameraError(cameraErrorMessage(e));
      } finally {
        if (!cancelled) setStarting(false);
      }
    })();

    return () => {
      cancelled = true;
      stop();
    };
  }, [unsupported]);

  const toggleTorch = async () => {
    const next = !torchOn;
    await controlsRef.current?.setTorch?.(next);
    setTorchOn(next);
  };

  return (
    <ScrollableDialogBody className="space-y-4 px-6 pb-6">
      <ErrorAlert message={error} />
      {!error && (
        <div className="relative overflow-hidden rounded-md bg-black">
          <video
            ref={videoRef}
            className="aspect-[3/4] w-full object-cover sm:aspect-video"
            muted
            playsInline
            aria-label="Camera view"
          />
          <div
            aria-hidden="true"
            className="pointer-events-none absolute inset-[15%] rounded-lg border-2 border-white/80"
          />
          {starting && (
            <p role="status" className="absolute inset-x-0 bottom-3 text-center text-sm text-white">
              Starting camera…
            </p>
          )}
        </div>
      )}
      {!error && <p className="text-muted-foreground text-sm">Hold the QR code inside the frame.</p>}
      <div className="flex justify-end gap-2">
        {torchAvailable && (
          <Button variant="outline" onClick={() => void toggleTorch()}>
            {torchOn ? <FlashlightOff className="mr-2 h-4 w-4" /> : <Flashlight className="mr-2 h-4 w-4" />}
            {torchOn ? 'Torch off' : 'Torch on'}
          </Button>
        )}
        <Button variant="outline" onClick={onCancel}>
          Cancel
        </Button>
      </div>
    </ScrollableDialogBody>
  );
}
