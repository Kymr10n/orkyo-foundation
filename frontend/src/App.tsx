/**
 * App — Entry point with domain-based rendering split.
 *
 * Three modes:
 * - Apex domain (orkyo.com)     → ApexGateway (auth pipeline, with React Router)
 * - Tenant subdomain            → TenantApp (standard SPA with React Router)
 * - Local dev (no baseDomain)   → LocalDevShell (ApexGateway for pipeline,
 *                                  TenantApp when ready — zero route duplication)
 *
 * The auth pipeline is always driven by ApexGateway's state machine — there is
 * no duplicate routing controller. SPA routes are defined once in TenantApp.
 */

import { BrowserRouter } from "react-router-dom";
import { AuthProvider, useAuth, debugAuth } from "./contexts/AuthContext";
import { ApexGateway } from "./components/auth/ApexGateway";
import { TenantApp } from "./components/auth/TenantApp";
import { getCurrentSubdomain } from "./lib/utils/tenant-navigation";
import { runtimeConfig } from "./config/runtime";
import { ThemeToggle } from "./components/layout/ThemeToggle";
import { AUTH_STAGES } from "./constants/auth";

/**
 * LocalDevShell — single state machine for local dev.
 *
 * When `authStage !== 'ready'`, renders ApexGateway (the same component used
 * on the apex domain in production). When `authStage === 'ready'`, mounts a
 * BrowserRouter with TenantApp — the same route tree used on tenant subdomains.
 *
 * This means SPA routes are defined in exactly one place (TenantApp).
 */
function LocalDevShell() {
  const { authStage } = useAuth();

  if (authStage !== AUTH_STAGES.READY) {
    return (
      <>
        <ThemeToggle variant="floating" />
        <ApexGateway />
      </>
    );
  }

  return <TenantApp />;
}

function App() {
  debugAuth('App render');

  const isLocalDev = !runtimeConfig.baseDomain;

  // Local dev: ApexGateway for pipeline, TenantApp when ready
  if (isLocalDev) {
    return (
      <BrowserRouter>
        <AuthProvider>
          <LocalDevShell />
        </AuthProvider>
      </BrowserRouter>
    );
  }

  const subdomain = getCurrentSubdomain();

  // Apex domain: auth pipeline + BrowserRouter (AdminPage uses useNavigate)
  if (!subdomain) {
    return (
      <BrowserRouter>
        <AuthProvider>
          <ThemeToggle variant="floating" />
          <ApexGateway />
        </AuthProvider>
      </BrowserRouter>
    );
  }

  // Tenant subdomain: standard SPA
  return (
    <BrowserRouter>
      <AuthProvider>
        <TenantApp />
      </AuthProvider>
    </BrowserRouter>
  );
}

export default App;
