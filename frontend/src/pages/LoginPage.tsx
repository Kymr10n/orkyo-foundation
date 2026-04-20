/**
 * LoginPage — redirect-only login entry point.
 *
 * This page is NOT a traditional UI page. Per the auth architecture (Section 8),
 * it immediately triggers the BFF OIDC flow on mount.
 *
 * The only time UI is shown is when a login error occurred (e.g. the BFF
 * redirected back with ?error=identity_link_failed). In that case, the machine
 * stores the error in context and this page displays it with a retry button.
 *
 * Rendering contexts:
 * - ApexGateway renders it when authStage === 'unauthenticated' (error case)
 * - TenantApp /login route (session recovery on tenant subdomain)
 */

import { useAuth, debugAuth } from "@/contexts/AuthContext";
import { useEffect, useRef } from "react";
import { useNavigate } from "react-router-dom";
import { AlertTriangle, RefreshCw } from "lucide-react";
import { Button } from "@/components/ui/button";
import { LoadingSpinner } from "@/components/ui/LoadingSpinner";
import { AUTH_MESSAGES } from "@/constants/auth";

export function LoginPage() {
  const { isAuthenticated, isLoading, login, error } = useAuth();
  const navigate = useNavigate();
  const loginAttempted = useRef(false);

  useEffect(() => {
    debugAuth("LoginPage mount");
  }, []);

  // Redirect-only: if no error, immediately start the BFF OIDC flow.
  // If already authenticated, navigate to the app root instead.
  useEffect(() => {
    if (isLoading) return;
    if (isAuthenticated) {
      navigate("/");
      return;
    }
    if (error || loginAttempted.current) return;
    loginAttempted.current = true;
    login();
  }, [isLoading, isAuthenticated, error, login, navigate]);

  if (isLoading) {
    return <LoadingSpinner message={AUTH_MESSAGES.REDIRECTING_LOGIN} />;
  }

  // Error case: show the error with a retry button
  if (error) {
    return (
      <div className="flex items-center justify-center min-h-screen bg-background p-4">
        <div className="flex flex-col items-center gap-4 text-center max-w-md">
          <AlertTriangle className="h-12 w-12 text-destructive" />
          <h1 className="text-xl font-semibold">
            {AUTH_MESSAGES.AUTH_ERROR_TITLE}
          </h1>
          <p className="text-muted-foreground text-sm">{error}</p>
          <Button onClick={() => login()}>
            <RefreshCw className="mr-2 h-4 w-4" />
            Try again
          </Button>
        </div>
      </div>
    );
  }

  // No error, not authenticated — login() was called, waiting for redirect
  return <LoadingSpinner message={AUTH_MESSAGES.REDIRECTING_LOGIN} />;
}
