import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs';
import { useEffect, useState } from 'react';
import { useSearchParams } from 'react-router-dom';

import { useAuth } from '@/contexts/AuthContext';
import { CriteriaSettings } from '@/components/settings/CriteriaSettings';
import { GroupSettings } from '@/components/settings/GroupSettings';
import { PresetSettings } from '@/components/settings/PresetSettings';
import { TemplateSettings } from '@/components/settings/TemplateSettings';
import { SiteSettings } from '@/components/settings/SiteSettings';
import { UserSettings } from '@/components/settings/UserSettings';
import { OrganizationSettings } from '@/components/settings/OrganizationSettings';
import { TenantConfigSettings } from '@/components/settings/TenantConfigSettings';
import { SchedulingSettings } from '@/components/settings/SchedulingSettings';
import { useSites } from '@/hooks/useSites';

export function SettingsPage() {
  const { membership } = useAuth();
  const isAdmin = membership?.isTenantAdmin === true;
  const tier = membership?.tier ?? 'Free';
  const { data: sites = [] } = useSites();
  const showSites = tier !== 'Free' || sites.length > 1;
  const [searchParams, setSearchParams] = useSearchParams();
  const tabParam = searchParams.get('tab');
  const editParam = searchParams.get('edit');
  const defaultTab = (!isAdmin && tabParam === 'users') ? 'criteria' : (tabParam || 'criteria');
  const [activeTab, setActiveTab] = useState(defaultTab);

  // Sync tab from URL param
  useEffect(() => {
    if (tabParam && tabParam !== activeTab) {
      setActiveTab(tabParam);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tabParam]);

  // Update URL when tab changes
  const handleTabChange = (tab: string) => {
    setActiveTab(tab);
    const newParams = new URLSearchParams(searchParams);
    newParams.set('tab', tab);
    // Clear edit param when switching tabs
    if (tab !== tabParam) {
      newParams.delete('edit');
    }
    setSearchParams(newParams, { replace: true });
  };

  return (
    <div className="h-full flex flex-col">
      <div className="border-b bg-card px-6 py-4">
        <h1 className="text-xl font-semibold">Settings</h1>
        <p className="text-sm text-muted-foreground mt-1">
          Manage tenant-wide configurations and definitions
        </p>
      </div>

      <div className="flex-1 overflow-auto">
        <Tabs value={activeTab} onValueChange={handleTabChange} className="p-6">

          <TabsList>
            <TabsTrigger value="criteria">Criteria</TabsTrigger>
            {showSites && <TabsTrigger value="sites">Sites</TabsTrigger>}
            <TabsTrigger value="groups">Groups</TabsTrigger>
            <TabsTrigger value="templates">Templates</TabsTrigger>
            <TabsTrigger value="presets">Presets</TabsTrigger>
            {isAdmin && <TabsTrigger value="users">Users</TabsTrigger>}
            <TabsTrigger value="organization">Organization</TabsTrigger>
            <TabsTrigger value="scheduling">Scheduling</TabsTrigger>
            <TabsTrigger value="configuration">Configuration</TabsTrigger>
          </TabsList>


          <TabsContent value="criteria" className="mt-6">
            <CriteriaSettings />
          </TabsContent>

          {showSites && (
            <TabsContent value="sites" className="mt-6">
              <SiteSettings />
            </TabsContent>
          )}

          <TabsContent value="groups" className="mt-6">
            <GroupSettings editGroupId={activeTab === 'groups' ? editParam : null} />
          </TabsContent>

          <TabsContent value="templates" className="mt-6">
            <TemplateSettings entityType="request" />
          </TabsContent>

          <TabsContent value="presets" className="mt-6">
            <PresetSettings />
          </TabsContent>

          {isAdmin && (
            <TabsContent value="users" className="mt-6">
              <UserSettings />
            </TabsContent>
          )}

          <TabsContent value="organization" className="mt-6">
            <OrganizationSettings />
          </TabsContent>

          <TabsContent value="scheduling" className="mt-6">
            <SchedulingSettings />
          </TabsContent>

          <TabsContent value="configuration" className="mt-6">
            <TenantConfigSettings scope="tenant" />
          </TabsContent>
        </Tabs>
      </div>
    </div>
  );
}
