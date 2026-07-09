/**
 * Terms of Service Acceptance Page
 *
 * Pure UI component — flow control is handled by the caller (ApexGateway)
 * via `onAccept` and `onCancel` callbacks.
 */

import { useState } from "react";
import { Button } from "@foundation/src/components/ui/button";
import { Checkbox } from "@foundation/src/components/ui/checkbox";
import { Card, CardContent, CardDescription, CardFooter, CardHeader, CardTitle } from "@foundation/src/components/ui/card";
import { ScrollArea } from "@foundation/src/components/ui/scroll-area";
import { FileText } from "lucide-react";
import { acceptTos } from "@foundation/src/lib/api/session-api";
import { logger } from "@foundation/src/lib/core/logger";
import { usePageTitle } from "@foundation/src/hooks/usePageTitle";

interface TosPageProps {
  /** Called after successful TOS acceptance (e.g. refresh the pipeline). */
  onAccept: () => Promise<void> | void;
  /** Called when the user cancels (e.g. logout). */
  onCancel: () => void;
  /** TOS version to accept. */
  tosVersion: string;
  /**
   * Terms text resolved by the backend (site-scoped `legal.tos_text` setting).
   * Plain text; blank lines separate paragraphs. Falls back to the built-in
   * default when absent (older backend that doesn't send the field yet).
   */
  tosText?: string;
}

/**
 * Built-in generic terms, mirrored from the backend's compiled default
 * (TenantSettings.DefaultTosText). Only rendered when the backend didn't
 * provide `tosText` — i.e. during the package version-skew window.
 */
export const DEFAULT_TOS_TEXT = `1. Acceptance of Terms

By accessing and using this service ("Service"), you accept and agree to be bound by the terms and provision of this agreement.

2. Use of Service

You agree to use the Service only for lawful purposes and in accordance with these Terms. You are responsible for maintaining the confidentiality of your account credentials and for all activities that occur under your account.

3. Data and Privacy

We collect and process your data as described in our Privacy Policy. You retain ownership of all data you upload to the Service. We implement appropriate security measures to protect your data.

4. Service Availability

While we strive to maintain high availability, we do not guarantee uninterrupted access to the Service. We may perform maintenance or updates that temporarily affect availability.

5. Limitation of Liability

To the maximum extent permitted by law, we shall not be liable for any indirect, incidental, special, consequential, or punitive damages resulting from your use of or inability to use the Service.

6. Changes to Terms

We reserve the right to modify these Terms at any time. We will notify you of any material changes. Your continued use of the Service after such modifications constitutes acceptance of the updated Terms.

7. Contact

If you have any questions about these Terms, please contact your system administrator.`;

export function TosPage({ onAccept, onCancel, tosVersion, tosText }: TosPageProps) {
  usePageTitle("Terms of Service");
  const [accepted, setAccepted] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const handleAccept = async () => {
    if (!accepted) return;

    setSubmitting(true);
    setError(null);

    try {
      await acceptTos(tosVersion);
      await onAccept();
    } catch (err) {
      logger.error("ToS acceptance error:", err);
      setError(err instanceof Error ? err.message : "Failed to accept Terms of Service");
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <div className="min-h-screen flex items-center justify-center bg-background p-4">
      <Card className="w-full max-w-2xl">
        <CardHeader className="space-y-1">
          <div className="flex items-center gap-2">
            <FileText className="h-5 w-5 text-primary" />
            <CardTitle className="text-2xl">Terms of Service</CardTitle>
          </div>
          <CardDescription>
            Please review and accept our Terms of Service (Version {tosVersion}) to continue.
          </CardDescription>
        </CardHeader>

        <CardContent className="space-y-4">
          <ScrollArea className="h-64 rounded-md border p-4">
            <div className="text-sm text-muted-foreground whitespace-pre-wrap">
              {tosText ?? DEFAULT_TOS_TEXT}
            </div>
          </ScrollArea>

          {error && (
            <div className="text-destructive text-sm bg-destructive/10 p-3 rounded-md">
              {error}
            </div>
          )}

          <div className="flex items-center space-x-2">
            <Checkbox
              id="accept"
              checked={accepted}
              onCheckedChange={(checked) => setAccepted(checked === true)}
              disabled={submitting}
            />
            <label
              htmlFor="accept"
              className="text-sm font-medium leading-none peer-disabled:cursor-not-allowed peer-disabled:opacity-70 cursor-pointer"
            >
              I have read and agree to the Terms of Service
            </label>
          </div>
        </CardContent>

        <CardFooter className="flex justify-between">
          <Button
            variant="outline"
            onClick={onCancel}
            disabled={submitting}
          >
            Cancel
          </Button>
          <Button
            onClick={handleAccept}
            loading={submitting}
            disabled={!accepted || submitting}
          >
            Accept and Continue
          </Button>
        </CardFooter>
      </Card>
    </div>
  );
}
