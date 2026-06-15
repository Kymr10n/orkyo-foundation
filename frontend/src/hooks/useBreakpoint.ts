import { useSyncExternalStore } from "react";

/**
 * The single source of truth for responsive device class across the app.
 *
 * Three classes aligned to Tailwind's default stops — never introduce
 * page-local breakpoint numbers; branch on this hook (or plain `md:`/`xl:`
 * utilities) instead:
 *   - `phone`   < 768px        (below `md`)
 *   - `tablet`  768px – 1279px (`md` … below `xl`)
 *   - `desktop` >= 1280px      (`xl`+)
 */
export type Breakpoint = "phone" | "tablet" | "desktop";

/** Tablet starts at Tailwind `md`. */
const TABLET_MIN_PX = 768;
/** Desktop starts at Tailwind `xl`. */
const DESKTOP_MIN_PX = 1280;

const DESKTOP_QUERY = `(min-width: ${DESKTOP_MIN_PX}px)`;
const TABLET_QUERY = `(min-width: ${TABLET_MIN_PX}px)`;

function getSnapshot(): Breakpoint {
  if (typeof window === "undefined" || typeof window.matchMedia !== "function") {
    return "desktop";
  }
  if (window.matchMedia(DESKTOP_QUERY).matches) return "desktop";
  if (window.matchMedia(TABLET_QUERY).matches) return "tablet";
  return "phone";
}

function subscribe(callback: () => void): () => void {
  if (typeof window === "undefined" || typeof window.matchMedia !== "function") {
    return () => {};
  }
  const queries = [window.matchMedia(DESKTOP_QUERY), window.matchMedia(TABLET_QUERY)];
  queries.forEach((mql) => mql.addEventListener("change", callback));
  return () => queries.forEach((mql) => mql.removeEventListener("change", callback));
}

export interface BreakpointState {
  device: Breakpoint;
  isPhone: boolean;
  isTablet: boolean;
  isDesktop: boolean;
}

/**
 * Subscribe to the current device class. Re-renders only when the class
 * actually changes (crossing the 768px / 1280px boundaries), not on every
 * resize. SSR-safe: defaults to `desktop` before hydration.
 */
export function useBreakpoint(): BreakpointState {
  const device = useSyncExternalStore(subscribe, getSnapshot, () => "desktop" as const);
  return {
    device,
    isPhone: device === "phone",
    isTablet: device === "tablet",
    isDesktop: device === "desktop",
  };
}
