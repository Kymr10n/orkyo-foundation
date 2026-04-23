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
import { Loader2, FileText } from "lucide-react";
import { acceptTos } from "@foundation/src/lib/api/session-api";
import { logger } from "@foundation/src/lib/core/logger";

interface TosPageProps {
  /** Called after successful TOS acceptance (e.g. refresh the pipeline). */
  onAccept: () => Promise<void> | void;
  /** Called when the user cancels (e.g. logout). */
  onCancel: () => void;
  /** TOS version to accept. */
  tosVersion: string;
}

export function TosPage({ onAccept, onCancel, tosVersion }: TosPageProps) {
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
      <div className="w-full max-w-2xl space-y-4">
        <div className="rounded-md bg-yellow-400 text-yellow-900 p-4 text-center">
          <p className="text-base font-extrabold uppercase tracking-widest mb-1">Public Demo</p>
          <p className="text-sm leading-relaxed opacity-85">
            You are welcome to explore this solution. Prior to official go-live, you may use it freely —
            however, your data may be lost in future updates. No guarantees or liability are given. Use at your own discretion.
          </p>
        </div>
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
            <div className="space-y-4 text-sm text-muted-foreground">
              <h3 className="font-semibold text-foreground">1. Acceptance of Terms</h3>
              <p>
                By accessing and using this space utilization service ("Service"), you accept
                and agree to be bound by the terms and provision of this agreement.
              </p>

              <h3 className="font-semibold text-foreground">2. Use of Service</h3>
              <p>
                You agree to use the Service only for lawful purposes and in accordance with these
                Terms. You are responsible for maintaining the confidentiality of your account
                credentials and for all activities that occur under your account.
              </p>

              <h3 className="font-semibold text-foreground">3. Data and Privacy</h3>
              <p>
                We collect and process your data as described in our Privacy Policy. You retain
                ownership of all data you upload to the Service. We implement appropriate security
                measures to protect your data.
              </p>

              <h3 className="font-semibold text-foreground">4. Service Availability</h3>
              <p>
                While we strive to maintain high availability, we do not guarantee uninterrupted
                access to the Service. We may perform maintenance or updates that temporarily
                affect availability.
              </p>

              <h3 className="font-semibold text-foreground">5. Limitation of Liability</h3>
              <p>
                To the maximum extent permitted by law, we shall not be liable for any indirect,
                incidental, special, consequential, or punitive damages resulting from your use
                of or inability to use the Service.
              </p>

              <h3 className="font-semibold text-foreground">6. Changes to Terms</h3>
              <p>
                We reserve the right to modify these Terms at any time. We will notify you of
                any material changes. Your continued use of the Service after such modifications
                constitutes acceptance of the updated Terms.
              </p>

              <h3 className="font-semibold text-foreground">7. Contact</h3>
              <p>
                If you have any questions about these Terms, please contact your system administrator.
              </p>
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
            disabled={!accepted || submitting}
          >
            {submitting && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}
            Accept and Continue
          </Button>
        </CardFooter>
      </Card>
      </div>
    </div>
  );
}
