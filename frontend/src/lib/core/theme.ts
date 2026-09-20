import { COOKIE_NAMES } from "../../constants/http";
import { STORAGE_KEYS } from "../../constants/storage";

export type ThemePreference = "dark" | "light" | "system";

/**
 * Persist the resolved theme in a cookie so server-rendered surfaces
 * (e.g. Keycloak login pages) can pick it up. Single owner of the cookie
 * format — also used by the layout store's theme actions.
 */
export function writeThemeCookie(resolved: "dark" | "light"): void {
  if (typeof document === "undefined") return;
  const isSecure = typeof location !== "undefined" && location.protocol === "https:";
  const secureFlag = isSecure ? ";Secure" : "";
  document.cookie = `${COOKIE_NAMES.THEME}=${resolved};path=/;max-age=31536000;SameSite=Lax${secureFlag}`;
}

/** Resolve "system" against the OS setting. */
export function resolveTheme(theme: ThemePreference): "dark" | "light" {
  if (theme === "dark" || theme === "light") return theme;
  if (typeof window !== "undefined" && window.matchMedia) {
    return window.matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light";
  }
  return "dark";
}

/** Put the resolved theme on the document and in the cookie. */
export function applyResolvedTheme(resolved: "dark" | "light"): void {
  if (typeof document === "undefined") return;
  document.documentElement.classList.toggle("dark", resolved === "dark");
  writeThemeCookie(resolved);
}

/**
 * The stored preference, read straight out of the layout store's persisted
 * envelope. `initTheme` runs before React — before any store module is even
 * imported — so it cannot go through the store itself, and the first paint must
 * not be a flash of the wrong theme. The shape is zustand `persist`'s
 * `{ state, version }`; anything else reads as no preference.
 */
function readStoredTheme(): ThemePreference {
  if (typeof localStorage === "undefined") return "system";
  try {
    const raw = localStorage.getItem(STORAGE_KEYS.LAYOUT);
    const theme: unknown = raw ? (JSON.parse(raw) as { state?: { theme?: unknown } }).state?.theme : null;
    return theme === "dark" || theme === "light" ? theme : "system";
  } catch {
    return "system";
  }
}

export function initTheme(): void {
  if (typeof document === "undefined") return;
  applyResolvedTheme(resolveTheme(readStoredTheme()));
}
