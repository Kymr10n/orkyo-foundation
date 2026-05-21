import { useState, useMemo } from 'react';
import { useQuery, useQueries, useMutation, useQueryClient } from '@tanstack/react-query';
import { Button } from '@foundation/src/components/ui/button';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@foundation/src/components/ui/select';
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
} from '@foundation/src/components/ui/dialog';
import { Plus, Trash2, Loader2 } from 'lucide-react';
import { getSites } from '@foundation/src/lib/api/site-api';
import { getResourceAbsences, deleteResourceAbsence } from '@foundation/src/lib/api/resource-absences-api';
import { PersonAbsenceEditDialog } from './PersonAbsenceEditDialog';
import { format } from 'date-fns';

const ABSENCE_TYPE_LABELS: Record<string, string> = {
  vacation: 'Vacation',
  sick_leave: 'Sick Leave',
  unavailable: 'Unavailable',
  training: 'Training',
  custom: 'Custom',
  holiday: 'Holiday',
  maintenance: 'Maintenance',
};

interface PersonAbsenceListProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  personId: string;
  personName: string;
}

export function PersonAbsenceList({ open, onOpenChange, personId, personName }: PersonAbsenceListProps) {
  const queryClient = useQueryClient();
  const [selectedSiteId, setSelectedSiteId] = useState<string>('');
  const [isAddOpen, setIsAddOpen] = useState(false);

  const { data: sites = [] } = useQuery({
    queryKey: ['sites'],
    queryFn: getSites,
    enabled: open,
  });

  const absenceQueries = useQueries({
    queries: sites.map((site) => ({
      queryKey: ['resource-absences', personId, site.id],
      queryFn: () => getResourceAbsences(personId, site.id),
      enabled: open,
    })),
  });

  const absenceRows = useMemo(() => {
    const rows: ({ siteId: string; siteName: string } & Awaited<ReturnType<typeof getResourceAbsences>>[number])[] = [];
    absenceQueries.forEach((q, i) => {
      const site = sites[i];
      if (!site || !q.data) return;
      q.data.forEach((absence) => rows.push({ ...absence, siteId: site.id, siteName: site.name }));
    });
    return rows.sort((a, b) => a.startTs.localeCompare(b.startTs));
  }, [absenceQueries, sites]);

  const isLoading = absenceQueries.some((q) => q.isLoading) && absenceRows.length === 0;

  const deleteMutation = useMutation({
    mutationFn: (absenceId: string) => deleteResourceAbsence(personId, absenceId),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['resource-absences', personId] }),
  });

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-[640px]">
        <DialogHeader>
          <DialogTitle>Absences — {personName}</DialogTitle>
        </DialogHeader>

        <div className="space-y-4">
          <div className="flex items-center justify-between gap-4">
            <div className="flex items-center gap-2">
              <span className="text-sm font-medium shrink-0">Add to site:</span>
              <Select value={selectedSiteId} onValueChange={setSelectedSiteId}>
                <SelectTrigger className="w-44">
                  <SelectValue placeholder="Select site" />
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
            <Button size="sm" onClick={() => setIsAddOpen(true)} disabled={!selectedSiteId}>
              <Plus className="h-4 w-4 mr-2" />
              Add Absence
            </Button>
          </div>

          <div className="border rounded-lg overflow-hidden">
            {isLoading ? (
              <div className="flex items-center justify-center p-8">
                <Loader2 className="h-5 w-5 animate-spin text-muted-foreground" />
              </div>
            ) : absenceRows.length === 0 ? (
              <div className="text-center py-8 text-muted-foreground text-sm">
                No absences recorded.
              </div>
            ) : (
              <table className="w-full text-sm">
                <thead className="bg-muted">
                  <tr>
                    <th className="text-left p-3 font-medium">Type</th>
                    <th className="text-left p-3 font-medium">Start</th>
                    <th className="text-left p-3 font-medium">End</th>
                    <th className="text-left p-3 font-medium">Reason</th>
                    <th className="text-left p-3 font-medium">Site</th>
                    <th className="p-3" />
                  </tr>
                </thead>
                <tbody>
                  {absenceRows.map((row) => (
                    <tr key={row.id} className="border-t hover:bg-muted/50">
                      <td className="p-3">{ABSENCE_TYPE_LABELS[row.type] ?? row.type}</td>
                      <td className="p-3">{format(new Date(row.startTs), 'PP')}</td>
                      <td className="p-3">{format(new Date(row.endTs), 'PP')}</td>
                      <td className="p-3 text-muted-foreground">{row.title}</td>
                      <td className="p-3 text-muted-foreground">{row.siteName}</td>
                      <td className="p-3 text-right">
                        <Button
                          variant="ghost"
                          size="icon"
                          onClick={() => deleteMutation.mutate(row.id)}
                          disabled={deleteMutation.isPending}
                          aria-label={`Delete absence`}
                        >
                          <Trash2 className="h-4 w-4" />
                        </Button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </div>
        </div>

        {selectedSiteId && (
          <PersonAbsenceEditDialog
            personId={personId}
            siteId={selectedSiteId}
            isOpen={isAddOpen}
            onClose={() => setIsAddOpen(false)}
            onSaved={() => {
              queryClient.invalidateQueries({ queryKey: ['resource-absences', personId] });
              setIsAddOpen(false);
            }}
          />
        )}
      </DialogContent>
    </Dialog>
  );
}
