import { useState } from "react";
import { Download, ShieldCheck, Trash2 } from "lucide-react";
import { Button } from "@foundation/src/components/ui/button";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@foundation/src/components/ui/card";
import { ConfirmDialog } from "@foundation/src/components/ui/ConfirmDialog";
import { ErrorAlert } from "@foundation/src/components/ui/ErrorAlert";
import { useDeleteOwnAccount, useExportPersonalData } from "@foundation/src/hooks/useAccount";
import { errorMessage } from "@foundation/src/hooks/mutation-utils";
import { goToApex } from "@foundation/src/lib/utils/tenant-navigation";

interface DataPrivacySectionProps {
  /** The account's email; the person types it to confirm the deletion. */
  email: string;
  /** When true (shared/locked identity, e.g. the demo account), deletion is not offered. */
  locked?: boolean;
}

/**
 * The person's rights over their own data: download everything Orkyo holds about them, and
 * delete the account. Deletion is type-to-confirm with the account email and stays open on a
 * refusal so the server's reason (an owned organization, a last-admin role) is read in place.
 */
export function DataPrivacySection({ email, locked = false }: DataPrivacySectionProps) {
  const exportMutation = useExportPersonalData();
  const deleteMutation = useDeleteOwnAccount();
  const [deleteOpen, setDeleteOpen] = useState(false);
  const [deleteError, setDeleteError] = useState<string | null>(null);

  const handleDelete = async () => {
    setDeleteError(null);
    try {
      await deleteMutation.mutateAsync(email);
      // The server has removed every session; a full navigation to the apex lands on sign-in.
      goToApex("/");
    } catch (err) {
      setDeleteError(errorMessage(err, "Could not delete your account"));
    }
  };

  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-2">
          <ShieldCheck className="h-5 w-5" />
          Your data
        </CardTitle>
        <CardDescription>
          Download a copy of your personal data, or delete your account.
        </CardDescription>
      </CardHeader>
      <CardContent className="space-y-6">
        <div className="flex items-center justify-between gap-4">
          <div className="space-y-1">
            <p className="text-sm font-medium">Download my data</p>
            <p className="text-xs text-muted-foreground">
              A JSON file with your profile, memberships, sessions and the data you created in
              each organization.
            </p>
          </div>
          <Button
            variant="outline"
            size="sm"
            onClick={() => exportMutation.mutate()}
            loading={exportMutation.isPending}
            disabled={exportMutation.isPending}
          >
            {!exportMutation.isPending && <Download className="h-4 w-4 mr-1" />}
            Download
          </Button>
        </div>

        {!locked && (
          <div className="flex items-center justify-between gap-4">
            <div className="space-y-1">
              <p className="text-sm font-medium">Delete account</p>
              <p className="text-xs text-muted-foreground">
                Removes your account and personal data from every organization. This cannot be
                undone. Organizations you own must be deleted or handed over first.
              </p>
            </div>
            <Button
              variant="outline"
              size="sm"
              className="text-destructive hover:text-destructive"
              onClick={() => { setDeleteError(null); setDeleteOpen(true); }}
              disabled={!email}
            >
              <Trash2 className="h-4 w-4 mr-1" />
              Delete
            </Button>
          </div>
        )}
      </CardContent>

      <ConfirmDialog
        open={deleteOpen}
        onOpenChange={(open) => { setDeleteOpen(open); if (!open) setDeleteError(null); }}
        title="Delete your account?"
        description={
          <>
            Your account and your personal data in every organization will be permanently
            deleted. Type <strong>{email}</strong> to confirm.
          </>
        }
        confirmLabel="Delete my account"
        destructive
        confirmPhrase={email}
        isPending={deleteMutation.isPending}
        onConfirm={handleDelete}
      >
        {/* Always an element: ConfirmDialog closes on confirm only when it has no children. */}
        <ErrorAlert message={deleteError} />
      </ConfirmDialog>
    </Card>
  );
}
