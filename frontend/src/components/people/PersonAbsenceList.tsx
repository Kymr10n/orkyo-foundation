import { useState } from 'react';
import { Button } from '@foundation/src/components/ui/button';
import { Plus } from 'lucide-react';
import { PersonAbsenceEditDialog } from './PersonAbsenceEditDialog';

export function PersonAbsenceList() {
  const [isDialogOpen, setIsDialogOpen] = useState(false);

  return (
    <div className="space-y-4">
      <div className="flex justify-end">
        <Button onClick={() => setIsDialogOpen(true)}>
          <Plus className="h-4 w-4 mr-2" />
          Add Absence
        </Button>
      </div>

      <div className="border rounded-lg overflow-hidden">
        <table className="w-full">
          <thead className="bg-muted">
            <tr>
              <th className="text-left p-4 font-medium">Person</th>
              <th className="text-left p-4 font-medium">Type</th>
              <th className="text-left p-4 font-medium">Start</th>
              <th className="text-left p-4 font-medium">End</th>
              <th className="text-left p-4 font-medium">Reason</th>
            </tr>
          </thead>
          <tbody>
            <tr className="border-t">
              <td className="p-4" colSpan={5}>
                <div className="text-center py-4 text-muted-foreground">
                  Absence management coming soon
                </div>
              </td>
            </tr>
          </tbody>
        </table>
      </div>

      <PersonAbsenceEditDialog
        isOpen={isDialogOpen}
        onClose={() => setIsDialogOpen(false)}
        onSaved={() => setIsDialogOpen(false)}
      />
    </div>
  );
}
