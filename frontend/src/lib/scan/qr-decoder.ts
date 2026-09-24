/**
 * The only module that loads the QR decoder (@zxing/browser). QrScannerDialog reaches it
 * through a dynamic import(), so the decoder stays out of the main chunk.
 *
 * QR only: a 1D barcode reader would also fire on labels that were never meant as Orkyo
 * codes (docs/qr-resource-linking-spec.md, §2).
 */
import { BrowserQRCodeReader, type IScannerControls } from '@zxing/browser';

/** `switchTorch` is present only when the active camera has a torch. */
export type QrDecoderControls = Pick<IScannerControls, 'stop' | 'switchTorch'>;

/**
 * Streams the rear camera into `video` and calls `onCode` with the text of each decoded QR
 * code. Rejects with the browser's DOMException when the camera cannot start.
 */
export function startQrDecoder(video: HTMLVideoElement, onCode: (text: string) => void): Promise<QrDecoderControls> {
  return new BrowserQRCodeReader().decodeFromConstraints(
    { audio: false, video: { facingMode: 'environment' } },
    video,
    (result) => {
      if (result) onCode(result.getText());
    },
  );
}
