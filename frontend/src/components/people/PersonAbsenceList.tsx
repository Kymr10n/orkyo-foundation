import { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { Button } from '@foundation/src/components/ui/button';
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
} from '@foundation/src/components/ui/dialog';
import { OrkyoDataTable } from '@foundation/src/components/ui/OrkyoDataTable';
import { Plus, Trash2 } from 'lucide-react';
import { getResourceAbsences, deleteResourceAbsence, type ResourceAbsenceInfo } from '@foundation/src/lib/api/resource-absences-api';
import { qk } from '@foundation/src/lib/api/query-keys';
import { PersonAbsenceEditDialog } from './PersonAbsenceEditDialog';
import { format } from 'date-fns';
import { DATE_FORMATS } from '@foundation/src/lib/formatters';

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
    queryKey: qk.resources.absences(personId),
    queryFn: () => getResourceAbsences(personId),
    enabled: open,
  });

  const deleteMutation = useMutation({
    mutationFn: (absenceId: string) => deleteResourceAbsence(personId, absenceId),
    meta: { invalidates: [qk.resources.absences(personId)] },
  });

  const renderDeleteButton = (absence: ResourceAbsenceInfo) => (
    <Button
      variant="ghost" size="icon"
      onClick={() => deleteMutation.mutate(absence.id)}
      disabled={deleteMutation.isPending}
      aria-label="Delete absence"
    >
      <Trash2 className="h-4 w-4" />
    </Button>
  );

  // Phone presentation: type + date range stacked, delete trailing.
  const renderCard = (absence: ResourceAbsenceInfo) => {
    const range = `${format(new Date(absence.startTs), DATE_FORMATS.DATE_LOCALE_SHORT)} – ${format(new Date(absence.endTs), DATE_FORMATS.DATE_LOCALE_SHORT)}`;
    return (
      <div className="flex items-start justify-between gap-2">
        <div className="min-w-0 space-y-0.5">
          <p className="font-medium truncate">{ABSENCE_TYPE_LABELS[absence.absenceType] ?? absence.absenceType}</p>
          <p className="text-sm text-muted-foreground truncate">{range}</p>
          {absence.title && <p className="text-xs text-muted-foreground truncate">{absence.title}</p>}
        </div>
        {renderDeleteButton(absence)}
      </div>
    );
  };

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

          <OrkyoDataTable<ResourceAbsenceInfo>
            columns={[
              {
                accessorKey: 'absenceType',
                header: 'Type',
                cell: ({ getValue }) => ABSENCE_TYPE_LABELS[getValue<string>()] ?? getValue<string>(),
              },
              {
                accessorKey: 'startTs',
                header: 'Start',
                cell: ({ getValue }) => format(new Date(getValue<string>()), DATE_FORMATS.DATE_LOCALE_SHORT),
              },
              {
                accessorKey: 'endTs',
                header: 'End',
                cell: ({ getValue }) => format(new Date(getValue<string>()), DATE_FORMATS.DATE_LOCALE_SHORT),
              },
              {
                accessorKey: 'title',
                header: 'Reason',
                cell: ({ getValue }) => <span className="text-muted-foreground">{getValue<string>()}</span>,
              },
              {
                id: 'actions',
                header: () => null,
                size: 56,
                cell: ({ row }) => (
                  <Button
                    variant="ghost" size="icon"
                    onClick={() => deleteMutation.mutate(row.original.id)}
                    disabled={deleteMutation.isPending}
                    aria-label="Delete absence"
                  >
                    <Trash2 className="h-4 w-4" />
                  </Button>
                ),
              },
            ]}
            data={absences}
            isLoading={isLoading}
            emptyMessage="No absences recorded."
            renderCard={renderCard}
          />
        </div>

        <PersonAbsenceEditDialog
          personId={personId}
          isOpen={isAddOpen}
          onClose={() => setIsAddOpen(false)}
          onSaved={() => {
            queryClient.invalidateQueries({ queryKey: qk.resources.absences(personId) });
            setIsAddOpen(false);
          }}
        />
      </DialogContent>
    </Dialog>
  );
}
