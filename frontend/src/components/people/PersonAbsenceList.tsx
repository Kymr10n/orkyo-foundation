import { useState, useMemo } from 'react';
import { useQuery, useQueries, useMutation, useQueryClient } from '@tanstack/react-query';
import { Button } from '@foundation/src/components/ui/button';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@foundation/src/components/ui/select';
import { Plus, Trash2, Loader2 } from 'lucide-react';
import { getSites } from '@foundation/src/lib/api/site-api';
import { getResources } from '@foundation/src/lib/api/resources-api';
import { getResourceAbsences, deleteResourceAbsence, type ResourceAbsenceInfo } from '@foundation/src/lib/api/resource-absences-api';
import { PersonAbsenceEditDialog } from './PersonAbsenceEditDialog';
import { format } from 'date-fns';

const ABSENCE_TYPE_LABELS: Record<string, string> = {
  vacation: 'Vacation',
  sick_leave: 'Sick Leave',
  unavailable: 'Unavailable',
  training: 'Training',
  public_holiday: 'Public Holiday',
  other: 'Other',
  custom: 'Custom',
};

interface AbsenceRow extends ResourceAbsenceInfo {
  personName: string;
  personId: string;
}

export function PersonAbsenceList() {
  const queryClient = useQueryClient();
  const [selectedSiteId, setSelectedSiteId] = useState<string>('');
  const [isDialogOpen, setIsDialogOpen] = useState(false);

  const { data: sites = [] } = useQuery({
    queryKey: ['sites'],
    queryFn: getSites,
  });

  const { data: people = [] } = useQuery({
    queryKey: ['resources', 'person'],
    queryFn: () => getResources({ resourceTypeKey: 'person' }),
    enabled: !!selectedSiteId,
    select: (d) => d.data,
  });

  const absenceQueries = useQueries({
    queries: people.map((person) => ({
      queryKey: ['resource-absences', person.id, selectedSiteId],
      queryFn: () => getResourceAbsences(person.id, selectedSiteId),
      enabled: !!selectedSiteId,
    })),
  });

  const absenceRows = useMemo<AbsenceRow[]>(() => {
    const rows: AbsenceRow[] = [];
    absenceQueries.forEach((q, i) => {
      const person = people[i];
      if (!person || !q.data) return;
      q.data.forEach((absence) =>
        rows.push({ ...absence, personName: person.name, personId: person.id }),
      );
    });
    return rows.sort((a, b) => a.startTs.localeCompare(b.startTs));
  }, [absenceQueries, people]);

  const isLoadingAbsences = absenceQueries.some((q) => q.isLoading);

  const deleteMutation = useMutation({
    mutationFn: ({ personId, absenceId }: { personId: string; absenceId: string }) =>
      deleteResourceAbsence(personId, absenceId),
    onSuccess: () =>
      queryClient.invalidateQueries({ queryKey: ['resource-absences'] }),
  });

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between gap-4">
        <div className="flex items-center gap-2">
          <span className="text-sm font-medium">Site:</span>
          <Select value={selectedSiteId} onValueChange={setSelectedSiteId}>
            <SelectTrigger className="w-48">
              <SelectValue placeholder="Select a site" />
            </SelectTrigger>
            <SelectContent>
              {sites.map((site) => (
                <SelectItem key={site.id} value={site.id}>
                  {site.name}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>
        <Button onClick={() => setIsDialogOpen(true)} disabled={!selectedSiteId}>
          <Plus className="h-4 w-4 mr-2" />
          Add Absence
        </Button>
      </div>

      <div className="border rounded-lg overflow-hidden">
        {!selectedSiteId ? (
          <div className="text-center py-8 text-muted-foreground">
            Select a site to view absences.
          </div>
        ) : isLoadingAbsences ? (
          <div className="flex items-center justify-center p-8">
            <Loader2 className="h-5 w-5 animate-spin text-muted-foreground" />
          </div>
        ) : (
          <table className="w-full">
            <thead className="bg-muted">
              <tr>
                <th className="text-left p-4 font-medium">Person</th>
                <th className="text-left p-4 font-medium">Type</th>
                <th className="text-left p-4 font-medium">Start</th>
                <th className="text-left p-4 font-medium">End</th>
                <th className="text-left p-4 font-medium">Reason</th>
                <th className="text-right p-4 font-medium">Actions</th>
              </tr>
            </thead>
            <tbody>
              {absenceRows.length === 0 ? (
                <tr>
                  <td className="p-4" colSpan={6}>
                    <div className="text-center py-4 text-muted-foreground">
                      No absences recorded for this site.
                    </div>
                  </td>
                </tr>
              ) : (
                absenceRows.map((row) => (
                  <tr key={row.id} className="border-t hover:bg-muted/50">
                    <td className="p-4 font-medium">{row.personName}</td>
                    <td className="p-4">{ABSENCE_TYPE_LABELS[row.type] ?? row.type}</td>
                    <td className="p-4">{format(new Date(row.startTs), 'PP')}</td>
                    <td className="p-4">{format(new Date(row.endTs), 'PP')}</td>
                    <td className="p-4 text-muted-foreground">{row.title}</td>
                    <td className="p-4">
                      <div className="flex justify-end">
                        <Button
                          variant="ghost"
                          size="icon"
                          onClick={() =>
                            deleteMutation.mutate({ personId: row.personId, absenceId: row.id })
                          }
                          disabled={deleteMutation.isPending}
                          aria-label={`Delete absence for ${row.personName}`}
                        >
                          <Trash2 className="h-4 w-4" />
                        </Button>
                      </div>
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        )}
      </div>

      {selectedSiteId && (
        <PersonAbsenceEditDialog
          siteId={selectedSiteId}
          isOpen={isDialogOpen}
          onClose={() => setIsDialogOpen(false)}
          onSaved={() => {
            queryClient.invalidateQueries({ queryKey: ['resource-absences'] });
            setIsDialogOpen(false);
          }}
        />
      )}
    </div>
  );
}
