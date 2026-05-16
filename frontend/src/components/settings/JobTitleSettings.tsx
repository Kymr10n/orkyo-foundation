import { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { Plus, Pencil, Trash2, AlertCircle } from 'lucide-react';
import { Button } from '@foundation/src/components/ui/button';
import { Card } from '@foundation/src/components/ui/card';
import { Badge } from '@foundation/src/components/ui/badge';
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

  if (isLoading) {
    return <div className="text-muted-foreground">Loading job titles...</div>;
  }

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

      {error && (
        <div className="flex items-center gap-2 p-4 border border-destructive/50 bg-destructive/10 rounded-lg">
          <AlertCircle className="h-5 w-5 text-destructive" />
          <p className="text-sm text-destructive">
            {error instanceof Error ? error.message : 'Failed to load job titles'}
          </p>
        </div>
      )}

      {jobTitles.length === 0 ? (
        <Card className="p-12 text-center">
          <p className="text-muted-foreground mb-4">No job titles defined yet</p>
          <Button onClick={() => setCreateOpen(true)} variant="outline">
            <Plus className="h-4 w-4 mr-2" />
            Create your first job title
          </Button>
        </Card>
      ) : (
        <div className="grid gap-2">
          {jobTitles.map((jt) => (
            <Card key={jt.id} className="p-4 flex items-start justify-between">
              <div className="flex-1 min-w-0">
                <div className="flex items-center gap-2">
                  <h3 className="font-medium">{jt.name}</h3>
                  {!jt.isActive && <Badge variant="outline">Inactive</Badge>}
                </div>
                {jt.description && (
                  <p className="text-sm text-muted-foreground mt-1">{jt.description}</p>
                )}
              </div>
              <div className="flex gap-1 ml-4">
                <Button variant="ghost" size="icon" onClick={() => setEditing(jt)}>
                  <Pencil className="h-4 w-4" />
                </Button>
                <Button
                  variant="ghost"
                  size="icon"
                  onClick={() => handleDelete(jt)}
                  className="text-destructive hover:text-destructive"
                >
                  <Trash2 className="h-4 w-4" />
                </Button>
              </div>
            </Card>
          ))}
        </div>
      )}

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
