import { useState } from "react";
import {
  KeyRound,
  Shield,
  ShieldCheck,
  ShieldOff,
  Trash2,
  AlertCircle,
  Loader2,
} from "lucide-react";
import { Button } from "@/components/ui/button";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { Alert, AlertDescription } from "@/components/ui/alert";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { getMfaStatus, removeMfa, enableMfa } from "@/lib/api/security-api";
import { formatDistanceToNow } from "date-fns";

export function MfaSection() {
  const queryClient = useQueryClient();
  const [removeMfaOpen, setRemoveMfaOpen] = useState(false);

  const { data: mfaStatus, isLoading: mfaLoading } = useQuery({
    queryKey: ["mfa-status"],
    queryFn: getMfaStatus,
  });

  const removeMfaMutation = useMutation({
    mutationFn: removeMfa,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["mfa-status"] });
      setRemoveMfaOpen(false);
    },
  });

  const enableMfaMutation = useMutation({
    mutationFn: enableMfa,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["mfa-status"] });
    },
  });

  return (
    <>
      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-2">
            <KeyRound className="h-5 w-5" />
            Two-Factor Authentication (MFA)
          </CardTitle>
          <CardDescription>
            Protect your account with time-based one-time passwords (TOTP)
          </CardDescription>
        </CardHeader>
        <CardContent>
          {mfaLoading ? (
            <div className="flex items-center justify-center py-4">
              <Loader2 className="h-6 w-6 animate-spin text-muted-foreground" />
            </div>
          ) : mfaStatus?.totpEnabled ? (
            <div className="space-y-4">
              <div className="flex items-center justify-between p-3 rounded-lg border bg-card">
                <div className="flex items-center gap-3">
                  <div className="p-2 rounded-full bg-green-100 dark:bg-green-900">
                    <ShieldCheck className="h-4 w-4 text-green-600 dark:text-green-400" />
                  </div>
                  <div>
                    <div className="flex items-center gap-2">
                      <span className="font-medium text-sm">
                        TOTP Authenticator
                      </span>
                      <Badge
                        variant="secondary"
                        className="text-xs bg-green-100 text-green-800 dark:bg-green-900 dark:text-green-200"
                      >
                        Enabled
                      </Badge>
                    </div>
                    <div className="text-xs text-muted-foreground">
                      {mfaStatus.totpLabel || "Authenticator app"}
                      {mfaStatus.totpCreatedDate && (
                        <>
                          {" "}
                          • Configured{" "}
                          {formatDistanceToNow(
                            new Date(mfaStatus.totpCreatedDate),
                            { addSuffix: true },
                          )}
                        </>
                      )}
                    </div>
                  </div>
                </div>
                <Button
                  variant="ghost"
                  size="sm"
                  className="text-destructive hover:text-destructive"
                  onClick={() => setRemoveMfaOpen(true)}
                >
                  <Trash2 className="h-4 w-4 mr-1" />
                  Remove
                </Button>
              </div>
              {mfaStatus.recoveryCodesConfigured && (
                <div className="flex items-center gap-2 text-sm text-muted-foreground">
                  <Shield className="h-4 w-4" />
                  Recovery codes configured
                </div>
              )}
            </div>
          ) : (
            <div className="space-y-3">
              <div className="flex items-center gap-3 p-3 rounded-lg border bg-muted/50">
                <div className="p-2 rounded-full bg-amber-100 dark:bg-amber-900">
                  <ShieldOff className="h-4 w-4 text-amber-600 dark:text-amber-400" />
                </div>
                <div>
                  <span className="font-medium text-sm">
                    MFA not configured
                  </span>
                  <p className="text-xs text-muted-foreground">
                    Add an extra layer of security to your account with TOTP
                  </p>
                </div>
              </div>
              {enableMfaMutation.isSuccess ? (
                <Alert>
                  <ShieldCheck className="h-4 w-4" />
                  <AlertDescription className="text-green-600">
                    MFA enrollment enabled! Log out and log back in to set up
                    your authenticator app.
                  </AlertDescription>
                </Alert>
              ) : (
                <>
                  <Button
                    onClick={() => enableMfaMutation.mutate()}
                    disabled={enableMfaMutation.isPending}
                  >
                    {enableMfaMutation.isPending ? (
                      <Loader2 className="h-4 w-4 mr-2 animate-spin" />
                    ) : (
                      <ShieldCheck className="h-4 w-4 mr-2" />
                    )}
                    Enable MFA
                  </Button>
                  {enableMfaMutation.isError && (
                    <Alert variant="destructive">
                      <AlertCircle className="h-4 w-4" />
                      <AlertDescription>
                        {enableMfaMutation.error?.message ||
                          "Failed to enable MFA"}
                      </AlertDescription>
                    </Alert>
                  )}
                </>
              )}
            </div>
          )}
        </CardContent>
      </Card>

      <Dialog open={removeMfaOpen} onOpenChange={setRemoveMfaOpen}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Remove Two-Factor Authentication?</DialogTitle>
            <DialogDescription>
              This will remove your TOTP authenticator and recovery codes. You
              will be prompted to set up MFA again on your next login.
            </DialogDescription>
          </DialogHeader>
          {removeMfaMutation.isError && (
            <Alert variant="destructive">
              <AlertCircle className="h-4 w-4" />
              <AlertDescription>
                {removeMfaMutation.error?.message || "Failed to remove MFA"}
              </AlertDescription>
            </Alert>
          )}
          <DialogFooter>
            <Button variant="outline" onClick={() => setRemoveMfaOpen(false)}>
              Cancel
            </Button>
            <Button
              variant="destructive"
              onClick={() => removeMfaMutation.mutate()}
              disabled={removeMfaMutation.isPending}
            >
              {removeMfaMutation.isPending && (
                <Loader2 className="h-4 w-4 mr-2 animate-spin" />
              )}
              Remove MFA
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </>
  );
}
