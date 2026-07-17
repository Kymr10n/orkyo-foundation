/**
 * FeedbackTab – Site-admin triage of user feedback (bug reports / feature requests / questions).
 *
 * Lists feedback (filterable by status), opens a detail dialog, and lets a site-admin change status,
 * edit admin notes, and attach a GitHub issue URL. Mirrors the AnnouncementsTab pattern.
 */

import { useCallback, useEffect, useState } from 'react';
import { formatDateDisplay } from '@foundation/src/lib/formatters';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@foundation/src/components/ui/card';
import { ErrorAlert } from '@foundation/src/components/ui/ErrorAlert';
import { Badge } from '@foundation/src/components/ui/badge';
import { Button } from '@foundation/src/components/ui/button';
import { Input } from '@foundation/src/components/ui/input';
import { Label } from '@foundation/src/components/ui/label';
import { Textarea } from '@foundation/src/components/ui/textarea';
import { OrkyoDataTable, type ColumnDef } from '@foundation/src/components/ui/OrkyoDataTable';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@foundation/src/components/ui/select';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@foundation/src/components/ui/dialog';
import { MessageSquare, Eye } from 'lucide-react';
import { LoadingSpinner } from '@foundation/src/components/ui/LoadingSpinner';
import { toast } from 'sonner';
import { useMutation } from '@tanstack/react-query';
import {
  type FeedbackSummary,
  type FeedbackDetail,
  type FeedbackStatus,
  getFeedback,
  getFeedbackItem,
  updateFeedback,
} from '@foundation/src/lib/api/feedback-admin-api';

const STATUSES: FeedbackStatus[] = ['new', 'reviewed', 'resolved', 'wont_fix'];

const STATUS_VARIANT: Record<FeedbackStatus, 'default' | 'secondary' | 'destructive' | 'success' | 'outline'> = {
  new: 'secondary',
  reviewed: 'default',
  resolved: 'success',
  wont_fix: 'outline',
};

const STATUS_LABEL: Record<FeedbackStatus, string> = {
  new: 'New',
  reviewed: 'Reviewed',
  resolved: 'Resolved',
  wont_fix: "Won't fix",
};

export function FeedbackTab() {
  const [items, setItems] = useState<FeedbackSummary[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [statusFilter, setStatusFilter] = useState<string>('all');
  const [selected, setSelected] = useState<FeedbackDetail | null>(null);

  // Deliberate manual load (not useQuery) for this operator surface — see dialog-feedback.md rule 3.
  const load = useCallback(async (status: string) => {
    try {
      setError(null);
      const response = await getFeedback(status === 'all' ? undefined : { status });
      setItems(response.items);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load feedback');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    load(statusFilter);
  }, [load, statusFilter]);

  const openDetail = async (id: string) => {
    try {
      setSelected(await getFeedbackItem(id));
    } catch (err) {
      toast.error(err instanceof Error ? err.message : 'Failed to load feedback');
    }
  };

  const columns: ColumnDef<FeedbackSummary>[] = [
    {
      accessorKey: 'title',
      header: 'Title',
      cell: ({ row }) => <span className="font-medium truncate max-w-[280px] block">{row.original.title}</span>,
    },
    {
      id: 'type',
      header: 'Type',
      cell: ({ row }) => <Badge variant="outline" className="capitalize">{row.original.feedbackType}</Badge>,
    },
    {
      id: 'submitter',
      header: 'From',
      cell: ({ row }) => (
        <span className="text-sm text-muted-foreground whitespace-nowrap">
          {row.original.submitterEmail ?? '—'}
          {row.original.tenantName ? <span className="text-xs"> · {row.original.tenantName}</span> : null}
        </span>
      ),
    },
    {
      id: 'status',
      header: 'Status',
      cell: ({ row }) => <Badge variant={STATUS_VARIANT[row.original.status]}>{STATUS_LABEL[row.original.status]}</Badge>,
    },
    {
      id: 'created',
      header: 'Created',
      cell: ({ row }) => (
        <span className="text-sm text-muted-foreground whitespace-nowrap">
          {formatDateDisplay(row.original.createdAt)}
        </span>
      ),
    },
    {
      id: 'actions',
      header: () => null,
      size: 64,
      cell: ({ row }) => (
        <div className="flex items-center justify-end">
          <Button
            variant="ghost"
            size="icon"
            className="h-8 w-8"
            onClick={(e) => { e.stopPropagation(); openDetail(row.original.id); }}
            aria-label={`Review ${row.original.title}`}
          >
            <Eye className="h-4 w-4" />
          </Button>
        </div>
      ),
    },
  ];

  // Phone presentation: title + type/status/submitter stacked, review trailing.
  const renderCard = (item: FeedbackSummary) => (
    <div className="flex items-start justify-between gap-2">
      <div className="min-w-0 space-y-1">
        <p className="font-medium truncate">{item.title}</p>
        <div className="flex flex-wrap items-center gap-1.5">
          <Badge variant="outline" className="capitalize">{item.feedbackType}</Badge>
          <Badge variant={STATUS_VARIANT[item.status]}>{STATUS_LABEL[item.status]}</Badge>
        </div>
        <p className="text-xs text-muted-foreground truncate">
          {item.submitterEmail ?? '—'}{item.tenantName ? ` · ${item.tenantName}` : ''} · {formatDateDisplay(item.createdAt)}
        </p>
      </div>
      <Button
        variant="ghost"
        size="icon"
        className="h-8 w-8 shrink-0"
        onClick={(e) => { e.stopPropagation(); openDetail(item.id); }}
        aria-label={`Review ${item.title}`}
      >
        <Eye className="h-4 w-4" />
      </Button>
    </div>
  );

  if (loading) {
    return (
      <Card>
        <CardContent className="flex items-center justify-center py-12">
          <LoadingSpinner size="sm" muted fullScreen={false} className="h-auto w-auto" />
          <span className="ml-2 text-muted-foreground">Loading feedback…</span>
        </CardContent>
      </Card>
    );
  }

  return (
    <div className="space-y-6">
      <Card>
        <CardHeader>
          <div className="flex items-center justify-between gap-4">
            <div>
              <CardTitle className="flex items-center gap-2">
                <MessageSquare className="h-5 w-5" />
                User Feedback
              </CardTitle>
              <CardDescription>Triage bug reports, feature requests, and questions from users.</CardDescription>
            </div>
            <Select value={statusFilter} onValueChange={setStatusFilter}>
              <SelectTrigger className="w-[180px]"><SelectValue /></SelectTrigger>
              <SelectContent>
                <SelectItem value="all">All statuses</SelectItem>
                {STATUSES.map((s) => (
                  <SelectItem key={s} value={s}>{STATUS_LABEL[s]}</SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
        </CardHeader>
        <CardContent>
          <div className="mb-4 empty:mb-0">
            <ErrorAlert message={error ?? null} />
          </div>
          <OrkyoDataTable
            columns={columns}
            data={items}
            filterColumn="title"
            filterPlaceholder="Search feedback…"
            emptyMessage="No feedback yet."
            renderCard={renderCard}
          />
        </CardContent>
      </Card>

      <FeedbackDetailDialog
        feedback={selected}
        onClose={() => setSelected(null)}
        onSaved={() => { setSelected(null); load(statusFilter); }}
      />
    </div>
  );
}

function FeedbackDetailDialog({
  feedback,
  onClose,
  onSaved,
}: {
  feedback: FeedbackDetail | null;
  onClose: () => void;
  onSaved: () => void;
}) {
  const [status, setStatus] = useState<FeedbackStatus>('new');
  const [notes, setNotes] = useState('');
  const [githubUrl, setGithubUrl] = useState('');

  const saveMutation = useMutation({
    mutationFn: (input: { id: string; status: FeedbackStatus; adminNotes: string; githubIssueUrl: string }) =>
      updateFeedback(input.id, { status: input.status, adminNotes: input.adminNotes, githubIssueUrl: input.githubIssueUrl }),
    // No `invalidates`: the tab loads manually (not useQuery), so there is no cached
    // consumer — onSaved() re-runs load() instead (dialog-feedback rule 3).
    meta: {
      successMessage: 'Feedback updated',
      errorMessage: 'Failed to update feedback',
    },
    onSuccess: () => onSaved(),
  });

  useEffect(() => {
    if (feedback) {
      setStatus(feedback.status);
      setNotes(feedback.adminNotes ?? '');
      setGithubUrl(feedback.githubIssueUrl ?? '');
    }
  }, [feedback]);

  if (!feedback) return null;

  const dirty =
    status !== feedback.status ||
    (notes ?? '') !== (feedback.adminNotes ?? '') ||
    (githubUrl ?? '') !== (feedback.githubIssueUrl ?? '');

  const handleSave = () => {
    saveMutation.mutate({ id: feedback.id, status, adminNotes: notes, githubIssueUrl: githubUrl });
  };

  const Field = ({ label, value }: { label: string; value: string | null }) => (
    <div>
      <div className="text-xs font-medium text-muted-foreground">{label}</div>
      <div className="text-sm whitespace-pre-wrap break-words">{value === null || value === '' ? '—' : value}</div>
    </div>
  );

  return (
    <Dialog open={!!feedback} onOpenChange={(open) => { if (!open) onClose(); }}>
      <DialogContent className="max-w-2xl max-h-[85vh] overflow-y-auto">
        <DialogHeader>
          <DialogTitle className="capitalize">{feedback.feedbackType}: {feedback.title}</DialogTitle>
          <DialogDescription>
            {(feedback.submitterEmail ?? 'unknown')}{feedback.tenantName ? ` · ${feedback.tenantName}` : ''}
          </DialogDescription>
        </DialogHeader>

        <Field label="Description" value={feedback.description} />
        <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
          <Field label="Page" value={feedback.pageUrl} />
          <Field label="User agent" value={feedback.userAgent} />
        </div>

        <div className="grid gap-2">
          <Label htmlFor="fb-status">Status</Label>
          <Select value={status} onValueChange={(v) => setStatus(v as FeedbackStatus)}>
            <SelectTrigger id="fb-status"><SelectValue /></SelectTrigger>
            <SelectContent>
              {STATUSES.map((s) => (
                <SelectItem key={s} value={s}>{STATUS_LABEL[s]}</SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>

        <div className="grid gap-2">
          <Label htmlFor="fb-notes">Admin notes</Label>
          <Textarea
            id="fb-notes"
            value={notes}
            onChange={(e) => setNotes(e.target.value)}
            placeholder="Internal notes"
            className="min-h-[80px]"
          />
        </div>

        <div className="grid gap-2">
          <Label htmlFor="fb-github">GitHub issue URL</Label>
          <Input
            id="fb-github"
            value={githubUrl}
            onChange={(e) => setGithubUrl(e.target.value)}
            placeholder="https://github.com/org/repo/issues/123"
          />
        </div>

        <DialogFooter>
          <Button variant="outline" onClick={onClose} disabled={saveMutation.isPending}>Cancel</Button>
          <Button onClick={handleSave} loading={saveMutation.isPending} disabled={saveMutation.isPending || !dirty}>
            Save
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
