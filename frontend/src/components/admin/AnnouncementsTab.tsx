/**
 * AnnouncementsTab – Admin tab for managing platform-wide announcements.
 *
 * Provides a list of all announcements (active + expired) with inline
 * create / edit / delete capabilities.
 */

import { useEffect, useState, useCallback } from 'react';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@foundation/src/components/ui/card';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@foundation/src/components/ui/table';
import { Badge } from '@foundation/src/components/ui/badge';
import { Button } from '@foundation/src/components/ui/button';
import { Input } from '@foundation/src/components/ui/input';
import { Label } from '@foundation/src/components/ui/label';
import { Textarea } from '@foundation/src/components/ui/textarea';
import { Switch } from '@foundation/src/components/ui/switch';
import { DateTimePicker } from '@foundation/src/components/ui/date-time-picker';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@foundation/src/components/ui/dialog';
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from '@foundation/src/components/ui/alert-dialog';
import { Plus, Pencil, Trash2, Megaphone, AlertTriangle } from 'lucide-react';
import {
  type Announcement,
  type CreateAnnouncementRequest,
  type UpdateAnnouncementRequest,
  getAnnouncements,
  createAnnouncement,
  updateAnnouncement,
  deleteAnnouncement,
} from '@foundation/src/lib/api/announcement-api';

// ============================================================================
// Main Tab
// ============================================================================

export function AnnouncementsTab() {
  const [announcements, setAnnouncements] = useState<Announcement[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // Dialog state
  const [showCreateDialog, setShowCreateDialog] = useState(false);
  const [editingAnnouncement, setEditingAnnouncement] = useState<Announcement | null>(null);
  const [deletingAnnouncement, setDeletingAnnouncement] = useState<Announcement | null>(null);

  const loadAnnouncements = useCallback(async () => {
    try {
      setError(null);
      const response = await getAnnouncements(true);
      setAnnouncements(response.announcements);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load announcements');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    loadAnnouncements();
  }, [loadAnnouncements]);

  const handleDelete = async () => {
    if (!deletingAnnouncement) return;
    try {
      await deleteAnnouncement(deletingAnnouncement.id);
      setDeletingAnnouncement(null);
      loadAnnouncements();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to delete announcement');
      setDeletingAnnouncement(null);
    }
  };

  if (loading) {
    return (
      <Card>
        <CardContent className="py-8">
          <div className="text-center text-muted-foreground">Loading announcements...</div>
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
          {error && (
            <div className="mb-4 p-3 rounded bg-destructive/10 text-destructive text-sm">
              {error}
            </div>
          )}

          {announcements.length === 0 ? (
            <div className="text-center py-8 text-muted-foreground">
              No announcements yet. Create one to get started.
            </div>
          ) : (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead className="w-[300px]">Title</TableHead>
                  <TableHead>Status</TableHead>
                  <TableHead>Created</TableHead>
                  <TableHead>Expires</TableHead>
                  <TableHead>Rev</TableHead>
                  <TableHead className="text-right">Actions</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {announcements.map((a) => (
                  <TableRow key={a.id} className={a.isExpired ? 'opacity-50' : ''}>
                    <TableCell>
                      <div className="flex items-center gap-2">
                        {a.isImportant && (
                          <AlertTriangle className="h-4 w-4 text-amber-500 shrink-0" />
                        )}
                        <span className="font-medium truncate max-w-[260px]">{a.title}</span>
                      </div>
                    </TableCell>
                    <TableCell>
                      {a.isExpired ? (
                        <Badge variant="secondary">Expired</Badge>
                      ) : a.isImportant ? (
                        <Badge variant="destructive">Important</Badge>
                      ) : (
                        <Badge>Active</Badge>
                      )}
                    </TableCell>
                    <TableCell className="text-sm text-muted-foreground whitespace-nowrap">
                      <div>{new Date(a.createdAt).toLocaleDateString()}</div>
                      <div className="text-xs">{a.createdByEmail}</div>
                    </TableCell>
                    <TableCell className="text-sm text-muted-foreground whitespace-nowrap">
                      {new Date(a.expiresAt).toLocaleDateString()}
                    </TableCell>
                    <TableCell className="text-sm text-muted-foreground">{a.revision}</TableCell>
                    <TableCell className="text-right">
                      <div className="flex items-center justify-end gap-1">
                        <Button
                          variant="ghost"
                          size="icon"
                          className="h-8 w-8"
                          onClick={() => setEditingAnnouncement(a)}
                        >
                          <Pencil className="h-4 w-4" />
                        </Button>
                        <Button
                          variant="ghost"
                          size="icon"
                          className="h-8 w-8 text-destructive hover:text-destructive"
                          onClick={() => setDeletingAnnouncement(a)}
                        >
                          <Trash2 className="h-4 w-4" />
                        </Button>
                      </div>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          )}
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
          loadAnnouncements();
        }}
      />

      {/* Delete Confirmation */}
      <AlertDialog open={!!deletingAnnouncement} onOpenChange={(o) => !o && setDeletingAnnouncement(null)}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Delete Announcement</AlertDialogTitle>
            <AlertDialogDescription>
              Are you sure you want to delete &ldquo;{deletingAnnouncement?.title}&rdquo;? This action cannot be undone.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>Cancel</AlertDialogCancel>
            <AlertDialogAction
              onClick={handleDelete}
              className="bg-destructive text-destructive-foreground hover:bg-destructive/90"
            >
              Delete
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </div>
  );
}

// ============================================================================
// Form Dialog (Create + Edit)
// ============================================================================

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

  const [title, setTitle] = useState('');
  const [body, setBody] = useState('');
  const [isImportant, setIsImportant] = useState(false);
  const [retentionDays, setRetentionDays] = useState('90');
  const [expiresAt, setExpiresAt] = useState('');
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // Populate form on open
  useEffect(() => {
    if (open) {
      if (announcement) {
        setTitle(announcement.title);
        setBody(announcement.body);
        setIsImportant(announcement.isImportant);
        // Format for datetime-local input
        setExpiresAt(announcement.expiresAt ? toLocalDatetimeString(announcement.expiresAt) : '');
      } else {
        setTitle('');
        setBody('');
        setIsImportant(false);
        setRetentionDays('90');
        setExpiresAt('');
      }
      setError(null);
    }
  }, [open, announcement]);

  const handleSubmit = async () => {
    try {
      setLoading(true);
      setError(null);

      if (isEdit) {
        const data: UpdateAnnouncementRequest = {
          title,
          body,
          isImportant,
          expiresAt: expiresAt ? new Date(expiresAt).toISOString() : undefined,
        };
        await updateAnnouncement(announcement.id, data);
      } else {
        const days = parseInt(retentionDays, 10);
        const data: CreateAnnouncementRequest = {
          title,
          body,
          isImportant,
          retentionDays: isNaN(days) ? 90 : days,
        };
        await createAnnouncement(data);
      }

      onSaved();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to save announcement');
    } finally {
      setLoading(false);
    }
  };

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-[540px]">
        <DialogHeader>
          <DialogTitle>{isEdit ? 'Edit Announcement' : 'New Announcement'}</DialogTitle>
          <DialogDescription>
            {isEdit
              ? 'Update the announcement. Editing increments the revision, marking it as unread for all users.'
              : 'Create a platform-wide announcement visible to all users.'}
          </DialogDescription>
        </DialogHeader>

        <div className="grid gap-4 py-4">
          <div className="grid gap-2">
            <Label htmlFor="ann-title">Title</Label>
            <Input
              id="ann-title"
              value={title}
              onChange={(e) => setTitle(e.target.value)}
              placeholder="Scheduled maintenance on Friday"
              maxLength={200}
            />
          </div>

          <div className="grid gap-2">
            <Label htmlFor="ann-body">Body</Label>
            <Textarea
              id="ann-body"
              value={body}
              onChange={(e) => setBody(e.target.value)}
              placeholder="Details about the announcement..."
              rows={5}
              maxLength={5000}
            />
            <p className="text-xs text-muted-foreground text-right">{body.length} / 5000</p>
          </div>

          <div className="flex items-center gap-3">
            <Switch
              id="ann-important"
              checked={isImportant}
              onCheckedChange={setIsImportant}
            />
            <Label htmlFor="ann-important" className="cursor-pointer">
              Mark as important
            </Label>
          </div>

          {isEdit ? (
            <div className="grid gap-2">
              <Label htmlFor="ann-expires">Expires At</Label>
              <DateTimePicker
                id="ann-expires"
                value={expiresAt}
                onChange={setExpiresAt}
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
                onChange={(e) => setRetentionDays(e.target.value)}
              />
              <p className="text-xs text-muted-foreground">
                Announcement expires after this many days. Default: 90.
              </p>
            </div>
          )}

          {error && <p className="text-sm text-destructive">{error}</p>}
        </div>

        <DialogFooter>
          <Button variant="outline" onClick={() => onOpenChange(false)}>
            Cancel
          </Button>
          <Button onClick={handleSubmit} disabled={loading || !title.trim() || !body.trim()}>
            {loading ? 'Saving...' : isEdit ? 'Save Changes' : 'Create'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

// ============================================================================
// Helpers
// ============================================================================

/** Converts an ISO date string to a `datetime-local` input value. */
function toLocalDatetimeString(iso: string): string {
  const d = new Date(iso);
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
}
