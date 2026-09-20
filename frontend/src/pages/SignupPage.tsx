import { useState } from "react";
import { LoadingSpinner } from "@foundation/src/components/ui/LoadingSpinner";
import { formatDateDisplay } from "@foundation/src/lib/formatters";
import { useSearchParams } from "react-router";
import { Button } from "@foundation/src/components/ui/button";
import { Input } from "@foundation/src/components/ui/input";
import { Label } from "@foundation/src/components/ui/label";
import { buildBffLoginUrl } from "@foundation/src/lib/utils/tenant-navigation";
import { ArrowLeft, UserPlus, CheckCircle, AlertCircle, Clock } from "lucide-react";
import { Alert, AlertDescription } from "@foundation/src/components/ui/alert";
import { usePageTitle } from "@foundation/src/hooks/usePageTitle";
import { errorMessage } from "@foundation/src/hooks/mutation-utils";
import {
  useAcceptInvitation,
  useInvitationValidation,
} from "@foundation/src/hooks/useInvitationSignup";

export function SignupPage() {
  usePageTitle("Accept invitation");
  const [searchParams] = useSearchParams();
  const invitationToken = searchParams.get("invitation");

  const validation = useInvitationValidation(invitationToken);
  const invitation = validation.data ?? null;
  const isValidating = invitationToken !== null && validation.isPending;
  const validationError = !invitationToken
    ? "No invitation token provided"
    : validation.error
      ? validation.error instanceof Error
        ? validation.error.message
        : "Failed to validate invitation"
      : null;

  const acceptMutation = useAcceptInvitation();
  const isLoading = acceptMutation.isPending;

  const [submitted, setSubmitted] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [formData, setFormData] = useState({
    displayName: "",
    password: "",
    confirmPassword: "",
  });

  const handleSubmit = (e: React.SyntheticEvent<HTMLFormElement>) => {
    e.preventDefault();
    setError(null);

    if (formData.password !== formData.confirmPassword) {
      setError("Passwords do not match");
      return;
    }

    if (formData.password.length < 8) {
      setError("Password must be at least 8 characters");
      return;
    }

    acceptMutation.mutate(
      {
        token: invitationToken as string,
        displayName: formData.displayName || invitation?.email.split("@")[0],
        password: formData.password,
      },
      {
        onSuccess: () => setSubmitted(true),
        onError: (err) => setError(errorMessage(err)),
      },
    );
  };

  const handleBackToLogin = (withHint = false) => {
    if (withHint) {
      // After successful account creation: go directly to the BFF so Keycloak
      // gets a fresh auth request, with the email pre-filled (login_hint).
      // returnTo must NOT be "/" — in dev the Vite marketing middleware serves
      // marketing/index.html for "/" (no React), and in prod nginx does the same.
      // "/login?auto=1" bypasses both and loads the React SPA, which then lets
      // the auth machine bootstrap the fresh session and route into the app.
      window.location.href = buildBffLoginUrl({
        returnTo: `${window.location.origin}/login?auto=1`,
        loginHint: invitation?.email,
      });
    } else {
      // Error cases ("Go to Sign In", "Already have an account?") — navigate
      // to /login and let the auth machine handle things. If the user is already
      // authenticated (e.g. admin testing same-browser) the app renders normally;
      // if not, LoginPage triggers the BFF flow.
      window.location.href = "/login?auto=1";
    }
  };

  // Loading state while validating invitation
  if (isValidating) {
    return (
      <LoadingSpinner message="Validating invitation…" />
    );
  }

  // Invalid or expired invitation
  if (validationError) {
    return (
      <div className="flex items-center justify-center min-h-screen bg-background p-4">
        <div className="w-full max-w-md space-y-6 text-center">
          <div className="flex justify-center">
            <AlertCircle className="h-16 w-16 text-destructive" />
          </div>
          <h1 className="text-2xl font-bold tracking-tight">
            Invalid Invitation
          </h1>
          <p className="text-muted-foreground">{validationError}</p>
          <Button
            onClick={() => handleBackToLogin()}
            variant="outline"
            className="mt-4"
          >
            <ArrowLeft className="mr-2 h-4 w-4" />
            Go to Sign In
          </Button>
        </div>
      </div>
    );
  }

  // Successfully created account
  if (submitted) {
    return (
      <div className="flex items-center justify-center min-h-screen bg-background p-4">
        <div className="w-full max-w-md space-y-6 text-center">
          <div className="flex justify-center">
            <CheckCircle className="h-16 w-16 text-green-500" />
          </div>
          <h1 className="text-2xl font-bold tracking-tight">
            Account Created!
          </h1>
          <p className="text-muted-foreground">
            Your account has been created successfully. You can now sign in with
            your credentials.
          </p>
          <Button onClick={() => handleBackToLogin(true)} className="mt-4">
            <ArrowLeft className="mr-2 h-4 w-4" />
            Sign In
          </Button>
        </div>
      </div>
    );
  }

  // Account creation form
  return (
    <div className="flex items-center justify-center min-h-screen bg-background p-4">
      <div className="w-full max-w-md space-y-6">
        <div className="text-center space-y-2">
          <h1 className="text-2xl font-bold tracking-tight">
            Accept Invitation
          </h1>
          <p className="text-sm text-muted-foreground">
            You've been invited to join{" "}
            <strong>{invitation?.tenantName}</strong>
          </p>
        </div>

        <div className="bg-muted/50 rounded-lg p-4 space-y-2">
          <div className="flex items-center gap-2 text-sm">
            <UserPlus className="h-4 w-4 text-muted-foreground" />
            <span className="text-muted-foreground">Email:</span>
            <span className="font-medium">{invitation?.email}</span>
          </div>
          <div className="flex items-center gap-2 text-sm">
            <Clock className="h-4 w-4 text-muted-foreground" />
            <span className="text-muted-foreground">Expires:</span>
            <span className="font-medium">
              {invitation?.expiresAt && formatDateDisplay(invitation.expiresAt)}
            </span>
          </div>
        </div>

        <form onSubmit={handleSubmit} className="space-y-4">
          {error && (
            <Alert variant="destructive">
              <AlertCircle className="h-4 w-4" />
              <AlertDescription>{error}</AlertDescription>
            </Alert>
          )}

          <div className="space-y-2">
            <Label htmlFor="displayName">Display Name (optional)</Label>
            <Input
              id="displayName"
              type="text"
              placeholder="How should we call you?"
              value={formData.displayName}
              onChange={(e) =>
                setFormData({ ...formData, displayName: e.target.value })
              }
            />
          </div>

          <div className="space-y-2">
            <Label htmlFor="password">Password</Label>
            <Input
              id="password"
              type="password"
              placeholder="Create a password"
              value={formData.password}
              onChange={(e) =>
                setFormData({ ...formData, password: e.target.value })
              }
              required
            />
          </div>

          <div className="space-y-2">
            <Label htmlFor="confirmPassword">Confirm Password</Label>
            <Input
              id="confirmPassword"
              type="password"
              placeholder="Confirm your password"
              value={formData.confirmPassword}
              onChange={(e) =>
                setFormData({ ...formData, confirmPassword: e.target.value })
              }
              required
            />
          </div>

          <Button type="submit" className="w-full" loading={isLoading} disabled={isLoading}>
            {isLoading ? (
              "Creating Account..."
            ) : (
              <>
                <UserPlus className="mr-2 h-4 w-4" />
                Create Account
              </>
            )}
          </Button>
        </form>

        <div className="text-center">
          <Button variant="link" onClick={() => handleBackToLogin()}>
            Already have an account? Sign in
          </Button>
        </div>
      </div>
    </div>
  );
}
