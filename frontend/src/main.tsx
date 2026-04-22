import "@fontsource-variable/inter";
import { QueryClientProvider } from "@tanstack/react-query";
import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import App from "./App";
import "./index.css";
import { TrackingProvider } from "./infrastructure/tracking";
import { queryClient } from "./lib/core/query-client";
import { initRUM } from "./lib/core/rum";
import { STORAGE_KEYS } from "./constants/storage";
import { COOKIE_NAMES } from "./constants/http";

// Apply theme on load and sync cookie for Keycloak
if (typeof document !== "undefined") {
  const stored = localStorage.getItem(STORAGE_KEYS.THEME) || "system";
  let isDark: boolean;
  if (stored === "system") {
    isDark = window.matchMedia("(prefers-color-scheme: dark)").matches;
  } else {
    isDark = stored === "dark";
  }
  document.documentElement.classList.toggle("dark", isDark);
  document.cookie = `${COOKIE_NAMES.THEME}=${isDark ? "dark" : "light"};path=/;max-age=31536000;SameSite=Lax`;
}

// Initialize Real User Monitoring (Web Vitals + long tasks)
initRUM();

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <TrackingProvider>
      <QueryClientProvider client={queryClient}>
        <App />
      </QueryClientProvider>
    </TrackingProvider>
  </StrictMode>,
);
