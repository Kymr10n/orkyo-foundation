/**
 * AnnouncementsTab – Admin tab for managing platform-wide announcements.
 *
 * Provides a list of all announcements (active + expired) with inline
 * create / edit / delete capabilities.
 */

import { useState } from 'react';
import { formatDateDisplay, toDateTimeLocalValue } from '@foundation/src/lib/formatters';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@foundation/src/components/ui/card';
import { ErrorAlert } from '@foundation/src/components/ui/ErrorAlert';
import { Badge } from '@foundation/src/components/ui/badge';
import { Button } from '@foundation/src/components/ui/button';
import { OrkyoDataTable, type ColumnDef } from '@foundation/src/components/ui/OrkyoDataTable';
import { RowActions } from '@foundation/src/components/ui/RowActions';
import { Input } from '@foundation/src/components/ui/input';
import { Label } from '@foundation/src/components/ui/label';
import { Textarea } from '@foundation/src/components/ui/textarea';
import { Switch } from '@foundation/src/components/ui/switch';
import { Checkbox } from '@foundation/src/components/ui/checkbox';
import { DateTimePicker } from '@foundation/src/components/ui/date-time-picker';
import { FormDialog } from '@foundation/src/components/ui/FormDialog';
import { ConfirmDialog } from '@foundation/src/components/ui/ConfirmDialog';
import { Plus, Pencil, Trash2, Megaphone, AlertTriangle } from 'lucide-react';
import type { Announcement, AnnouncementChannel } from '@foundation/src/lib/api/announcement-api';
import {
  useAdminAnnouncements,
  useDeleteAnnouncement,
  useSaveAnnouncement,
} from '@foundation/src/hooks/usePlatformAdmin';
import { errorMessage } from '@foundation/src/hooks/mutation-utils';
import { useTableUrlState } from '@foundation/src/hooks/useTableUrlState';
import { useEntityFormDialog } from '@foundation/src/hooks/useEntityFormDialog';
import { LoadingSpinner } from "@foundation/src/components/ui/LoadingSpinner";

/** Selectable delivery channels for new announcements (label + hint). */
const CHANNEL_OPTIONS: { value: AnnouncementChannel; label: string; hint: string }[] = [
  { value: 'site', label: 'In-app', hint: 'Shows in the notification center.' },
  { value: 'email', label: 'Email', hint: 'Sends to all registered users.' },
];

/** Derived status shown in the Status column — shared by the badge cell and its facet. */
function announcementStatus(a: Announcement): 'Expired' | 'Important' | 'Active' {
  return a.isExpired ? 'Expired' : a.isImportant ? 'Important' : 'Active';
}

// ============================================================================
// Main Tab
// ============================================================================

export function AnnouncementsTab() {
  const { data, isLoading, error: loadError, refetch } = useAdminAnnouncements();
  const announcements = data?.announcements ?? [];
  const [error, setError] = useState<string | null>(null);

  // Dialog state
  const [showCreateDialog, setShowCreateDialog] = useState(false);
  const [editingAnnouncement, setEditingAnnouncement] = useState<Announcement | null>(null);
  const [deletingAnnouncement, setDeletingAnnouncement] = useState<Announcement | null>(null);

  const deleteMutation = useDeleteAnnouncement({
    onSuccess: () => setDeletingAnnouncement(null),
    onError: (err) => {
      setError(errorMessage(err));
      setDeletingAnnouncement(null);
    },
  });

  const handleDelete = () => {
    if (!deletingAnnouncement) return;
    deleteMutation.mutate(deletingAnnouncement.id);
  };

  // Shared row actions — desktop table cell and phone card.
  const renderActions = (a: Announcement) => (
    <RowActions
      triggerLabel={`Actions for ${a.title}`}
      actions={[
        { label: 'Edit', icon: Pencil, onSelect: () => setEditingAnnouncement(a) },
        { label: 'Delete', icon: Trash2, onSelect: () => setDeletingAnnouncement(a), destructive: true },
      ]}
    />
  );

  const columns: ColumnDef<Announcement>[] = [
    {
      accessorKey: 'title',
      header: 'Title',
      meta: { filter: { type: 'text' } },
      cell: ({ row }) => {
        const a = row.original;
        return (
          <div className={`flex items-center gap-2 ${a.isExpired ? 'opacity-50' : ''}`}>
            {a.isImportant && (
              <AlertTriangle className="h-4 w-4 text-amber-500 shrink-0" />
            )}
            <span className="font-medium truncate max-w-[260px]">{a.title}</span>
          </div>
        );
      },
    },
    {
      id: 'status',
      accessorFn: (a) => announcementStatus(a),
      header: 'Status',
      meta: { filter: { type: 'enum' } },
      cell: ({ row }) => {
        const status = announcementStatus(row.original);
        return status === 'Expired' ? (
          <Badge variant="secondary">Expired</Badge>
        ) : status === 'Important' ? (
          <Badge variant="destructive">Important</Badge>
        ) : (
          <Badge>Active</Badge>
        );
      },
    },
    {
      id: 'created',
      accessorFn: (a) => a.createdAt,
      header: 'Created',
      meta: { filter: { type: 'date' } },
      cell: ({ row }) => {
        const a = row.original;
        return (
          <div className={`text-sm text-muted-foreground whitespace-nowrap ${a.isExpired ? 'opacity-50' : ''}`}>
            <div>{formatDateDisplay(a.createdAt)}</div>
            <div className="text-xs">{a.createdByEmail}</div>
          </div>
        );
      },
    },
    {
      id: 'expires',
      accessorFn: (a) => a.expiresAt,
      header: 'Expires',
      meta: { filter: { type: 'date' } },
      cell: ({ row }) => (
        <span className={`text-sm text-muted-foreground whitespace-nowrap ${row.original.isExpired ? 'opacity-50' : ''}`}>
          {formatDateDisplay(row.original.expiresAt)}
        </span>
      ),
    },
    {
      id: 'revision',
      header: 'Rev',
      cell: ({ row }) => (
        <span className={`text-sm text-muted-foreground ${row.original.isExpired ? 'opacity-50' : ''}`}>
          {row.original.revision}
        </span>
      ),
    },
    {
      id: 'actions',
      header: () => null,
      size: 96,
      cell: ({ row }) => renderActions(row.original),
    },
  ];

  // Header sort/filter state lives in the URL: bookmarkable, shareable, Back-safe.
  const tableUrlState = useTableUrlState('ann', columns);

  // Phone presentation: title + status/dates stacked, edit/delete trailing.
  const renderCard = (a: Announcement) => (
    <div className={`flex items-start justify-between gap-2 ${a.isExpired ? 'opacity-50' : ''}`}>
      <div className="min-w-0 space-y-1">
        <div className="flex items-center gap-2 min-w-0">
          {a.isImportant && <AlertTriangle className="h-4 w-4 text-amber-500 shrink-0" />}
          <span className="font-medium truncate">{a.title}</span>
        </div>
        <div>
          {a.isExpired ? (
            <Badge variant="secondary">Expired</Badge>
          ) : a.isImportant ? (
            <Badge variant="destructive">Important</Badge>
          ) : (
            <Badge>Active</Badge>
          )}
        </div>
        <p className="text-xs text-muted-foreground truncate">
          {formatDateDisplay(a.createdAt)} · expires {formatDateDisplay(a.expiresAt)}
        </p>
      </div>
      <div className="shrink-0">{renderActions(a)}</div>
    </div>
  );

  if (isLoading) {
    return (
      <Card>
        <CardContent className="py-8 md:py-8">
          <LoadingSpinner size="sm" muted fullScreen={false} message="Loading announcements…" />
        </CardContent>
      </Card>
    );
  }

  return (
    <div className="space-y-6">
      <Card>
        <CardHeader className="flex-row items-center justify-between space-y-0">
          <div>
            <CardTitle className="flex items-center gap-2">
              <Megaphone className="h-5 w-5" />
              Platform Announcements
            </CardTitle>
            <CardDescription className="mt-1.5">
              Create and manage announcements visible to all users across the platform.
            </CardDescription>
          </div>
          <Button size="sm" onClick={() => setShowCreateDialog(true)}>
            <Plus className="h-4 w-4 mr-1" />
            New Announcement
          </Button>
        </CardHeader>
        <CardContent>
          <div className="mb-4 empty:mb-0">
            <ErrorAlert message={error} />
          </div>

          <OrkyoDataTable
            {...tableUrlState}
            onRowClick={(a) => setEditingAnnouncement(a)}
            columns={columns}
            data={announcements}
            error={loadError}
            errorFallback="Failed to load announcements"
            onRetry={() => void refetch()}
            emptyMessage="No announcements yet. Create one to get started."
            renderCard={renderCard}
          />
        </CardContent>
      </Card>

      {/* Create / Edit Dialog */}
      <AnnouncementFormDialog
        open={showCreateDialog || !!editingAnnouncement}
        announcement={editingAnnouncement}
        onOpenChange={(open) => {
          if (!open) {
            setShowCreateDialog(false);
            setEditingAnnouncement(null);
          }
        }}
        onSaved={() => {
          setShowCreateDialog(false);
          setEditingAnnouncement(null);
        }}
      />

      {/* Delete Confirmation */}
      <ConfirmDialog
        open={!!deletingAnnouncement}
        onOpenChange={(open) => !open && setDeletingAnnouncement(null)}
        title={`Delete "${deletingAnnouncement?.title}"?`}
        description="This action cannot be undone."
        confirmLabel="Delete"
        destructive
        isPending={deleteMutation.isPending}
        onConfirm={handleDelete}
      />
    </div>
  );
}

// ============================================================================
// Form Dialog (Create + Edit)
// ============================================================================

interface AnnouncementForm {
  title: string;
  body: string;
  isImportant: boolean;
  channels: AnnouncementChannel[];
  /** Raw input text, so an emptied box is not read as 0 while retyping. */
  retentionDays: string;
  expiresAt: string;
}

function AnnouncementFormDialog({
  open,
  announcement,
  onOpenChange,
  onSaved,
}: {
  open: boolean;
  announcement: Announcement | null;
  onOpenChange: (open: boolean) => void;
  onSaved: () => void;
}) {
  const isEdit = !!announcement;

  const mutation = useSaveAnnouncement();
  const { form, set, setForm, isDirty, error, submit, isSubmitting } = useEntityFormDialog({
    open,
    onOpenChange,
    entity: announcement,
    emptyForm: (): AnnouncementForm => ({
      title: '',
      body: '',
      isImportant: false,
      channels: ['site'],
      retentionDays: '90',
      expiresAt: '',
    }),
    toForm: (a: Announcement): AnnouncementForm => ({
      title: a.title,
      body: a.body,
      isImportant: a.isImportant,
      // Channels and retention are chosen at creation only; the edit form does not show them.
      channels: ['site'],
      retentionDays: '90',
      // Format for datetime-local input
      expiresAt: a.expiresAt ? toDateTimeLocalValue(a.expiresAt) : '',
    }),
    mutation,
    toVariables: (f: AnnouncementForm, a: Announcement | null) => {
      if (a) {
        return {
          id: a.id,
          data: {
            title: f.title,
            body: f.body,
            isImportant: f.isImportant,
            expiresAt: f.expiresAt ? new Date(f.expiresAt).toISOString() : undefined,
          },
        };
      }
      const days = parseInt(f.retentionDays, 10);
      return {
        id: null,
        data: {
          title: f.title,
          body: f.body,
          isImportant: f.isImportant,
          retentionDays: isNaN(days) ? 90 : days,
          channels: f.channels,
        },
      };
    },
    onSaved,
  });
  const { title, body, isImportant, channels, retentionDays, expiresAt } = form;

  const toggleChannel = (channel: AnnouncementChannel, checked: boolean) =>
    setForm((prev) => ({
      ...prev,
      channels: checked
        ? [...new Set([...prev.channels, channel])]
        : prev.channels.filter((c) => c !== channel),
    }));

  return (
    <FormDialog
      open={open}
      onOpenChange={onOpenChange}
      title={isEdit ? 'Edit Announcement' : 'New Announcement'}
      description={
        isEdit
          ? 'Update the announcement. Editing increments the revision, marking it as unread for all users.'
          : 'Create a platform-wide announcement visible to all users.'
      }
      error={error}
      dirty={isDirty}
      onSubmit={submit}
      isSubmitting={isSubmitting}
      submitLabel={isEdit ? 'Save Changes' : 'Create'}
      submittingLabel="Saving…"
      submitDisabled={!title.trim() || !body.trim() || (!isEdit && channels.length === 0)}
    >
    <div className="grid gap-4 py-4">
      <div className="grid gap-2">
        <Label htmlFor="ann-title">Title</Label>
        <Input
          id="ann-title"
          value={title}
          onChange={(e) => set({ title: e.target.value })}
          placeholder="Scheduled maintenance on Friday"
          maxLength={200}
        />
      </div>

      <div className="grid gap-2">
        <Label htmlFor="ann-body">Body</Label>
        <Textarea
          id="ann-body"
          value={body}
          onChange={(e) => set({ body: e.target.value })}
          placeholder="Details about the announcement…"
          rows={5}
          maxLength={5000}
        />
        <p className="text-xs text-muted-foreground text-right">{body.length} / 5000</p>
      </div>

      <div className="flex items-start gap-3">
        <Switch
          id="ann-important"
          checked={isImportant}
          onCheckedChange={(checked) => set({ isImportant: checked })}
          className="mt-0.5"
        />
        <Label htmlFor="ann-important" className="cursor-pointer font-normal">
          Mark as important
          <span className="block text-xs text-muted-foreground">
            Emailed to all users, even those who opted out of announcement emails.
          </span>
        </Label>
      </div>

      {!isEdit && (
        <div className="grid gap-2">
          <Label>Delivery channels</Label>
          {CHANNEL_OPTIONS.map((opt) => (
            <div key={opt.value} className="flex items-center gap-3">
              <Checkbox
                id={`ann-channel-${opt.value}`}
                checked={channels.includes(opt.value)}
                onCheckedChange={(checked) => toggleChannel(opt.value, checked === true)}
              />
              <Label htmlFor={`ann-channel-${opt.value}`} className="cursor-pointer font-normal">
                {opt.label}
                <span className="ml-2 text-xs text-muted-foreground">{opt.hint}</span>
              </Label>
            </div>
          ))}
        </div>
      )}

      {isEdit ? (
        <div className="grid gap-2">
          <Label htmlFor="ann-expires">Expires At</Label>
          <DateTimePicker
            id="ann-expires"
            value={expiresAt}
            onChange={(value) => set({ expiresAt: value })}
            placeholder="Pick expiration date & time"
          />
        </div>
      ) : (
        <div className="grid gap-2">
          <Label htmlFor="ann-retention">Retention (days)</Label>
          <Input
            id="ann-retention"
            type="number"
            min={1}
            max={3650}
            value={retentionDays}
            onChange={(e) => set({ retentionDays: e.target.value })}
          />
          <p className="text-xs text-muted-foreground">
            Announcement expires after this many days. Default: 90.
          </p>
        </div>
      )}

    </div>
    </FormDialog>
  );
}
