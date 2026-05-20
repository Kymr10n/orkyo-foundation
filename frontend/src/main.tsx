import "@fontsource-variable/inter";
import { QueryClientProvider } from "@tanstack/react-query";
import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import App from "./App";
import "./index.css";
import { TrackingProvider } from "./infrastructure/tracking";
import { queryClient } from "./lib/core/query-client";
import { initRUM } from "./lib/core/rum";
import { initTheme } from "./lib/core/theme";

initTheme();
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
