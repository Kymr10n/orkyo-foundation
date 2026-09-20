import { useMutation, useQuery } from '@tanstack/react-query';
import {
  createCalendarSubscription,
  getCalendarSubscriptions,
  revokeCalendarSubscription,
} from '@foundation/src/lib/api/calendar-feed-api';
import { qk } from '@foundation/src/lib/api/query-keys';

/** Every calendar subscription the user owns, across sites. The caller narrows to the site shown. */
export const useCalendarSubscriptions = (enabled = true) =>
  useQuery({
    queryKey: qk.calendarSubscriptions(),
    queryFn: getCalendarSubscriptions,
    enabled,
  });

export const useCreateCalendarSubscription = () =>
  useMutation({
    mutationFn: (data: { label?: string; siteId: string | null }) => createCalendarSubscription(data),
    meta: {
      errorMessage: 'Could not create the calendar subscription',
      invalidates: [qk.calendarSubscriptions()],
    },
  });

export const useRevokeCalendarSubscription = () =>
  useMutation({
    mutationFn: (id: string) => revokeCalendarSubscription(id),
    meta: {
      successMessage: 'Subscription revoked',
      errorMessage: 'Could not revoke the subscription',
      invalidates: [qk.calendarSubscriptions()],
    },
  });
