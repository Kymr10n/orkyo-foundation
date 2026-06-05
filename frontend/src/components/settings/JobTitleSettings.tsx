import { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { Plus, Pencil, Trash2 } from 'lucide-react';
import { Button } from '@foundation/src/components/ui/button';
import { Badge } from '@foundation/src/components/ui/badge';
import { OrkyoDataTable, type ColumnDef } from '@foundation/src/components/ui/OrkyoDataTable';
import { SettingsPageHeader } from './SettingsPageHeader';
import { JobTitleEditDialog } from './JobTitleEditDialog';
import {
  getJobTitles,
  deleteJobTitle,
  type JobTitleInfo,
} from '@foundation/src/lib/api/job-titles-api';

export function JobTitleSettings() {
  const [editing, setEditing] = useState<JobTitleInfo | null>(null);
  const [createOpen, setCreateOpen] = useState(false);
  const [includeInactive, setIncludeInactive] = useState(false);
  const queryClient = useQueryClient();

  const { data: jobTitles = [], isLoading, error } = useQuery({
    queryKey: ['job-titles', { includeInactive }],
    queryFn: () => getJobTitles(includeInactive),
  });

  const deleteMutation = useMutation({
    mutationFn: (id: string) => deleteJobTitle(id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['job-titles'] }),
  });

  const handleDelete = (jt: JobTitleInfo) => {
    if (!confirm(`Delete job title "${jt.name}"? People assigned to this title will be unlinked.`)) return;
    deleteMutation.mutate(jt.id);
  };

  const columns: ColumnDef<JobTitleInfo>[] = [
    {
      accessorKey: 'name',
      header: 'Name',
      cell: ({ row }) => (
        <div className="flex items-center gap-2">
          <span className="font-medium">{row.original.name}</span>
          {!row.original.isActive && <Badge variant="outline">Inactive</Badge>}
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
      cell: ({ row }) => (
        <div className="flex justify-end gap-1">
          <Button variant="ghost" size="icon" onClick={() => setEditing(row.original)}>
            <Pencil className="h-4 w-4" />
          </Button>
          <Button
            variant="ghost"
            size="icon"
            onClick={() => handleDelete(row.original)}
            className="text-destructive hover:text-destructive"
          >
            <Trash2 className="h-4 w-4" />
          </Button>
        </div>
      ),
    },
  ];

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
        emptyMessage="No job titles defined yet."
        filterColumn="name"
        filterPlaceholder="Search job titles..."
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
    </div>
  );
}
