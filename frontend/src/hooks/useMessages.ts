import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  type UserAnnouncement,
  getActiveAnnouncements,
  getUnreadAnnouncementCount,
  markAnnouncementRead,
} from '@foundation/src/lib/api/user-announcements-api';
import { qk } from '@foundation/src/lib/api/query-keys';
import { STALE } from "@foundation/src/lib/core/query-client";

export const useActiveAnnouncements = () =>
  useQuery({
    queryKey: qk.announcements.active(),
    queryFn: async () => {
      const res = await getActiveAnnouncements();
      return res.announcements;
    },
  });

/**
 * The TopBar badge count. refetchIntervalInBackground defaults to false, so polling pauses when
 * the tab is hidden (React Query honors document visibility). refetchOnWindowFocus runs a single
 * refetch when the tab is refocused, which is cheaper than burning a poll cycle while hidden.
 */
export const useUnreadAnnouncementCount = () =>
  useQuery({
    queryKey: qk.announcements.unread(),
    queryFn: getUnreadAnnouncementCount,
    refetchInterval: 60_000,
    staleTime: STALE.REALTIME,
    refetchOnWindowFocus: true,
  });

/**
 * Marks one announcement read, flipping the row optimistically so the expand
 * gesture feels instant. Rolls back on error.
 */
export const useMarkAnnouncementRead = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: markAnnouncementRead,
    onMutate: async (announcementId: string) => {
      // Cancel any outgoing refetches so they don't overwrite our optimistic update
      await queryClient.cancelQueries({ queryKey: qk.announcements.active() });

      // Snapshot previous value
      const previous = queryClient.getQueryData<UserAnnouncement[]>(qk.announcements.active());

      // Optimistically update
      queryClient.setQueryData<UserAnnouncement[]>(qk.announcements.active(), (old) =>
        old?.map((a) => (a.id === announcementId ? { ...a, isRead: true } : a))
      );

      return { previous };
    },
    onError: (_err, _id, context) => {
      // Rollback on error
      if (context?.previous) {
        queryClient.setQueryData(qk.announcements.active(), context.previous);
      }
    },
    onSettled: () => {
      // Sync TopBar unread badge
      // eslint-disable-next-line no-restricted-syntax -- optimistic-rollback mutation (onMutate snapshot): meta can't express it, invalidation stays hand-rolled (docs/dialog-feedback.md)
      queryClient.invalidateQueries({ queryKey: qk.announcements.unread() });
    },
  });
};
