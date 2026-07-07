import { useState } from 'react';
import { useQuery, useMutation } from '@tanstack/react-query';
import { Plus, Pencil, Trash2 } from 'lucide-react';
import { Button } from '@foundation/src/components/ui/button';
import { StatusBadge } from '@foundation/src/components/ui/status-badge';
import { OrkyoDataTable, type ColumnDef } from '@foundation/src/components/ui/OrkyoDataTable';
import { ConfirmDialog } from '@foundation/src/components/ui/ConfirmDialog';
import { SettingsPageHeader } from './SettingsPageHeader';
import { JobTitleEditDialog } from './JobTitleEditDialog';
import {
  getJobTitles,
  deleteJobTitle,
  type JobTitleInfo,
} from '@foundation/src/lib/api/job-titles-api';
import { qk } from '@foundation/src/lib/api/query-keys';

export function JobTitleSettings() {
  const [editing, setEditing] = useState<JobTitleInfo | null>(null);
  const [createOpen, setCreateOpen] = useState(false);
  const [includeInactive, setIncludeInactive] = useState(false);
  const [deletingJobTitle, setDeletingJobTitle] = useState<JobTitleInfo | null>(null);

  const { data: jobTitles = [], isLoading, error, refetch } = useQuery({
    queryKey: qk.jobTitles.list(includeInactive),
    queryFn: () => getJobTitles(includeInactive),
  });

  const deleteMutation = useMutation({
    mutationFn: (id: string) => deleteJobTitle(id),
    meta: {
      successMessage: 'Job title deleted',
      errorMessage: 'Failed to delete job title',
      invalidates: [qk.jobTitles.all()],
    },
    onSuccess: () => setDeletingJobTitle(null),
  });

  const handleDelete = (jt: JobTitleInfo) => setDeletingJobTitle(jt);
  const handleConfirmDelete = () => {
    if (deletingJobTitle) deleteMutation.mutate(deletingJobTitle.id);
  };

  const renderActions = (jt: JobTitleInfo) => (
    <div className="flex justify-end gap-1">
      <Button variant="ghost" size="icon" onClick={() => setEditing(jt)} aria-label={`Edit ${jt.name}`}>
        <Pencil className="h-4 w-4" />
      </Button>
      <Button
        variant="ghost"
        size="icon"
        onClick={() => handleDelete(jt)}
        className="text-destructive hover:text-destructive"
        aria-label={`Delete ${jt.name}`}
      >
        <Trash2 className="h-4 w-4" />
      </Button>
    </div>
  );

  const columns: ColumnDef<JobTitleInfo>[] = [
    {
      accessorKey: 'name',
      header: 'Name',
      cell: ({ row }) => (
        <div className="flex items-center gap-2">
          <span className="font-medium">{row.original.name}</span>
          {!row.original.isActive && <StatusBadge status="inactive" label="Inactive" />}
        </div>
      ),
    },
    {
      accessorKey: 'description',
      header: 'Description',
      cell: ({ row }) => (
        <span className="text-muted-foreground text-sm">
          {row.original.description || '—'}
        </span>
      ),
    },
    {
      id: 'actions',
      header: () => null,
      size: 100,
      cell: ({ row }) => renderActions(row.original),
    },
  ];

  // Shared row actions — desktop table cell and phone card.
  const renderCard = (jt: JobTitleInfo) => (
    <div className="flex items-start justify-between gap-2">
      <div className="min-w-0 space-y-1">
        <div className="flex items-center gap-2">
          <span className="font-medium truncate">{jt.name}</span>
          {!jt.isActive && <StatusBadge status="inactive" label="Inactive" />}
        </div>
        <p className="text-sm text-muted-foreground">{jt.description || '—'}</p>
      </div>
      {renderActions(jt)}
    </div>
  );

  const errorMsg = error instanceof Error ? error.message : error ? 'Failed to load job titles' : null;

  return (
    <div className="space-y-6">
      <SettingsPageHeader
        title="Job Titles"
        description="Tenant-wide reference data assigned to person resources. Job titles classify people by their organizational role."
      >
        <Button onClick={() => setCreateOpen(true)}>
          <Plus className="h-4 w-4 mr-2" />
          Add Job Title
        </Button>
      </SettingsPageHeader>

      <div className="flex items-center gap-2">
        <input
          id="include-inactive-jt"
          type="checkbox"
          checked={includeInactive}
          onChange={(e) => setIncludeInactive(e.target.checked)}
        />
        <label htmlFor="include-inactive-jt" className="text-sm text-muted-foreground">
          Show inactive
        </label>
      </div>

      <OrkyoDataTable
        columns={columns}
        data={jobTitles}
        isLoading={isLoading}
        error={errorMsg}
        onRetry={() => refetch()}
        emptyMessage="No job titles defined yet."
        filterColumn="name"
        filterPlaceholder="Search job titles..."
        renderCard={renderCard}
      />

      <JobTitleEditDialog
        jobTitle={null}
        open={createOpen}
        onOpenChange={setCreateOpen}
      />
      {editing && (
        <JobTitleEditDialog
          jobTitle={editing}
          open={!!editing}
          onOpenChange={(open) => !open && setEditing(null)}
        />
      )}

      <ConfirmDialog
        open={!!deletingJobTitle}
        onOpenChange={(open) => !open && setDeletingJobTitle(null)}
        title={`Delete "${deletingJobTitle?.name}"?`}
        description="People assigned to this job title will be unlinked."
        confirmLabel="Delete"
        destructive
        isPending={deleteMutation.isPending}
        onConfirm={handleConfirmDelete}
      />
    </div>
  );
}
