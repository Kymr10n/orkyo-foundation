import { beforeEach, describe, expect, it, vi } from 'vitest';
import { startQrDecoder } from './qr-decoder';

const zxing = vi.hoisted(() => ({
  decodeFromConstraints: vi.fn(),
  stop: vi.fn(),
  switchTorch: vi.fn(async () => {}),
}));

vi.mock('@zxing/browser', () => ({
  BrowserQRCodeReader: class {
    decodeFromConstraints = zxing.decodeFromConstraints;
  },
}));

describe('startQrDecoder', () => {
  beforeEach(() => vi.clearAllMocks());

  it('asks for the rear camera and reports only frames that hold a code', async () => {
    const video = document.createElement('video');
    const onCode = vi.fn();
    zxing.decodeFromConstraints.mockImplementation(async (_c, _v, callback) => {
      callback(undefined);
      callback({ getText: () => 'STICKER-1' });
      return { stop: zxing.stop, switchTorch: zxing.switchTorch };
    });

    const controls = await startQrDecoder(video, onCode);

    expect(zxing.decodeFromConstraints).toHaveBeenCalledWith(
      { audio: false, video: { facingMode: 'environment' } },
      video,
      expect.any(Function),
    );
    expect(onCode).toHaveBeenCalledTimes(1);
    expect(onCode).toHaveBeenCalledWith('STICKER-1');

    await controls.switchTorch!(true);
    controls.stop();
    expect(zxing.switchTorch).toHaveBeenCalledWith(true);
    expect(zxing.stop).toHaveBeenCalled();
  });

  it('offers no torch when the camera has none', async () => {
    zxing.decodeFromConstraints.mockResolvedValue({ stop: zxing.stop });

    const controls = await startQrDecoder(document.createElement('video'), vi.fn());

    expect(controls.switchTorch).toBeUndefined();
  });
});
