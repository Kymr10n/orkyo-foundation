import { Bell } from "lucide-react";
import { LoadingSpinner } from "@foundation/src/components/ui/LoadingSpinner";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@foundation/src/components/ui/card";
import { Label } from "@foundation/src/components/ui/label";
import { Switch } from "@foundation/src/components/ui/switch";
import { useQuery, useMutation } from "@tanstack/react-query";
import {
  getNotificationPreferences,
  updateNotificationPreferences,
} from "@foundation/src/lib/api/security-api";
import { qk } from "@foundation/src/lib/api/query-keys";

interface NotificationPreferencesSectionProps {
  /** When true (shared/locked identity, e.g. the demo account), disable the toggle. */
  locked?: boolean;
}

export function NotificationPreferencesSection({
  locked = false,
}: NotificationPreferencesSectionProps = {}) {
  const { data, isLoading } = useQuery({
    queryKey: qk.notificationPreferences.all(),
    queryFn: getNotificationPreferences,
  });

  const mutation = useMutation({
    // The switch is "Receive announcement emails" (on = opted in), so opt-out is the inverse.
    mutationFn: (receiveEmails: boolean) =>
      updateNotificationPreferences(!receiveEmails),
    meta: {
      successMessage: "Email preferences updated",
      errorMessage: "Failed to update email preferences",
      invalidates: [qk.notificationPreferences.all()],
    },
  });

  const receiveEmails = !(data?.announcementEmailOptOut ?? false);

  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-2">
          <Bell className="h-5 w-5" />
          Email notifications
        </CardTitle>
        <CardDescription>
          Choose which emails you receive from us
        </CardDescription>
      </CardHeader>
      <CardContent>
        {isLoading ? (
          <LoadingSpinner size="sm" muted fullScreen={false} className="py-4" />
        ) : (
          <div className="flex items-center justify-between gap-4">
            <div className="space-y-1">
              <Label htmlFor="announcement-emails" className="text-sm font-medium">
                Receive announcement emails
              </Label>
              <p className="text-xs text-muted-foreground">
                Product news and updates. Important announcements are always sent,
                even when this is off.
              </p>
            </div>
            <Switch
              id="announcement-emails"
              checked={receiveEmails}
              disabled={locked || mutation.isPending}
              onCheckedChange={(checked) => mutation.mutate(checked)}
            />
          </div>
        )}
      </CardContent>
    </Card>
  );
}
