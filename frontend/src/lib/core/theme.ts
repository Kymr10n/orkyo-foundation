import { COOKIE_NAMES } from "../../constants/http";
import { STORAGE_KEYS } from "../../constants/storage";

export function initTheme(): void {
  if (typeof document === "undefined") return;
  const stored = localStorage.getItem(STORAGE_KEYS.THEME) || "system";
  const isDark =
    stored === "system"
      ? window.matchMedia("(prefers-color-scheme: dark)").matches
      : stored === "dark";
  document.documentElement.classList.toggle("dark", isDark);
  document.cookie = `${COOKIE_NAMES.THEME}=${isDark ? "dark" : "light"};path=/;max-age=31536000;SameSite=Lax`;
}
