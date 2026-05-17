import { useState, useEffect } from 'react';
import { useMutation } from '@tanstack/react-query';
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
import { Textarea } from '@foundation/src/components/ui/textarea';
import { Loader2 } from 'lucide-react';
import { createResourceGroup, updateResourceGroup, type ResourceGroupInfo } from '@foundation/src/lib/api/resource-groups-api';

interface ResourceGroupEditDialogProps {
  resourceTypeKey: string;
  group: ResourceGroupInfo | null;
  isOpen: boolean;
  onClose: () => void;
  onSaved: () => void;
}

export function ResourceGroupEditDialog({ resourceTypeKey, group, isOpen, onClose, onSaved }: ResourceGroupEditDialogProps) {
  const [name, setName] = useState('');
  const [description, setDescription] = useState('');
  const [defaultAvailabilityPercent, setDefaultAvailabilityPercent] = useState(100);

  useEffect(() => {
    if (group) {
      setName(group.name);
      setDescription(group.description ?? '');
      setDefaultAvailabilityPercent(group.defaultAvailabilityPercent);
    } else {
      setName('');
      setDescription('');
      setDefaultAvailabilityPercent(100);
    }
  }, [group, isOpen]);

  const saveMutation = useMutation({
    mutationFn: () =>
      group
        ? updateResourceGroup(group.id, {
            name,
            description: description || undefined,
            defaultAvailabilityPercent,
          })
        : createResourceGroup({
            resourceTypeKey,
            name,
            description: description || undefined,
            defaultAvailabilityPercent,
          }),
    onSuccess: onSaved,
  });

  const handleSubmit = (e: React.SyntheticEvent<HTMLFormElement>) => {
    e.preventDefault();
    saveMutation.mutate();
  };

  return (
    <Dialog open={isOpen} onOpenChange={onClose}>
      <DialogContent className="sm:max-w-[500px]">
        <DialogHeader>
          <DialogTitle>{group ? 'Edit Group' : 'Add Group'}</DialogTitle>
        </DialogHeader>

        <form onSubmit={handleSubmit} className="space-y-4">
          <div className="space-y-2">
            <Label htmlFor="name">Name *</Label>
            <Input
              id="name"
              value={name}
              onChange={(e) => setName(e.target.value)}
              required
            />
          </div>

          <div className="space-y-2">
            <Label htmlFor="description">Description</Label>
            <Textarea
              id="description"
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              rows={3}
            />
          </div>

          <div className="space-y-2">
            <Label htmlFor="defaultAvailability">Default Availability (%)</Label>
            <Input
              id="defaultAvailability"
              type="number"
              value={defaultAvailabilityPercent}
              onChange={(e) => setDefaultAvailabilityPercent(Number(e.target.value))}
              min={0}
              max={100}
            />
          </div>

          <DialogFooter>
            <Button type="button" variant="outline" onClick={onClose} disabled={saveMutation.isPending}>
              Cancel
            </Button>
            <Button type="submit" disabled={saveMutation.isPending || !name.trim()}>
              {saveMutation.isPending ? (
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
