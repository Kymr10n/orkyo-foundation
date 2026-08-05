import { useEffect, useRef, useState } from 'react';
import { useMutation, useQuery } from '@tanstack/react-query';
import { Calendar, Check, Copy, Plus, Trash2 } from 'lucide-react';
import { toast } from 'sonner';
import { Button } from '@foundation/src/components/ui/button';
import { ScrollableDialogBody } from '@foundation/src/components/ui/dialog';
import { ScaffoldDialog } from '@foundation/src/components/ui/ScaffoldDialog';
import { Input } from '@foundation/src/components/ui/input';
import { Label } from '@foundation/src/components/ui/label';
import { Alert, AlertDescription } from '@foundation/src/components/ui/alert';
import { ConfirmDialog } from '@foundation/src/components/ui/ConfirmDialog';
import { FeatureUpsell } from '@foundation/src/components/ui/FeatureUpsell';
import { LoadingSpinner } from '@foundation/src/components/ui/LoadingSpinner';
import { useCalendarFeedAvailable } from '@foundation/src/hooks/useCalendarFeedAvailable';
import {
  createCalendarSubscription,
  getCalendarSubscriptions,
  revokeCalendarSubscription,
  type CalendarSubscriptionInfo,
} from '@foundation/src/lib/api/calendar-feed-api';
import { qk } from '@foundation/src/lib/api/query-keys';
import { formatLocalized } from '@foundation/src/lib/formatters';
import { useAppStore } from '@foundation/src/store/app-store';

interface CalendarFeedDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** The registering page's name for what the feed contains, e.g. "Utilization schedule". */
  label: string;
  description: string;
  /** Where the upsell's CTA points when the tenant's plan lacks the feature. Omit to hide the CTA. */
  upgradeHref?: string;
}

/**
 * Calendar subscriptions: a live feed address added to Outlook (or Google, or
 * Apple) once, which then keeps itself current. Read-only by design — the
 * calendar shows Orkyo's schedule, it does not write back into it.
 *
 * Scoped to the selected site, because that is what the feed actually serves:
 * the site's scheduled requests, not the subscriber's personal appointments.
 */
export function CalendarFeedDialog({ open, onOpenChange, label, description, upgradeHref }: CalendarFeedDialogProps) {
  const available = useCalendarFeedAvailable();
  const selectedSiteId = useAppStore((s) => s.selectedSiteId);
  const [subscriptionLabel, setSubscriptionLabel] = useState('');
  const [newUrl, setNewUrl] = useState<string | null>(null);
  const [copied, setCopied] = useState(false);
  const [revoking, setRevoking] = useState<CalendarSubscriptionInfo | null>(null);

  const { data: allSubscriptions = [], isLoading } = useQuery({
    queryKey: qk.calendarSubscriptions(),
    queryFn: getCalendarSubscriptions,
    enabled: open,
  });

  // The endpoint returns every subscription the user owns, across sites. Show the
  // ones for the site on screen, plus any site-less ones: those cover every site
  // (so they are relevant here too) and this dialog is the only place to revoke
  // them — the pre-site-scoping Account panel created them without a site.
  const subscriptions = allSubscriptions.filter(
    (s) => s.siteId === selectedSiteId || s.siteId == null,
  );

  const createMutation = useMutation({
    mutationFn: () => createCalendarSubscription({
      label: subscriptionLabel.trim() || undefined,
      siteId: selectedSiteId,
    }),
    onSuccess: (created) => {
      // Shown once and never again: only a hash is stored, so this is the
      // user's single chance to copy it.
      setNewUrl(created.feedUrl);
      setSubscriptionLabel('');
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

  // Cleared on unmount so closing the dialog inside the window can't set state on
  // a gone component.
  const copiedTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  useEffect(() => () => { if (copiedTimer.current) clearTimeout(copiedTimer.current); }, []);

  const copyUrl = async () => {
    if (!newUrl) return;
    await navigator.clipboard.writeText(newUrl);
    setCopied(true);
    toast.success('Feed URL copied');
    if (copiedTimer.current) clearTimeout(copiedTimer.current);
    copiedTimer.current = setTimeout(() => setCopied(false), 2000);
  };

  // The revealed address belongs to the session that created it, not to the
  // dialog: reopening must not show a URL the user already had their one look at.
  const handleOpenChange = (next: boolean) => {
    if (!next) {
      setNewUrl(null);
      setCopied(false);
      setSubscriptionLabel('');
    }
    onOpenChange(next);
  };

  return (
    <ScaffoldDialog
      open={open}
      onOpenChange={handleOpenChange}
      size="md"
      // Short, content-sized dialog: opt out of the shell's fixed 85dvh height
      // so it doesn't stand tall and mostly empty with one subscription listed.
      contentClassName="h-auto max-h-[85dvh]"
      title={
        <span className="flex items-center gap-2">
          <Calendar className="h-5 w-5" />
          Subscribe to {label.toLowerCase()}
        </span>
      }
      description={description}
    >
      <ScrollableDialogBody className="space-y-6 px-6 pb-6">
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

          {/* Only creating needs the plan. The list below stays either way, so a
              tenant that drops off a paid plan can still revoke what it handed out
              — which is exactly what the server allows. */}
          {available ? (
            <>
              <div className="flex items-end gap-2">
                <div className="flex-1 space-y-2">
                  <Label htmlFor="subscription-label">Name this subscription</Label>
                  <Input
                    id="subscription-label"
                    value={subscriptionLabel}
                    onChange={(e) => setSubscriptionLabel(e.target.value)}
                    placeholder="Outlook, work laptop"
                    maxLength={100}
                  />
                </div>
                <Button
                  onClick={() => createMutation.mutate()}
                  disabled={createMutation.isPending || !selectedSiteId}
                  title={selectedSiteId ? undefined : 'Select a site first'}
                >
                  <Plus className="mr-2 h-4 w-4" />
                  Create feed
                </Button>
              </div>

              {/* A site-less feed serves every site's schedule, which is not what this
                  dialog offers — so require a site rather than silently widening it. */}
              {!selectedSiteId && (
                <p className="text-sm text-muted-foreground">
                  Select a site to create a calendar subscription for its schedule.
                </p>
              )}
            </>
          ) : (
            <FeatureUpsell
              title="Calendar subscriptions"
              description="Keep your schedule live in Outlook, Google Calendar or Apple Calendar. Available on the Professional and Enterprise plans."
              upgradeHref={upgradeHref}
            />
          )}

          {isLoading ? (
            <LoadingSpinner message="Loading subscriptions…" fullScreen={false} />
          ) : subscriptions.length === 0 ? (
            <p className="text-sm text-muted-foreground">No calendar subscriptions yet.</p>
          ) : (
            <ul className="divide-y rounded-md border">
              {subscriptions.map((subscription) => (
                <li key={subscription.id} className="flex items-center justify-between gap-4 p-3">
                  <div className="min-w-0">
                    <p className="truncate font-medium">{subscription.label || 'Calendar subscription'}</p>
                    <p className="text-xs text-muted-foreground">
                      {subscription.siteId == null && <>All sites{' · '}</>}
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
        </ScrollableDialogBody>

        <ConfirmDialog
          open={!!revoking}
          onOpenChange={(isOpen) => !isOpen && setRevoking(null)}
          title="Revoke this subscription?"
          description="The calendar stops updating and the address stops working immediately. Anyone you shared it with loses access too."
          confirmLabel="Revoke"
          destructive
          onConfirm={() => { if (revoking) revokeMutation.mutate(revoking.id); }}
        />
    </ScaffoldDialog>
  );
}
