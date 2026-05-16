import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { getResources } from '@foundation/src/lib/api/resources-api';
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogFooter,
} from '@foundation/src/components/ui/dialog';
import { Button } from '@foundation/src/components/ui/button';
import { Input } from '@foundation/src/components/ui/input';
import { Label } from '@foundation/src/components/ui/label';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@foundation/src/components/ui/select';
import { Textarea } from '@foundation/src/components/ui/textarea';
import { Loader2 } from 'lucide-react';
import { Calendar as CalendarIcon } from 'lucide-react';
import { Popover, PopoverContent, PopoverTrigger } from '@foundation/src/components/ui/popover';
import { Calendar } from '@foundation/src/components/ui/calendar';
import { format } from 'date-fns';

interface PersonAbsenceEditDialogProps {
  isOpen: boolean;
  onClose: () => void;
  onSaved: () => void;
}

interface AbsenceType {
  value: string;
  label: string;
}

const absenceTypes: AbsenceType[] = [
  { value: 'vacation', label: 'Vacation' },
  { value: 'sick_leave', label: 'Sick Leave' },
  { value: 'unavailable', label: 'Unavailable' },
  { value: 'training', label: 'Training' },
  { value: 'public_holiday', label: 'Public Holiday' },
  { value: 'other', label: 'Other' },
];

export function PersonAbsenceEditDialog({ isOpen, onClose, onSaved }: PersonAbsenceEditDialogProps) {
  const [personId, setPersonId] = useState('');

  const { data: people } = useQuery({
    queryKey: ['resources', 'person'],
    queryFn: () => getResources({ resourceTypeKey: 'person' }),
    enabled: isOpen,
  });
  const personList = people?.data ?? [];
  const [type, setType] = useState('vacation');
  const [title, setTitle] = useState('');
  const [startDate, setStartDate] = useState<Date | undefined>(undefined);
  const [endDate, setEndDate] = useState<Date | undefined>(undefined);
  const [notes, setNotes] = useState('');
  const [isSubmitting, setIsSubmitting] = useState(false);

  const handleSubmit = async (e: React.SyntheticEvent<HTMLFormElement>) => {
    e.preventDefault();
    setIsSubmitting(true);

    // Placeholder for actual API call
    await new Promise(resolve => setTimeout(resolve, 500));

    setIsSubmitting(false);
    onSaved();
  };

  return (
    <Dialog open={isOpen} onOpenChange={onClose}>
      <DialogContent className="sm:max-w-[500px]">
        <DialogHeader>
          <DialogTitle>Add Absence</DialogTitle>
        </DialogHeader>

        <form onSubmit={handleSubmit} className="space-y-4">
          <div className="space-y-2">
            <Label htmlFor="person">Person *</Label>
            <Select value={personId} onValueChange={setPersonId}>
              <SelectTrigger>
                <SelectValue placeholder="Select a person" />
              </SelectTrigger>
              <SelectContent>
                {personList.map((p) => (
                  <SelectItem key={p.id} value={p.id}>
                    {p.name}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>

          <div className="space-y-2">
            <Label htmlFor="type">Type</Label>
            <Select value={type} onValueChange={setType}>
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
            <Label htmlFor="title">Title</Label>
            <Input
              id="title"
              value={title}
              onChange={(e) => setTitle(e.target.value)}
            />
          </div>

          <div className="grid grid-cols-2 gap-4">
            <div className="space-y-2">
              <Label htmlFor="startDate">Start Date *</Label>
              <Popover>
                <PopoverTrigger asChild>
                  <Button
                    variant="outline"
                    className="w-full justify-start text-left font-normal"
                  >
                    <CalendarIcon className="h-4 w-4 mr-2" />
                    {startDate ? format(startDate, 'PPP') : 'Pick a date'}
                  </Button>
                </PopoverTrigger>
                <PopoverContent className="w-auto p-0">
                  <Calendar
                    mode="single"
                    selected={startDate}
                    onSelect={setStartDate}
                    autoFocus
                  />
                </PopoverContent>
              </Popover>
            </div>

            <div className="space-y-2">
              <Label htmlFor="endDate">End Date *</Label>
              <Popover>
                <PopoverTrigger asChild>
                  <Button
                    variant="outline"
                    className="w-full justify-start text-left font-normal"
                  >
                    <CalendarIcon className="h-4 w-4 mr-2" />
                    {endDate ? format(endDate, 'PPP') : 'Pick a date'}
                  </Button>
                </PopoverTrigger>
                <PopoverContent className="w-auto p-0">
                  <Calendar
                    mode="single"
                    selected={endDate}
                    onSelect={setEndDate}
                    autoFocus
                  />
                </PopoverContent>
              </Popover>
            </div>
          </div>

          <div className="space-y-2">
            <Label htmlFor="notes">Notes</Label>
            <Textarea
              id="notes"
              value={notes}
              onChange={(e) => setNotes(e.target.value)}
              rows={3}
            />
          </div>

          <DialogFooter>
            <Button type="button" variant="outline" onClick={onClose} disabled={isSubmitting}>
              Cancel
            </Button>
            <Button type="submit" disabled={isSubmitting || !personId || !startDate || !endDate}>
              {isSubmitting ? (
                <>
                  <Loader2 className="h-4 w-4 mr-2 animate-spin" />
                  Saving...
                </>
              ) : (
                'Save'
              )}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
