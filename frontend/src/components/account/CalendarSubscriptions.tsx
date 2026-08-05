import { useState } from 'react';
import { useMutation, useQuery } from '@tanstack/react-query';
import { Calendar, Check, Copy, Plus, Trash2 } from 'lucide-react';
import { toast } from 'sonner';
import { Button } from '@foundation/src/components/ui/button';
import { Input } from '@foundation/src/components/ui/input';
import { Label } from '@foundation/src/components/ui/label';
import { Alert, AlertDescription } from '@foundation/src/components/ui/alert';
import { ConfirmDialog } from '@foundation/src/components/ui/ConfirmDialog';
import { LoadingSpinner } from '@foundation/src/components/ui/LoadingSpinner';
import {
  createCalendarSubscription,
  getCalendarSubscriptions,
  revokeCalendarSubscription,
  type CalendarSubscriptionInfo,
} from '@foundation/src/lib/api/calendar-feed-api';
import { qk } from '@foundation/src/lib/api/query-keys';
import { formatLocalized } from '@foundation/src/lib/formatters';

/**
 * Calendar subscriptions: a live feed URL the user adds to Outlook (or Google,
 * or Apple) once, which then keeps itself current. Read-only by design — the
 * calendar shows Orkyo's schedule, it does not write back into it.
 */
export function CalendarSubscriptions() {
  const [label, setLabel] = useState('');
  const [newUrl, setNewUrl] = useState<string | null>(null);
  const [copied, setCopied] = useState(false);
  const [revoking, setRevoking] = useState<CalendarSubscriptionInfo | null>(null);

  const { data: subscriptions = [], isLoading } = useQuery({
    queryKey: qk.calendarSubscriptions(),
    queryFn: getCalendarSubscriptions,
  });

  const createMutation = useMutation({
    mutationFn: () => createCalendarSubscription({ label: label.trim() || undefined }),
    onSuccess: (created) => {
      // Shown once and never again: only a hash is stored, so this is the
      // user's single chance to copy it.
      setNewUrl(created.feedUrl);
      setLabel('');
    },
    meta: {
      errorMessage: 'Could not create the calendar subscription',
      invalidates: [qk.calendarSubscriptions()],
    },
  });

  const revokeMutation = useMutation({
    mutationFn: (id: string) => revokeCalendarSubscription(id),
    onSuccess: () => setRevoking(null),
    meta: {
      successMessage: 'Subscription revoked',
      errorMessage: 'Could not revoke the subscription',
      invalidates: [qk.calendarSubscriptions()],
    },
  });

  const copyUrl = async () => {
    if (!newUrl) return;
    await navigator.clipboard.writeText(newUrl);
    setCopied(true);
    toast.success('Feed URL copied');
    setTimeout(() => setCopied(false), 2000);
  };

  if (isLoading) return <LoadingSpinner message="Loading subscriptions…" fullScreen={false} />;

  return (
    <div className="space-y-6">
      <div>
        <h3 className="text-lg font-semibold">Calendar subscriptions</h3>
        <p className="text-sm text-muted-foreground">
          Add your Orkyo schedule to Outlook, Google Calendar or Apple Calendar. The calendar
          updates itself — you subscribe once and it stays current.
        </p>
      </div>

      {newUrl && (
        <Alert>
          <Calendar className="h-4 w-4" />
          <AlertDescription className="space-y-3">
            <p className="font-medium">
              Copy this address now — it is shown only once.
            </p>
            <div className="flex gap-2">
              <Input readOnly value={newUrl} aria-label="Calendar feed address" className="font-mono text-xs" />
              <Button type="button" variant="secondary" onClick={copyUrl}>
                {copied ? <Check className="h-4 w-4" /> : <Copy className="h-4 w-4" />}
                {copied ? 'Copied' : 'Copy'}
              </Button>
            </div>
            <p className="text-sm text-muted-foreground">
              In Outlook: <strong>Add calendar → Subscribe from web</strong>, then paste the
              address. Google Calendar: <strong>Other calendars → From URL</strong>. Apple
              Calendar: <strong>File → New Calendar Subscription</strong>.
            </p>
          </AlertDescription>
        </Alert>
      )}

      <div className="flex items-end gap-2">
        <div className="flex-1 space-y-2">
          <Label htmlFor="subscription-label">Name this subscription</Label>
          <Input
            id="subscription-label"
            value={label}
            onChange={(e) => setLabel(e.target.value)}
            placeholder="Outlook, work laptop"
            maxLength={100}
          />
        </div>
        <Button onClick={() => createMutation.mutate()} disabled={createMutation.isPending}>
          <Plus className="mr-2 h-4 w-4" />
          Create feed
        </Button>
      </div>

      {subscriptions.length === 0 ? (
        <p className="text-sm text-muted-foreground">No calendar subscriptions yet.</p>
      ) : (
        <ul className="divide-y rounded-md border">
          {subscriptions.map((subscription) => (
            <li key={subscription.id} className="flex items-center justify-between gap-4 p-3">
              <div className="min-w-0">
                <p className="truncate font-medium">{subscription.label || 'Calendar subscription'}</p>
                <p className="text-xs text-muted-foreground">
                  Created {formatLocalized(new Date(subscription.createdAt), { dateStyle: 'medium' })}
                  {' · '}
                  {subscription.lastUsedAt
                    ? `last used ${formatLocalized(new Date(subscription.lastUsedAt), { dateStyle: 'medium' })}`
                    : 'never used'}
                </p>
              </div>
              <Button
                variant="ghost"
                size="icon"
                onClick={() => setRevoking(subscription)}
                aria-label={`Revoke ${subscription.label || 'calendar subscription'}`}
              >
                <Trash2 className="h-4 w-4" />
              </Button>
            </li>
          ))}
        </ul>
      )}

      <ConfirmDialog
        open={!!revoking}
        onOpenChange={(open) => !open && setRevoking(null)}
        title="Revoke this subscription?"
        description="The calendar stops updating and the address stops working immediately. Anyone you shared it with loses access too."
        confirmLabel="Revoke"
        destructive
        onConfirm={() => { if (revoking) revokeMutation.mutate(revoking.id); }}
      />
    </div>
  );
}
