import { Button } from "@foundation/src/components/ui/button";
import { SettingsPageHeader } from "./SettingsPageHeader";
import { Card } from "@foundation/src/components/ui/card";
import {
    Dialog,
    DialogContent,
    DialogDescription,
    DialogFooter,
    DialogHeader,
    DialogTitle,
} from "@foundation/src/components/ui/dialog";
import { Input } from "@foundation/src/components/ui/input";
import { Label } from "@foundation/src/components/ui/label";
import {
    Table,
    TableBody,
    TableCell,
    TableHead,
    TableHeader,
    TableRow,
} from "@foundation/src/components/ui/table";
import { Textarea } from "@foundation/src/components/ui/textarea";
import {
    useCreateSpaceGroup,
    useDeleteSpaceGroup,
    useUpdateSpaceGroup
} from "@foundation/src/hooks/useGroups";
import { getSpaceGroups } from "@foundation/src/lib/api/space-groups-api";
import type { CreateSpaceGroupRequest, SpaceGroup } from "@foundation/src/types/spaceGroup";
import { Pencil, Plus, Sparkles, Trash2, Users } from "lucide-react";
import { useEffect, useState } from "react";
import { useSearchParams } from "react-router-dom";
import { GroupCapabilitiesEditor } from "./GroupCapabilitiesEditor";
import { GroupSpacesEditor } from "./GroupSpacesEditor";
import { logger } from "@foundation/src/lib/core/logger";

interface GroupSettingsProps {
  editGroupId?: string | null;
}

export function GroupSettings({ editGroupId }: GroupSettingsProps) {
  // Note: useSpaceGroups requires siteId, but GroupSettings is site-independent
  // We'll keep manual loading for now since groups are tenant-wide
  const [, setSearchParams] = useSearchParams();
  const [groups, setGroups] = useState<SpaceGroup[]>([]);
  const [loading, setLoading] = useState(true);
  const [isDialogOpen, setIsDialogOpen] = useState(false);
  const [editingGroup, setEditingGroup] = useState<SpaceGroup | null>(null);
  const [formData, setFormData] = useState<CreateSpaceGroupRequest>({
    name: "",
    description: "",
    color: "#3b82f6",
    displayOrder: 0,
  });
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  
  // New dialog states for spaces and capabilities
  const [spacesEditorOpen, setSpacesEditorOpen] = useState(false);
  const [capabilitiesEditorOpen, setCapabilitiesEditorOpen] = useState(false);
  const [selectedGroup, setSelectedGroup] = useState<SpaceGroup | null>(null);

  // Note: These hooks need siteId - for now using a dummy value until we refactor
  // the backend to support tenant-wide groups without siteId
  const createGroupMutation = useCreateSpaceGroup("dummy");
  const updateGroupMutation = useUpdateSpaceGroup("dummy");
  const deleteGroupMutation = useDeleteSpaceGroup("dummy");

  useEffect(() => {
    loadGroups();
  }, []);

  // Handle ?edit=<id> query param from global search
  useEffect(() => {
    if (editGroupId && groups.length > 0 && !loading) {
      const groupToEdit = groups.find(g => g.id === editGroupId);
      if (groupToEdit) {
        openEditDialog(groupToEdit);
        // Clear the edit param
        setSearchParams((prev) => {
          prev.delete('edit');
          return prev;
        }, { replace: true });
      }
    }
  }, [editGroupId, groups, loading, setSearchParams]);

  async function loadGroups() {
    try {
      setLoading(true);
      const data = await getSpaceGroups();
      setGroups(data);
    } catch (err) {
      logger.error("Failed to load groups:", err);
      setError(err instanceof Error ? err.message : "Failed to load groups");
    } finally {
      setLoading(false);
    }
  }

  function openCreateDialog() {
    setEditingGroup(null);
    setFormData({
      name: "",
      description: "",
      color: "#3b82f6",
      displayOrder: groups.length,
    });
    setError(null);
    setIsDialogOpen(true);
  }

  function openEditDialog(group: SpaceGroup) {
    setEditingGroup(group);
    setFormData({
      name: group.name,
      description: group.description || "",
      color: group.color || "#3b82f6",
      displayOrder: group.displayOrder,
    });
    setError(null);
    setIsDialogOpen(true);
  }

  async function handleSave() {
    if (!formData.name.trim()) {
      setError("Name is required");
      return;
    }

    try {
      setSaving(true);
      setError(null);

      if (editingGroup) {
        await updateGroupMutation.mutateAsync({
          id: editingGroup.id,
          data: formData,
        });
      } else {
        await createGroupMutation.mutateAsync(formData);
      }

      setIsDialogOpen(false);
      await loadGroups();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to save group");
    } finally {
      setSaving(false);
    }
  }

  async function handleDelete(group: SpaceGroup) {
    if (!confirm(`Delete "${group.name}"? Spaces in this group will become ungrouped.`)) {
      return;
    }

    try {
      await deleteGroupMutation.mutateAsync(group.id);
      await loadGroups();
    } catch (err) {
      logger.error("Failed to delete group:", err);
      alert(err instanceof Error ? err.message : "Failed to delete group");
    }
  }

  if (loading) {
    return (
      <div className="flex items-center justify-center h-64">
        <p className="text-muted-foreground">Loading space groups...</p>
      </div>
    );
  }

  return (
    <div className="space-y-6">
      <SettingsPageHeader
        title="Space Groups"
        description="Organize spaces into collapsible groups in the scheduler."
      >
        <Button onClick={openCreateDialog}>
          <Plus className="h-4 w-4 mr-2" />
          New Group
        </Button>
      </SettingsPageHeader>

      {groups.length === 0 ? (
        <Card className="p-12 text-center">
          <p className="text-muted-foreground mb-4">No space groups yet</p>
          <Button onClick={openCreateDialog} variant="outline">
            <Plus className="h-4 w-4 mr-2" />
            Create your first group
          </Button>
        </Card>
      ) : (
        <div className="border rounded-lg">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Name</TableHead>
                <TableHead>Description</TableHead>
                <TableHead>Color</TableHead>
                <TableHead className="text-center">Spaces</TableHead>
                <TableHead className="text-center">Order</TableHead>
                <TableHead className="text-right">Actions</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {groups.map((group) => (
                <TableRow key={group.id}>
                  <TableCell className="font-medium">{group.name}</TableCell>
                  <TableCell className="text-muted-foreground">
                    {group.description || "-"}
                  </TableCell>
                  <TableCell>
                    <div className="flex items-center gap-2">
                      {group.color && (
                        <div
                          className="w-6 h-6 rounded border"
                          style={{ backgroundColor: group.color }}
                        />
                      )}
                      <span className="text-muted-foreground">{group.color || "-"}</span>
                    </div>
                  </TableCell>
                  <TableCell className="text-center">{group.spaceCount || 0}</TableCell>
                  <TableCell className="text-center text-muted-foreground">{group.displayOrder}</TableCell>
                  <TableCell>
                    <div className="flex items-center justify-end gap-2">
                      <Button
                        variant="ghost"
                        size="sm"
                        onClick={() => {
                          setSelectedGroup(group);
                          setSpacesEditorOpen(true);
                        }}
                        title="Manage Spaces"
                      >
                        <Users className="h-4 w-4" />
                      </Button>
                      <Button
                        variant="ghost"
                        size="sm"
                        onClick={() => {
                          setSelectedGroup(group);
                          setCapabilitiesEditorOpen(true);
                        }}
                        title="Manage Capabilities"
                      >
                        <Sparkles className="h-4 w-4" />
                      </Button>
                      <Button
                        variant="ghost"
                        size="sm"
                        onClick={() => openEditDialog(group)}
                        title="Edit Group"
                      >
                        <Pencil className="h-4 w-4" />
                      </Button>
                      <Button
                        variant="ghost"
                        size="sm"
                        onClick={() => handleDelete(group)}
                        title="Delete Group"
                      >
                        <Trash2 className="h-4 w-4 text-destructive" />
                      </Button>
                    </div>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
      )}

      <Dialog open={isDialogOpen} onOpenChange={setIsDialogOpen}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>
              {editingGroup ? "Edit Group" : "Create New Group"}
            </DialogTitle>
            <DialogDescription>
              {editingGroup
                ? "Update the group details"
                : "Create a new group to organize your spaces"}
            </DialogDescription>
          </DialogHeader>

          <div className="space-y-4">
            {error && (
              <div className="text-sm text-destructive bg-destructive/10 p-3 rounded">
                {error}
              </div>
            )}

            <div className="space-y-2">
              <Label htmlFor="name">Name *</Label>
              <Input
                id="name"
                value={formData.name}
                onChange={(e) => setFormData({ ...formData, name: e.target.value })}
                placeholder="e.g., Meeting Rooms, Classrooms"
              />
            </div>

            <div className="space-y-2">
              <Label htmlFor="description">Description</Label>
              <Textarea
                id="description"
                value={formData.description}
                onChange={(e) => setFormData({ ...formData, description: e.target.value })}
                placeholder="Optional description"
                rows={3}
              />
            </div>

            <div className="space-y-2">
              <Label htmlFor="color">Color</Label>
              <div className="flex gap-2">
                <Input
                  id="color"
                  type="color"
                  value={formData.color}
                  onChange={(e) => setFormData({ ...formData, color: e.target.value })}
                  className="w-20"
                />
                <Input
                  type="text"
                  value={formData.color}
                  onChange={(e) => setFormData({ ...formData, color: e.target.value })}
                  placeholder="#3b82f6"
                  className="flex-1"
                />
              </div>
            </div>

            <div className="space-y-2">
              <Label htmlFor="displayOrder">Display Order</Label>
              <Input
                id="displayOrder"
                type="text"
                inputMode="numeric"
                pattern="[0-9]*"
                value={formData.displayOrder}
                onChange={(e) => {
                  const val = e.target.value.replace(/[^0-9]/g, '');
                  setFormData({ ...formData, displayOrder: val === '' ? 0 : parseInt(val) });
                }}
              />
            </div>
          </div>

          <DialogFooter>
            <Button variant="outline" onClick={() => setIsDialogOpen(false)} disabled={saving}>
              Cancel
            </Button>
            <Button onClick={handleSave} disabled={saving}>
              {saving ? "Saving..." : editingGroup ? "Update" : "Create"}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      {/* Spaces Editor Dialog */}
      {selectedGroup && (
        <GroupSpacesEditor
          open={spacesEditorOpen}
          onOpenChange={setSpacesEditorOpen}
          groupId={selectedGroup.id}
          groupName={selectedGroup.name}
          onSuccess={loadGroups}
        />
      )}

      {/* Capabilities Editor Dialog */}
      {selectedGroup && (
        <GroupCapabilitiesEditor
          open={capabilitiesEditorOpen}
          onOpenChange={setCapabilitiesEditorOpen}
          groupId={selectedGroup.id}
          groupName={selectedGroup.name}
          onSuccess={loadGroups}
        />
      )}
    </div>
  );
}
