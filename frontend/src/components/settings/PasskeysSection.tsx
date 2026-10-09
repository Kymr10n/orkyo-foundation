import { useState } from "react";
import { AlertCircle, FingerprintPattern, Pencil, Plus, Shield, Trash2 } from "lucide-react";
import { LoadingSpinner } from "@foundation/src/components/ui/LoadingSpinner";
import { Button } from "@foundation/src/components/ui/button";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@foundation/src/components/ui/card";
import { Alert, AlertDescription } from "@foundation/src/components/ui/alert";
import { Input } from "@foundation/src/components/ui/input";
import { Label } from "@foundation/src/components/ui/label";
import { ConfirmDialog } from "@foundation/src/components/ui/ConfirmDialog";
import {
  useMfaStatus,
  usePasskeys,
  useRemovePasskey,
  useRenamePasskey,
} from "@foundation/src/hooks/useSecuritySettings";
import type { Passkey } from "@foundation/src/lib/api/security-api";
import { buildBffLoginUrl } from "@foundation/src/lib/utils/tenant-navigation";
import { formatDistanceToNow } from "date-fns";

/** Same limit as the server's DomainLimits.PasskeyLabelMaxLength. */
const LABEL_MAX_LENGTH = 64;

interface PasskeysSectionProps {
  /** When true (shared/locked identity, e.g. the demo account), hide the passkey actions. */
  locked?: boolean;
}

/**
 * Adding a passkey is Keycloak's own registration step: the browser leaves for the BFF login
 * with `kc_action`, Keycloak registers the passkey, and the BFF returns here with
 * `kc_action_status`, which AccountPage turns into a toast.
 */
function startAddPasskey() {
  window.location.replace(
    buildBffLoginUrl({
      returnTo: `${window.location.origin}/account?tab=security`,
      kcAction: "webauthn-register-passwordless",
    }),
  );
}

export function PasskeysSection({ locked = false }: PasskeysSectionProps = {}) {
  const { data: passkeys, isLoading } = usePasskeys();
  // Removal re-checks the password, and for a TOTP user Keycloak also needs the code.
  const { data: mfaStatus } = useMfaStatus();
  const needsCode = mfaStatus?.totpEnabled ?? false;

  const [renaming, setRenaming] = useState<Passkey | null>(null);
  const [label, setLabel] = useState("");
  const renameMutation = useRenamePasskey();

  const [removing, setRemoving] = useState<Passkey | null>(null);
  const [currentPassword, setCurrentPassword] = useState("");
  const [currentCode, setCurrentCode] = useState("");
  const removeMutation = useRemovePasskey();
  const codeValid = !needsCode || /^\d{6}$/.test(currentCode);

  const openRename = (passkey: Passkey | null) => {
    setRenaming(passkey);
    setLabel(passkey?.label ?? "");
    if (!passkey) renameMutation.reset();
  };

  // Closing the dialog forgets the typed password, the code and the last failure.
  const openRemove = (passkey: Passkey | null) => {
    setRemoving(passkey);
    if (!passkey) {
      setCurrentPassword("");
      setCurrentCode("");
      removeMutation.reset();
    }
  };

  return (
    <>
      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-2">
            <FingerprintPattern className="h-5 w-5" />
            Passkeys
          </CardTitle>
          <CardDescription>
            Sign in with your fingerprint, face or device PIN instead of your password
          </CardDescription>
        </CardHeader>
        <CardContent>
          {locked ? (
            <Alert>
              <Shield className="h-4 w-4" />
              <AlertDescription>
                Passkeys are disabled for the shared demo account.
              </AlertDescription>
            </Alert>
          ) : isLoading ? (
            <LoadingSpinner size="sm" muted fullScreen={false} className="py-4" />
          ) : (
            <div className="space-y-3">
              {passkeys && passkeys.length > 0 ? (
                <ul className="space-y-2">
                  {passkeys.map((passkey) => (
                    <li
                      key={passkey.id}
                      className="flex items-center justify-between gap-3 p-3 rounded-lg border bg-card"
                    >
                      <div className="min-w-0">
                        <div className="font-medium text-sm truncate">
                          {passkey.label || "Passkey"}
                        </div>
                        {passkey.createdDate && (
                          <div className="text-xs text-muted-foreground">
                            Added{" "}
                            {formatDistanceToNow(new Date(passkey.createdDate), { addSuffix: true })}
                          </div>
                        )}
                      </div>
                      <div className="flex shrink-0 gap-1">
                        <Button
                          variant="ghost"
                          size="sm"
                          aria-label={`Rename ${passkey.label || "passkey"}`}
                          onClick={() => openRename(passkey)}
                        >
                          <Pencil className="h-4 w-4" />
                        </Button>
                        <Button
                          variant="ghost"
                          size="sm"
                          className="text-destructive hover:text-destructive"
                          onClick={() => openRemove(passkey)}
                        >
                          <Trash2 className="h-4 w-4 mr-1" />
                          Remove
                        </Button>
                      </div>
                    </li>
                  ))}
                </ul>
              ) : (
                <p className="text-sm text-muted-foreground">
                  No passkeys yet. A passkey replaces your password and skips the authenticator
                  code.
                </p>
              )}
              <Button variant="outline" onClick={startAddPasskey}>
                <Plus className="h-4 w-4 mr-2" />
                Add a passkey
              </Button>
            </div>
          )}
        </CardContent>
      </Card>

      <ConfirmDialog
        open={renaming !== null}
        onOpenChange={(open) => !open && openRename(null)}
        title="Rename passkey"
        description="Choose a name that tells you which device holds this passkey."
        confirmLabel="Save"
        isPending={renameMutation.isPending}
        confirmDisabled={!label.trim()}
        onConfirm={() => {
          if (!renaming) return;
          renameMutation.mutate(
            { id: renaming.id, label: label.trim() },
            { onSuccess: () => openRename(null) },
          );
        }}
      >
        <div className="space-y-2">
          <Label htmlFor="passkeyLabel">Name</Label>
          <Input
            id="passkeyLabel"
            maxLength={LABEL_MAX_LENGTH}
            value={label}
            onChange={(e) => setLabel(e.target.value)}
          />
        </div>
      </ConfirmDialog>

      <ConfirmDialog
        open={removing !== null}
        onOpenChange={(open) => !open && openRemove(null)}
        title="Remove this passkey?"
        description="You can still sign in with your password. You can add a new passkey at any time."
        confirmLabel="Remove passkey"
        destructive
        isPending={removeMutation.isPending}
        confirmDisabled={!currentPassword || !codeValid}
        onConfirm={() => {
          if (!removing) return;
          removeMutation.mutate(
            { id: removing.id, currentPassword, currentCode: needsCode ? currentCode : undefined },
            { onSuccess: () => openRemove(null) },
          );
        }}
      >
        {/* The server re-checks the password: a session alone must not remove a way to sign in. */}
        <div className="space-y-2">
          <Label htmlFor="passkeyCurrentPassword">Current Password</Label>
          <Input
            id="passkeyCurrentPassword"
            type="password"
            autoComplete="current-password"
            value={currentPassword}
            onChange={(e) => setCurrentPassword(e.target.value)}
          />
        </div>
        {needsCode && (
          <div className="space-y-2">
            <Label htmlFor="passkeyCurrentCode">Current Authenticator Code</Label>
            <Input
              id="passkeyCurrentCode"
              inputMode="numeric"
              autoComplete="one-time-code"
              maxLength={6}
              placeholder="6-digit code"
              value={currentCode}
              onChange={(e) => setCurrentCode(e.target.value.replace(/\D/g, ""))}
            />
          </div>
        )}
        {removeMutation.isError && (
          <Alert variant="destructive">
            <AlertCircle className="h-4 w-4" />
            <AlertDescription>
              {removeMutation.error?.message || "Failed to remove passkey"}
            </AlertDescription>
          </Alert>
        )}
      </ConfirmDialog>
    </>
  );
}
