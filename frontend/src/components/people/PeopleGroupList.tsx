import { useState } from 'react';
import { Button } from '@foundation/src/components/ui/button';
import { Plus, Users } from 'lucide-react';
import { PeopleGroupEditDialog } from './PeopleGroupEditDialog';

// Phase 5 placeholder: full Groups implementation lands in the People-pack
// "Groups tab" cleanup phase. See requirements/people_resources_spec/STATUS.md.

export function PeopleGroupList() {
  // Group type is owned by PeopleGroupEditDialog; PeopleGroupList only holds null
  // until the Groups tab is fully implemented (see plan Group 4).
  const [editingGroup, setEditingGroup] = useState<null>(null);
  const [isDialogOpen, setIsDialogOpen] = useState(false);

  const handleClose = () => {
    setIsDialogOpen(false);
    setEditingGroup(null);
  };

  return (
    <div className="space-y-4">
      <div className="flex justify-end">
        <Button onClick={() => { setEditingGroup(null); setIsDialogOpen(true); }}>
          <Plus className="h-4 w-4 mr-2" />
          Add Group
        </Button>
      </div>

      <div className="border rounded-lg overflow-hidden">
        <div className="flex items-center gap-2 p-8 text-center text-muted-foreground">
          <Users className="h-5 w-5" />
          <span>Groups management coming soon.</span>
        </div>
      </div>

      <PeopleGroupEditDialog
        group={editingGroup}
        isOpen={isDialogOpen}
        onClose={handleClose}
        onSaved={handleClose}
      />
    </div>
  );
}
