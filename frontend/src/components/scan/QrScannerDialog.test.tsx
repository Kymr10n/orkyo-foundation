import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QrScannerDialog } from './QrScannerDialog';

const decoder = vi.hoisted(() => ({
  startQrDecoder: vi.fn(),
  stop: vi.fn(),
  switchTorch: vi.fn(async () => {}),
  onCode: null as ((text: string) => void) | null,
}));

vi.mock('@foundation/src/lib/scan/qr-decoder', () => ({ startQrDecoder: decoder.startQrDecoder }));

function setEnvironment({ secure = true, camera = true } = {}) {
  Object.defineProperty(window, 'isSecureContext', { value: secure, configurable: true });
  Object.defineProperty(navigator, 'mediaDevices', {
    value: camera ? { getUserMedia: vi.fn() } : undefined,
    configurable: true,
  });
}

function renderScanner(onScan = vi.fn(), onOpenChange = vi.fn()) {
  render(
    <QrScannerDialog open onOpenChange={onOpenChange} title="Scan QR code" description="Point the camera." onScan={onScan} />,
  );
  return { onScan, onOpenChange };
}

describe('QrScannerDialog', () => {
  beforeEach(() => {
    setEnvironment();
    decoder.onCode = null;
    decoder.startQrDecoder.mockImplementation(async (_video: HTMLVideoElement, onCode: (text: string) => void) => {
      decoder.onCode = onCode;
      return { stop: decoder.stop, switchTorch: decoder.switchTorch };
    });
  });

  afterEach(() => vi.clearAllMocks());

  it('starts the decoder and hands the first code over once, with the camera already off', async () => {
    const { onScan } = renderScanner();
    await waitFor(() => expect(decoder.onCode).not.toBeNull());

    decoder.onCode!('https://vendor.example/asset/42');
    decoder.onCode!('second read of the same frame');

    expect(onScan).toHaveBeenCalledTimes(1);
    expect(onScan).toHaveBeenCalledWith('https://vendor.example/asset/42');
    expect(decoder.stop).toHaveBeenCalled();
  });

  it('stops the camera when the dialog closes', async () => {
    const { rerender } = render(<QrScannerDialog open onOpenChange={vi.fn()} title="Scan" onScan={vi.fn()} />);
    await waitFor(() => expect(decoder.onCode).not.toBeNull());

    rerender(<QrScannerDialog open={false} onOpenChange={vi.fn()} title="Scan" onScan={vi.fn()} />);

    expect(decoder.stop).toHaveBeenCalled();
  });

  it('stops a camera that finishes starting after the dialog closed', async () => {
    let resolve!: (controls: { stop: () => void }) => void;
    decoder.startQrDecoder.mockImplementation(() => new Promise((r) => { resolve = r; }));
    const { unmount } = render(<QrScannerDialog open onOpenChange={vi.fn()} title="Scan" onScan={vi.fn()} />);
    await waitFor(() => expect(decoder.startQrDecoder).toHaveBeenCalled());

    unmount();
    resolve({ stop: decoder.stop });

    await waitFor(() => expect(decoder.stop).toHaveBeenCalled());
  });

  it('toggles the torch when the camera has one', async () => {
    const user = userEvent.setup();
    renderScanner();

    const torch = await screen.findByRole('button', { name: 'Torch', pressed: false });
    await user.click(torch);

    expect(decoder.switchTorch).toHaveBeenCalledWith(true);
    await waitFor(() => expect(torch).toHaveAttribute('aria-pressed', 'true'));
  });

  it('offers no torch when the camera has none', async () => {
    decoder.startQrDecoder.mockResolvedValue({ stop: decoder.stop });
    renderScanner();

    await waitFor(() => expect(screen.queryByText('Starting camera…')).not.toBeInTheDocument());
    expect(screen.queryByRole('button', { name: /Torch/ })).not.toBeInTheDocument();
  });

  it('says that scanning needs HTTPS, and never touches the camera', () => {
    setEnvironment({ secure: false });
    renderScanner();

    expect(screen.getByText(/Scanning needs HTTPS/)).toBeInTheDocument();
    expect(decoder.startQrDecoder).not.toHaveBeenCalled();
  });

  it('says so when the browser has no camera API', () => {
    setEnvironment({ camera: false });
    renderScanner();

    expect(screen.getByText('This browser cannot use a camera.')).toBeInTheDocument();
  });

  it.each([
    ['NotAllowedError', /Camera access is blocked/],
    ['NotFoundError', /No camera found/],
    ['NotReadableError', /in use by another application/],
    ['AbortError', /could not start/],
  ])('explains a camera that fails with %s', async (name, expected) => {
    decoder.startQrDecoder.mockRejectedValue(new DOMException('x', name));
    renderScanner();

    expect(await screen.findByText(expected)).toBeInTheDocument();
  });

  it('closes on Cancel', async () => {
    const user = userEvent.setup();
    const { onOpenChange } = renderScanner();

    await user.click(screen.getByRole('button', { name: 'Cancel' }));

    expect(onOpenChange).toHaveBeenCalledWith(false);
  });
});
