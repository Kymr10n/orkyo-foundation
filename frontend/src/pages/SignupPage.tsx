import { useState, useEffect } from "react";
import { useSearchParams } from "react-router-dom";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { navigateToApex } from "@/lib/utils/tenant-navigation";
import {
  ArrowLeft,
  UserPlus,
  CheckCircle,
  Loader2,
  AlertCircle,
  Clock,
} from "lucide-react";
import { Alert, AlertDescription } from "@/components/ui/alert";
import { API_BASE_URL } from "@/lib/core/api-utils";
import { API_PATHS } from "@/lib/core/api-paths";

interface InvitationDetails {
  email: string;
  expiresAt: string;
  tenantName: string;
}

export function SignupPage() {
  const [searchParams] = useSearchParams();
  const invitationToken = searchParams.get("invitation");

  const [isValidating, setIsValidating] = useState(true);
  const [invitation, setInvitation] = useState<InvitationDetails | null>(null);
  const [validationError, setValidationError] = useState<string | null>(null);

  const [submitted, setSubmitted] = useState(false);
  const [isLoading, setIsLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [formData, setFormData] = useState({
    displayName: "",
    password: "",
    confirmPassword: "",
  });

  // Validate invitation token on mount
  useEffect(() => {
    async function validateInvitation() {
      if (!invitationToken) {
        setValidationError("No invitation token provided");
        setIsValidating(false);
        return;
      }

      try {
        const response = await fetch(
          `${API_BASE_URL}${API_PATHS.INVITATION_VALIDATE}?token=${encodeURIComponent(invitationToken)}`,
        );

        const data = await response.json();

        if (!response.ok) {
          setValidationError(data.error || "Invalid invitation");
          setIsValidating(false);
          return;
        }

        setInvitation(data);
      } catch {
        setValidationError("Failed to validate invitation");
      } finally {
        setIsValidating(false);
      }
    }

    validateInvitation();
  }, [invitationToken]);

  const handleSubmit = async (e: React.FormEvent) => {
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

    setIsLoading(true);

    try {
      const response = await fetch(
        `${API_BASE_URL}${API_PATHS.INVITATION_ACCEPT}`,
        {
          method: "POST",
          headers: {
            "Content-Type": "application/json",
          },
          body: JSON.stringify({
            token: invitationToken,
            displayName:
              formData.displayName || invitation?.email.split("@")[0],
            password: formData.password,
          }),
        },
      );

      const data = await response.json();

      if (!response.ok) {
        setError(data.error || data.detail || "Failed to create account");
        return;
      }

      setSubmitted(true);
    } catch {
      setError("Network error. Please try again.");
    } finally {
      setIsLoading(false);
    }
  };

  const handleBackToLogin = () => {
    if (!navigateToApex("/")) window.location.href = "/";
  };

  // Loading state while validating invitation
  if (isValidating) {
    return (
      <div className="flex items-center justify-center min-h-screen bg-background p-4">
        <div className="w-full max-w-md space-y-6 text-center">
          <Loader2 className="h-12 w-12 animate-spin mx-auto text-primary" />
          <p className="text-muted-foreground">Validating invitation...</p>
        </div>
      </div>
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
            onClick={handleBackToLogin}
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
          <Button onClick={handleBackToLogin} className="mt-4">
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
              {invitation?.expiresAt &&
                new Date(invitation.expiresAt).toLocaleDateString()}
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

          <Button type="submit" className="w-full" disabled={isLoading}>
            {isLoading ? (
              <>
                <Loader2 className="mr-2 h-4 w-4 animate-spin" />
                Creating Account...
              </>
            ) : (
              <>
                <UserPlus className="mr-2 h-4 w-4" />
                Create Account
              </>
            )}
          </Button>
        </form>

        <div className="text-center">
          <Button variant="link" onClick={handleBackToLogin}>
            Already have an account? Sign in
          </Button>
        </div>
      </div>
    </div>
  );
}
