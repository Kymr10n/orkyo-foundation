import { useState } from 'react';
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@foundation/src/components/ui/tabs';
import { PersonList } from '@foundation/src/components/people/PersonList';
import { ResourceGroupList } from '@foundation/src/components/resource-groups/ResourceGroupList';
import { PersonAbsenceList } from '@foundation/src/components/people/PersonAbsenceList';

export function PeoplePage() {
  const [activeTab, setActiveTab] = useState('people');

  return (
    <div className="flex flex-col h-full p-4 md:p-6 lg:p-8">
      <div className="flex items-center justify-between mb-6">
        <div>
          <h1 className="text-2xl font-bold">People</h1>
          <p className="text-sm text-muted-foreground">
            Manage person resources, groups, and absences
          </p>
        </div>
      </div>

      <Tabs value={activeTab} onValueChange={setActiveTab} className="flex-1 flex flex-col">
        <TabsList className="mb-4">
          <TabsTrigger value="people">People</TabsTrigger>
          <TabsTrigger value="groups">Groups</TabsTrigger>
          <TabsTrigger value="absences">Absences</TabsTrigger>
        </TabsList>

        <TabsContent value="people" className="flex-1">
          <PersonList />
        </TabsContent>

        <TabsContent value="groups" className="flex-1">
          <ResourceGroupList resourceTypeKey="person" />
        </TabsContent>

        <TabsContent value="absences" className="flex-1">
          <PersonAbsenceList />
        </TabsContent>
      </Tabs>
    </div>
  );
}
