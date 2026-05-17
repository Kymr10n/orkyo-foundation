import { useState } from 'react';
import { useQuery, useQueries } from '@tanstack/react-query';
import { Settings } from 'lucide-react';
import { Button } from '@foundation/src/components/ui/button';
import { ScrollArea } from '@foundation/src/components/ui/scroll-area';
import { getResources, type ResourceInfo } from '@foundation/src/lib/api/resources-api';
import { getResourceCapabilities } from '@foundation/src/lib/api/resource-capabilities-api';
import { PersonSkillsEditor } from './PersonSkillsEditor';

/**
 * People > Skills tab. Lists every person with a "Manage skills" button
 * that opens PersonSkillsEditor (per-person criterion assignment).
 * Skill counts are fetched per-person via useQueries.
 */
export function PersonSkillsTab() {
  const personsQuery = useQuery({
    queryKey: ['resources', 'person'],
    queryFn: () => getResources({ resourceTypeKey: 'person', isActive: true }),
  });

  const persons = personsQuery.data?.data ?? [];

  const skillsQueries = useQueries({
    queries: persons.map((p) => ({
      queryKey: ['resource-capabilities', p.id],
      queryFn: () => getResourceCapabilities(p.id),
      enabled: !!p.id,
    })),
  });

  const [editing, setEditing] = useState<ResourceInfo | null>(null);

  if (personsQuery.isLoading) {
    return (
      <div className="flex items-center justify-center h-full">
        <p className="text-sm text-muted-foreground">Loading people...</p>
      </div>
    );
  }

  if (persons.length === 0) {
    return (
      <div className="rounded-2xl border bg-card p-6 text-center">
        <p className="text-sm text-muted-foreground">No people defined yet.</p>
      </div>
    );
  }

  return (
    <div className="h-full flex flex-col bg-card rounded-lg border">
      <div className="p-4 border-b">
        <h3 className="font-semibold">Skills by Person</h3>
        <p className="text-xs text-muted-foreground mt-1">
          Assign criterion values (e.g. certifications, ratings) to individual people.
        </p>
      </div>
      <ScrollArea className="flex-1">
        <div className="divide-y">
          {persons.map((person, idx) => {
            const skillCount = skillsQueries[idx]?.data?.length ?? 0;
            return (
              <div key={person.id} className="flex items-center justify-between p-4">
                <div>
                  <div className="font-medium">{person.name}</div>
                  <div className="text-xs text-muted-foreground">
                    {skillCount} {skillCount === 1 ? 'skill' : 'skills'}
                  </div>
                </div>
                <Button variant="outline" size="sm" onClick={() => setEditing(person)}>
                  <Settings className="h-4 w-4 mr-2" />
                  Manage skills
                </Button>
              </div>
            );
          })}
        </div>
      </ScrollArea>

      {editing && (
        <PersonSkillsEditor
          open={!!editing}
          onOpenChange={(open) => !open && setEditing(null)}
          resourceId={editing.id}
          personName={editing.name}
        />
      )}
    </div>
  );
}
