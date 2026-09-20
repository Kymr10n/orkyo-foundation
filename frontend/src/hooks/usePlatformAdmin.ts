import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  type AdminSettingsResponse,
  getAdminDiagnostics,
  getAdminSettings,
  updateAdminSettings,
} from '@foundation/src/lib/api/admin-api';
import {
  type Announcement,
  type CreateAnnouncementRequest,
  type UpdateAnnouncementRequest,
  createAnnouncement,
  deleteAnnouncement,
  getAnnouncements,
  updateAnnouncement,
} from '@foundation/src/lib/api/announcement-api';
import { qk } from '@foundation/src/lib/api/query-keys';

/**
 * The platform-operator surfaces (diagnostics, runtime settings, announcements, audit).
 *
 * Every query here is `staleTime: 0`: an operator opens these tabs to see the state of the
 * system right now, so each visit re-reads it, as the hand-rolled loads these replaced did.
 */

// ── Diagnostics ──────────────────────────────────────────────────────────────

export const useAdminDiagnostics = () =>
  useQuery({
    queryKey: qk.admin.diagnostics(),
    queryFn: getAdminDiagnostics,
    staleTime: 0,
  });

// ── Runtime settings ─────────────────────────────────────────────────────────

export const useAdminSettings = () =>
  useQuery({
    queryKey: qk.admin.settings(),
    queryFn: getAdminSettings,
    staleTime: 0,
  });

/**
 * Saves the changed keys only. The response carries the settings as they now stand, so it
 * is written straight into the cache instead of forcing a second read.
 */
export const useUpdateAdminSettings = (
  options: { onSuccess: (result: { runtime: AdminSettingsResponse['runtime']; updatedKeys: string[] }) => void },
) => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (changes: Record<string, string>) => updateAdminSettings(changes),
    onSuccess: (result) => {
      queryClient.setQueryData<AdminSettingsResponse>(qk.admin.settings(), (prev) =>
        prev ? { ...prev, runtime: result.runtime } : prev,
      );
      options.onSuccess(result);
    },
  });
};

// ── Announcements ────────────────────────────────────────────────────────────

/** The admin list: expired announcements included. */
export const useAdminAnnouncements = () =>
  useQuery({
    queryKey: qk.announcements.adminAll(),
    queryFn: () => getAnnouncements(true),
    staleTime: 0,
  });

/**
 * Create or update, by whether an existing announcement was handed in. No success toast:
 * the dialog closes on success and reports failures inline, as it always has.
 */
export const useSaveAnnouncement = (
  announcement: Announcement | null,
  options: { onSuccess: () => void; onError: (err: Error) => void },
) =>
  useMutation({
    mutationFn: async (data: CreateAnnouncementRequest | UpdateAnnouncementRequest) => {
      if (announcement) await updateAnnouncement(announcement.id, data as UpdateAnnouncementRequest);
      else await createAnnouncement(data as CreateAnnouncementRequest);
    },
    meta: { invalidates: [qk.announcements.adminAll()] },
    ...options,
  });

export const useDeleteAnnouncement = (
  options: { onSuccess: () => void; onError: (err: Error) => void },
) =>
  useMutation({
    mutationFn: (id: string) => deleteAnnouncement(id),
    meta: { invalidates: [qk.announcements.adminAll()] },
    ...options,
  });

// ── Audit ────────────────────────────────────────────────────────────────────

/** Wire shape both audit endpoints return (grandfathered `events`/`totalCount`). */
export interface AuditEventPage<T> {
  events: T[];
  totalCount: number;
}

/**
 * One page of an audit log. The caller owns the key and the fetcher, because the tenant log
 * and the control-plane log are different endpoints over the same surface.
 *
 * The previous page stays visible while the next one loads (`keepPreviousData`), so paging
 * and filter keystrokes never flash an empty table or unmount an open filter popover.
 */
export const useAuditEventPage = <T,>(
  queryKey: readonly unknown[],
  fetchPage: () => Promise<AuditEventPage<T>>,
) =>
  useQuery({
    queryKey,
    queryFn: fetchPage,
    // staleTime: 0 like every other query in this module. The hand-rolled load this replaced
    // re-read on each mount; inheriting the five-minute default would show an operator cached
    // rows on reopening the tab, which is the worst place in the app to do that.
    staleTime: 0,
    placeholderData: keepPreviousData,
  });
