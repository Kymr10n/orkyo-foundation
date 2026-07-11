import { describe, it, expect, vi } from "vitest";
import { renderHook, act } from "@testing-library/react";
import { useDialogDirtyGuard } from "./useDialogDirtyGuard";

describe("useDialogDirtyGuard", () => {
  it("starts with confirmOpen false, and a close attempt while dirty opens the confirm without calling onOpenChange", () => {
    const onOpenChange = vi.fn();
    const { result } = renderHook(() =>
      useDialogDirtyGuard({ isDirty: true, onOpenChange }),
    );
    expect(result.current.confirmOpen).toBe(false);

    act(() => result.current.guardedOnOpenChange(false));

    expect(result.current.confirmOpen).toBe(true);
    expect(onOpenChange).not.toHaveBeenCalled();
  });

  it("ignores a second close attempt while the confirm is already open", () => {
    const onOpenChange = vi.fn();
    const { result } = renderHook(() =>
      useDialogDirtyGuard({ isDirty: true, onOpenChange }),
    );

    act(() => result.current.guardedOnOpenChange(false));
    expect(result.current.confirmOpen).toBe(true);

    act(() => result.current.guardedOnOpenChange(false));

    expect(onOpenChange).not.toHaveBeenCalled();
    expect(result.current.confirmOpen).toBe(true);
  });

  it("closes directly when not dirty, without opening the confirm", () => {
    const onOpenChange = vi.fn();
    const { result } = renderHook(() =>
      useDialogDirtyGuard({ isDirty: false, onOpenChange }),
    );

    act(() => result.current.guardedOnOpenChange(false));

    expect(onOpenChange).toHaveBeenCalledWith(false);
    expect(result.current.confirmOpen).toBe(false);
  });

  it("always passes open=true through to onOpenChange", () => {
    const onOpenChange = vi.fn();
    const { result } = renderHook(() =>
      useDialogDirtyGuard({ isDirty: true, onOpenChange }),
    );

    act(() => result.current.guardedOnOpenChange(true));

    expect(onOpenChange).toHaveBeenCalledWith(true);
  });
});
