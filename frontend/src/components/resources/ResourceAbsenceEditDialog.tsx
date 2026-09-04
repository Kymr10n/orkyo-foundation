import { useState } from 'react';
import { useMutation } from '@tanstack/react-query';
import {
  createResourceAbsence,
  updateResourceAbsence,
  type AbsenceType,
  type ResourceAbsenceInfo,
} from '@foundation/src/lib/api/resource-absences-api';
import { qk } from '@foundation/src/lib/api/query-keys';
import { FormDialog } from '@foundation/src/components/ui/FormDialog';
import { Button } from '@foundation/src/components/ui/button'; // date-picker popover triggers
import { Input } from '@foundation/src/components/ui/input';
import { Label } from '@foundation/src/components/ui/label';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@foundation/src/components/ui/select';
import { Calendar as CalendarIcon } from 'lucide-react';
import { Popover, PopoverContent, PopoverTrigger } from '@foundation/src/components/ui/popover';
import { Calendar } from '@foundation/src/components/ui/calendar';
import { format } from 'date-fns';
import { DATE_FORMATS } from '@foundation/src/lib/formatters';

interface PersonAbsenceEditDialogProps {
  resourceId: string;
  isOpen: boolean;
  /** Omit to record a new absence; pass one to edit it. */
  absence?: ResourceAbsenceInfo;
  /** Dates a drag on the schedule already chose, so the form does not ask for them again. */
  initialStart?: Date;
  initialEnd?: Date;
  /** What this kind of resource is usually blocked for — a machine goes down for maintenance,
   *  a person takes leave. The caller knows the resource; the dialog does not. */
  defaultAbsenceType?: AbsenceType;
  onClose: () => void;
  onSaved: () => void;
}

const absenceTypes: { value: AbsenceType; label: string }[] = [
  { value: 'vacation', label: 'Vacation' },
  { value: 'sickness', label: 'Sickness' },
  { value: 'unavailable', label: 'Unavailable' },
  { value: 'training', label: 'Training' },
  { value: 'maintenance', label: 'Maintenance' },
  { value: 'custom', label: 'Custom' },
];

export function ResourceAbsenceEditDialog({
  resourceId,
  isOpen,
  absence,
  initialStart,
  initialEnd,
  defaultAbsenceType = 'vacation',
  onClose,
  onSaved,
}: PersonAbsenceEditDialogProps) {
  const [absenceType, setAbsenceType] = useState<AbsenceType>(absence?.absenceType ?? defaultAbsenceType);
  const [title, setTitle] = useState(absence?.title ?? '');
  const [startDate, setStartDate] = useState<Date | undefined>(
    absence ? new Date(absence.startTs) : initialStart,
  );
  const [endDate, setEndDate] = useState<Date | undefined>(
    absence ? new Date(absence.endTs) : initialEnd,
  );

  const saveMutation = useMutation({
    mutationFn: () => {
      // The times of day are preserved from the existing absence: this form edits dates, and a
      // drag on the schedule calendar is what sets times. Dropping them here would quietly
      // widen an absence to midnight-to-midnight on every save.
      const payload = {
        absenceType,
        title: title || absenceType,
        startTs: startDate!.toISOString(),
        endTs: endDate!.toISOString(),
      };
      return absence
        ? updateResourceAbsence(resourceId, absence.id, { ...payload, notes: absence.notes, enabled: absence.enabled })
        : createResourceAbsence(resourceId, payload);
    },
    meta: {
      successMessage: absence ? 'Absence updated' : 'Absence added',
      errorMessage: absence ? 'Failed to update absence' : 'Failed to add absence',
      // An absence makes existing bookings on this resource conflict, so the conflict registry
      // and the utilization grid are stale the moment it is saved — not just the absence list.
      invalidates: [
        qk.resources.absences(resourceId),
        qk.conflicts.all(),
        qk.utilization.byResourceAll(),
      ],
    },
    onSuccess: () => {
      setAbsenceType(defaultAbsenceType);
      setTitle('');
      setStartDate(undefined);
      setEndDate(undefined);
      onSaved();
    },
  });

  const handleSubmit = () => {
    if (!startDate || !endDate) return;
    saveMutation.mutate();
  };

  return (
    <FormDialog
      open={isOpen}
      onOpenChange={(o) => { if (!o) onClose(); }}
      title={absence ? 'Edit Absence' : 'Block Time'}
      onSubmit={handleSubmit}
      isSubmitting={saveMutation.isPending}
      submitLabel="Save"
      submitDisabled={!startDate || !endDate}
      size="sm"
    >
      <div className="space-y-2">
        <Label htmlFor="type">Type</Label>
        <Select value={absenceType} onValueChange={(v) => setAbsenceType(v as AbsenceType)}>
          <SelectTrigger>
            <SelectValue placeholder="Select type" />
          </SelectTrigger>
          <SelectContent>
            {absenceTypes.map((at) => (
              <SelectItem key={at.value} value={at.value}>
                {at.label}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>

      <div className="space-y-2">
        <Label htmlFor="title">Reason</Label>
        <Input
          id="title"
          value={title}
          onChange={(e) => setTitle(e.target.value)}
          placeholder="Optional"
        />
      </div>

      <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
        <div className="space-y-2">
          <Label>Start Date *</Label>
          <Popover>
            <PopoverTrigger asChild>
              <Button variant="outline" className="w-full justify-start text-left font-normal">
                <CalendarIcon className="h-4 w-4 mr-2" />
                {startDate ? format(startDate, DATE_FORMATS.DATE_LOCALE_SHORT) : 'Pick a date'}
              </Button>
            </PopoverTrigger>
            <PopoverContent className="w-auto p-0">
              <Calendar mode="single" selected={startDate} onSelect={setStartDate} autoFocus />
            </PopoverContent>
          </Popover>
        </div>

        <div className="space-y-2">
          <Label>End Date *</Label>
          <Popover>
            <PopoverTrigger asChild>
              <Button variant="outline" className="w-full justify-start text-left font-normal">
                <CalendarIcon className="h-4 w-4 mr-2" />
                {endDate ? format(endDate, DATE_FORMATS.DATE_LOCALE_SHORT) : 'Pick a date'}
              </Button>
            </PopoverTrigger>
            <PopoverContent className="w-auto p-0">
              <Calendar mode="single" selected={endDate} onSelect={setEndDate} autoFocus />
            </PopoverContent>
          </Popover>
        </div>
      </div>
    </FormDialog>
  );
}
