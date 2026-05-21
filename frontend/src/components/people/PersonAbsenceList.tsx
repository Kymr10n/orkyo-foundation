import { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { Button } from '@foundation/src/components/ui/button';
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
} from '@foundation/src/components/ui/dialog';
import { Plus, Trash2, Loader2 } from 'lucide-react';
import { getResourceAbsences, deleteResourceAbsence } from '@foundation/src/lib/api/resource-absences-api';
import { PersonAbsenceEditDialog } from './PersonAbsenceEditDialog';
import { format } from 'date-fns';

const ABSENCE_TYPE_LABELS: Record<string, string> = {
  vacation: 'Vacation',
  sickness: 'Sickness',
  unavailable: 'Unavailable',
  training: 'Training',
  maintenance: 'Maintenance',
  custom: 'Custom',
};

interface PersonAbsenceListProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  personId: string;
  personName: string;
}

export function PersonAbsenceList({ open, onOpenChange, personId, personName }: PersonAbsenceListProps) {
  const queryClient = useQueryClient();
  const [isAddOpen, setIsAddOpen] = useState(false);

  const { data: absences = [], isLoading } = useQuery({
    queryKey: ['resource-absences', personId],
    queryFn: () => getResourceAbsences(personId),
    enabled: open,
  });

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
          <div className="flex justify-end">
            <Button size="sm" onClick={() => setIsAddOpen(true)}>
              <Plus className="h-4 w-4 mr-2" />
              Add Absence
            </Button>
          </div>

          <div className="border rounded-lg overflow-hidden">
            {isLoading ? (
              <div className="flex items-center justify-center p-8">
                <Loader2 className="h-5 w-5 animate-spin text-muted-foreground" />
              </div>
            ) : absences.length === 0 ? (
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
                    <th className="p-3" />
                  </tr>
                </thead>
                <tbody>
                  {absences.map((absence) => (
                    <tr key={absence.id} className="border-t hover:bg-muted/50">
                      <td className="p-3">{ABSENCE_TYPE_LABELS[absence.absenceType] ?? absence.absenceType}</td>
                      <td className="p-3">{format(new Date(absence.startTs), 'PP')}</td>
                      <td className="p-3">{format(new Date(absence.endTs), 'PP')}</td>
                      <td className="p-3 text-muted-foreground">{absence.title}</td>
                      <td className="p-3 text-right">
                        <Button
                          variant="ghost"
                          size="icon"
                          onClick={() => deleteMutation.mutate(absence.id)}
                          disabled={deleteMutation.isPending}
                          aria-label="Delete absence"
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

        <PersonAbsenceEditDialog
          personId={personId}
          isOpen={isAddOpen}
          onClose={() => setIsAddOpen(false)}
          onSaved={() => {
            queryClient.invalidateQueries({ queryKey: ['resource-absences', personId] });
            setIsAddOpen(false);
          }}
        />
      </DialogContent>
    </Dialog>
  );
}
