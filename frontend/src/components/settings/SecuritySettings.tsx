import { useAuth } from "@foundation/src/contexts/AuthContext";
import { AlertCircle, Loader2, Lock } from "lucide-react";
import { Alert, AlertDescription, AlertTitle } from "@foundation/src/components/ui/alert";
import { useQuery } from "@tanstack/react-query";
import { getSecurityInfo } from "@foundation/src/lib/api/security-api";
import { qk } from "@foundation/src/lib/api/query-keys";
import { PasswordSection } from "./PasswordSection";
import { MfaSection } from "./MfaSection";
import { SessionsSection } from "./SessionsSection";

export function SecuritySettings() {
  const { send } = useAuth();

  const {
    data: securityInfo,
    isLoading: securityInfoLoading,
    error: securityInfoError,
  } = useQuery({
    queryKey: qk.security.info(),
    queryFn: getSecurityInfo,
  });

  if (securityInfoLoading) {
    return (
      <div className="flex items-center justify-center py-12">
        <Loader2 className="h-8 w-8 animate-spin text-muted-foreground" />
      </div>
    );
  }

  if (securityInfoError) {
    return (
      <Alert variant="destructive">
        <AlertCircle className="h-4 w-4" />
        <AlertDescription>
          Failed to load security settings. Please try again.
        </AlertDescription>
      </Alert>
    );
  }

  const accountLocked = securityInfo?.accountLocked ?? false;

  return (
    <div className="space-y-6">
      {accountLocked && (
        <Alert>
          <Lock className="h-4 w-4" />
          <AlertTitle>Shared demo account</AlertTitle>
          <AlertDescription>
            This is a shared demo account. Its password, email, profile, and two-factor
            settings cannot be changed.
          </AlertDescription>
        </Alert>
      )}
      <PasswordSection
        isFederated={securityInfo?.isFederated ?? false}
        identityProvider={securityInfo?.identityProvider}
        locked={accountLocked}
      />
      <MfaSection locked={accountLocked} />
      <SessionsSection onLogoutAll={() => send({ type: "LOGOUT" })} />
    </div>
  );
}
