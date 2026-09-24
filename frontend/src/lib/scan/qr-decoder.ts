/**
 * The only module that loads the QR decoder (@zxing/browser). QrScannerDialog reaches it
 * through a dynamic import(), so the decoder stays out of the main chunk.
 *
 * QR only: a 1D barcode reader would also fire on labels that were never meant as Orkyo
 * codes (docs/qr-resource-linking-spec.md, §2).
 */
import { BrowserQRCodeReader } from '@zxing/browser';

export interface QrDecoderControls {
  stop: () => void;
  /** Present only when the active camera has a torch. */
  setTorch?: (on: boolean) => Promise<void>;
}

/**
 * Streams the rear camera into `video` and calls `onCode` with the text of each decoded QR
 * code. Rejects with the browser's DOMException when the camera cannot start.
 */
export async function startQrDecoder(
  video: HTMLVideoElement,
  onCode: (text: string) => void,
): Promise<QrDecoderControls> {
  const reader = new BrowserQRCodeReader();
  const controls = await reader.decodeFromConstraints(
    { audio: false, video: { facingMode: 'environment' } },
    video,
    (result) => {
      if (result) onCode(result.getText());
    },
  );
  return {
    stop: () => controls.stop(),
    setTorch: controls.switchTorch ? (on) => controls.switchTorch!(on) : undefined,
  };
}
