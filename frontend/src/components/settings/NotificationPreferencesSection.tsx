import { Bell, Loader2 } from "lucide-react";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@foundation/src/components/ui/card";
import { Label } from "@foundation/src/components/ui/label";
import { Switch } from "@foundation/src/components/ui/switch";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import {
  getNotificationPreferences,
  updateNotificationPreferences,
} from "@foundation/src/lib/api/security-api";
import { toast } from "sonner";

interface NotificationPreferencesSectionProps {
  /** When true (shared/locked identity, e.g. the demo account), disable the toggle. */
  locked?: boolean;
}

export function NotificationPreferencesSection({
  locked = false,
}: NotificationPreferencesSectionProps = {}) {
  const queryClient = useQueryClient();

  const { data, isLoading } = useQuery({
    queryKey: ["notification-preferences"],
    queryFn: getNotificationPreferences,
  });

  const mutation = useMutation({
    // The switch is "Receive announcement emails" (on = opted in), so opt-out is the inverse.
    mutationFn: (receiveEmails: boolean) =>
      updateNotificationPreferences(!receiveEmails),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["notification-preferences"] });
      toast.success("Email preferences updated");
    },
    onError: () => {
      toast.error("Failed to update email preferences");
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
          <div className="flex items-center justify-center py-4">
            <Loader2 className="h-6 w-6 animate-spin text-muted-foreground" />
          </div>
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
