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
import {
  useNotificationPreferences,
  useUpdateNotificationPreferences,
} from "@foundation/src/hooks/useNotificationPreferences";

interface NotificationPreferencesSectionProps {
  /** When true (shared/locked identity, e.g. the demo account), disable the toggle. */
  locked?: boolean;
}

export function NotificationPreferencesSection({
  locked = false,
}: NotificationPreferencesSectionProps = {}) {
  const { data, isLoading } = useNotificationPreferences();

  const mutation = useUpdateNotificationPreferences();

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
