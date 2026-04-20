import { useAuth } from "@/contexts/AuthContext";
import { SettingsPageHeader } from "./SettingsPageHeader";
import { AlertCircle, Loader2 } from "lucide-react";
import { Alert, AlertDescription } from "@/components/ui/alert";
import { useQuery } from "@tanstack/react-query";
import { getSecurityInfo } from "@/lib/api/security-api";
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
    queryKey: ["security-info"],
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

  return (
    <div className="space-y-6">
      <SettingsPageHeader
        title="Security"
        description="Manage your password, multi-factor authentication, and active sessions."
      />

      <PasswordSection
        isFederated={securityInfo?.isFederated ?? false}
        identityProvider={securityInfo?.identityProvider}
      />
      <MfaSection />
      <SessionsSection onLogoutAll={() => send({ type: "LOGOUT" })} />
    </div>
  );
}
