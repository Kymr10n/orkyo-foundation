import { COOKIE_NAMES } from "../../constants/http";
import { STORAGE_KEYS } from "../../constants/storage";

/**
 * Persist the resolved theme in a cookie so server-rendered surfaces
 * (e.g. Keycloak login pages) can pick it up. Single owner of the cookie
 * format — also used by the app store's theme actions.
 */
export function writeThemeCookie(resolved: "dark" | "light"): void {
  if (typeof document === "undefined") return;
  const isSecure = typeof location !== "undefined" && location.protocol === "https:";
  const secureFlag = isSecure ? ";Secure" : "";
  document.cookie = `${COOKIE_NAMES.THEME}=${resolved};path=/;max-age=31536000;SameSite=Lax${secureFlag}`;
}

export function initTheme(): void {
  if (typeof document === "undefined") return;
  const stored = localStorage.getItem(STORAGE_KEYS.THEME) || "system";
  const isDark =
    stored === "system"
      ? window.matchMedia("(prefers-color-scheme: dark)").matches
      : stored === "dark";
  document.documentElement.classList.toggle("dark", isDark);
  writeThemeCookie(isDark ? "dark" : "light");
}
