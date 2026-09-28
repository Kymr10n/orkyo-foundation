/**
 * Organization Settings Component
 *
 * Allows tenant owners to:
 * - Update organization name
 * - Transfer ownership to another admin
 * - Delete the organization
 */

import { useMemo, useState } from "react";
import { useAuth } from "@foundation/src/contexts/AuthContext";
import { TENANT_ROLE } from "@foundation/src/hooks/usePermissions";
import { SettingsPageHeader } from "./SettingsPageHeader";
import { navigateToApex } from "@foundation/src/lib/utils/tenant-navigation";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@foundation/src/components/ui/card";
import { Button } from "@foundation/src/components/ui/button";
import { Input } from "@foundation/src/components/ui/input";
import { Label } from "@foundation/src/components/ui/label";
import { ConfirmDialog } from "@foundation/src/components/ui/ConfirmDialog";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@foundation/src/components/ui/select";
import { Alert, AlertDescription } from "@foundation/src/components/ui/alert";
import {
  Building2,
  UserCog,
  Trash2,
  AlertTriangle,
  Save,
  Download,
  Check,
} from "lucide-react";
import { Checkbox } from "@foundation/src/components/ui/checkbox";
import { LoadingSpinner } from "@foundation/src/components/ui/LoadingSpinner";
import {
  useDeleteOrganization,
  useExportTenantData,
  useRenameTenant,
  useTransferTenantOwnership,
} from "@foundation/src/hooks/useOrganization";
import { useUsers } from "@foundation/src/hooks/useTenantUsers";
import { FeatureUpsell } from "@foundation/src/components/ui/FeatureUpsell";
import { FeatureKeys } from "@foundation/contracts/plans";
import { useFeatureEnabled } from "@foundation/src/hooks/useFeatureEnabled";

interface OrganizationSettingsProps {
  /** Where the export upsell's CTA points when the plan lacks the feature. Omit to hide the CTA. */
  upgradeHref?: string;
}

export function OrganizationSettings({ upgradeHref }: OrganizationSettingsProps = {}) {
  const { membership, appUser, clearMembership } = useAuth();
  // Server-enforced too (FeatureKeys.DataExport → 403); this just keeps the card
  // from offering a button that can only fail.
  const dataExportAvailable = useFeatureEnabled(FeatureKeys.DataExport);

  const [displayName, setDisplayName] = useState("");
  const [originalName, setOriginalName] = useState("");
  const [selectedNewOwner, setSelectedNewOwner] = useState("");
  const [transferConfirmOpen, setTransferConfirmOpen] = useState(false);
  const [deleteOrgOpen, setDeleteOrgOpen] = useState(false);
  const [exportDone, setExportDone] = useState(false);
  const [includePlanningData, setIncludePlanningData] = useState(false);

  const isOwner = membership?.isOwner ?? false;
  const isBreakGlass = membership?.isBreakGlass ?? false;
  const canManageOrg = isOwner || isBreakGlass;
  const tenantId = membership?.tenantId ?? "";
  const tenantSlug = membership?.slug ?? "";
  const currentUserId = appUser?.id ?? "";

  // Seeding the name fields from the membership is a render-phase update.
  // Wrapped so "not yet synced" is distinguishable from "synced to null": RequireAuth resolves
  // membership before this mounts, so it is normally present on the first render and must seed.
  const [syncedMembership, setSyncedMembership] = useState<{ v: typeof membership } | null>(null);
  if (syncedMembership?.v !== membership) {
    setSyncedMembership({ v: membership });
    if (membership) {
      setDisplayName(membership.displayName);
      setOriginalName(membership.displayName);
    }
  }

  // The admins an owner can hand the organization to. Read only with manage rights.
  const usersQuery = useUsers(canManageOrg && !!membership);
  const admins = useMemo(
    () =>
      (usersQuery.data ?? []).filter(
        (u) => u.role === TENANT_ROLE.Admin && u.status === "active" && u.id !== currentUserId,
      ),
    [usersQuery.data, currentUserId],
  );
  const loading = canManageOrg && usersQuery.isLoading;

  // Success and failure are toasted from each hook's `meta`; the handlers keep only the
  // follow-ups that are not feedback.
  const renameMutation = useRenameTenant();
  const transferMutation = useTransferTenantOwnership();
  const exportMutation = useExportTenantData(tenantSlug);
  const deleteMutation = useDeleteOrganization();
  const saving = renameMutation.isPending;
  const transferring = transferMutation.isPending;
  const exporting = exportMutation.isPending;
  const deleting = deleteMutation.isPending;

  const handleSaveName = () => {
    if (!tenantId || displayName === originalName) return;
    const trimmed = displayName.trim();
    renameMutation.mutate(
      { tenantId, displayName: trimmed },
      { onSuccess: () => setOriginalName(trimmed) },
    );
  };

  const handleTransferOwnership = () => {
    if (!tenantId || !selectedNewOwner) return;
    setTransferConfirmOpen(false);
    transferMutation.mutate(
      { tenantId, newOwnerId: selectedNewOwner },
      // Refresh the page to update membership state
      { onSuccess: () => window.location.reload() },
    );
  };

  const handleExport = () => {
    setExportDone(false);
    exportMutation.mutate(includePlanningData, {
      onSuccess: () => {
        setExportDone(true);
        setTimeout(() => setExportDone(false), 3000);
      },
    });
  };

  const handleDeleteOrganization = () => {
    if (!tenantId) return;
    deleteMutation.mutate(tenantId, {
      onSuccess: () => {
        clearMembership();
        if (!navigateToApex("/")) window.location.href = "/";
      },
      onError: () => setDeleteOrgOpen(false),
    });
  };

  if (loading) {
    return (
      <LoadingSpinner size="sm" muted fullScreen={false} className="py-12" />
    );
  }

  if (!canManageOrg) {
    return (
      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-2">
            <Building2 className="h-5 w-5" />
            Organization
          </CardTitle>
          <CardDescription>
            Organization settings can only be modified by the owner.
          </CardDescription>
        </CardHeader>
        <CardContent>
          <div className="space-y-4">
            <div>
              <Label className="text-muted-foreground">Organization Name</Label>
              <p className="text-lg font-medium">{membership?.displayName}</p>
            </div>
            <div>
              <Label className="text-muted-foreground">Slug</Label>
              <p className="font-mono text-sm">{tenantSlug}</p>
            </div>
          </div>
        </CardContent>
      </Card>
    );
  }

  return (
    <div className="space-y-6">
      <SettingsPageHeader
        title="Organization"
        description="Manage your organization name, ownership, and deletion."
      />

      {/* Organization Name */}
      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-2">
            <Building2 className="h-5 w-5" />
            Organization Details
          </CardTitle>
          <CardDescription>
            Basic information about your organization.
          </CardDescription>
        </CardHeader>
        <CardContent className="space-y-4">
          <div className="space-y-2">
            <Label htmlFor="displayName">Organization Name</Label>
            <div className="flex gap-2">
              <Input
                id="displayName"
                value={displayName}
                onChange={(e) => setDisplayName(e.target.value)}
                placeholder="Enter organization name"
                className="flex-1"
              />
              <Button
                onClick={handleSaveName}
                loading={saving}
                disabled={
                  saving || displayName === originalName || !displayName.trim()
                }
              >
                {!saving && <Save className="h-4 w-4" />}
                <span className="ml-2">Save</span>
              </Button>
            </div>
          </div>

          <div className="space-y-2">
            <Label className="text-muted-foreground">Slug</Label>
            <p className="font-mono text-sm bg-muted px-3 py-2 rounded">
              {tenantSlug}
            </p>
            <p className="text-xs text-muted-foreground">
              The slug cannot be changed after creation.
            </p>
          </div>
        </CardContent>
      </Card>

      {/* Export Data */}
      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-2">
            <Download className="h-5 w-5" />
            Export Data
          </CardTitle>
          <CardDescription>
            Download a complete JSON export of your organization data for backup
            or migration.
          </CardDescription>
        </CardHeader>
        <CardContent className="space-y-4">
          {!dataExportAvailable ? (
            <FeatureUpsell
              title="Data export / import"
              description="Download your organization as JSON, and move data in and out per page. Available on Professional and Enterprise plans."
              upgradeHref={upgradeHref}
            />
          ) : (
            <>
              <div className="flex items-center space-x-2">
                <Checkbox
                  id="includePlanning"
                  checked={includePlanningData}
                  onCheckedChange={(checked) =>
                    setIncludePlanningData(checked === true)
                  }
                />
                <Label htmlFor="includePlanning" className="cursor-pointer">
                  Include planning data (requests and schedules)
                </Label>
              </div>

              <Button onClick={handleExport} loading={exporting} disabled={exporting}>
                {!exporting && (exportDone ? (
                  <Check className="h-4 w-4" />
                ) : (
                  <Download className="h-4 w-4" />
                ))}
                <span className="ml-2">
                  {exporting
                    ? "Exporting..."
                    : exportDone
                      ? "Downloaded"
                      : "Export JSON"}
                </span>
              </Button>
            </>
          )}
        </CardContent>
      </Card>

      {/* Transfer Ownership */}
      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-2">
            <UserCog className="h-5 w-5" />
            Transfer Ownership
          </CardTitle>
          <CardDescription>
            Transfer organization ownership to another admin. You will remain an
            admin but will no longer be able to delete the organization.
          </CardDescription>
        </CardHeader>
        <CardContent className="space-y-4">
          {admins.length === 0 ? (
            <p className="text-sm text-muted-foreground">
              No other admins available. Promote another member to admin first
              before transferring ownership.
            </p>
          ) : (
            <>
              <div className="space-y-2">
                <Label htmlFor="newOwner">New Owner</Label>
                <Select
                  value={selectedNewOwner}
                  onValueChange={setSelectedNewOwner}
                >
                  <SelectTrigger>
                    <SelectValue placeholder="Select an admin..." />
                  </SelectTrigger>
                  <SelectContent>
                    {admins.map((admin) => (
                      <SelectItem key={admin.id} value={admin.id}>
                        {admin.displayName} ({admin.email})
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </div>

              <Button
                variant="outline"
                loading={transferring}
                disabled={!selectedNewOwner || transferring}
                onClick={() => setTransferConfirmOpen(true)}
              >
                Transfer Ownership
              </Button>

              <ConfirmDialog
                open={transferConfirmOpen}
                onOpenChange={setTransferConfirmOpen}
                title="Transfer ownership?"
                description={
                  <div>
                    Are you sure you want to transfer ownership? You will no
                    longer be able to:
                    <ul className="list-disc ml-6 mt-2">
                      <li>Delete the organization</li>
                      <li>Transfer ownership again</li>
                      <li>Change organization settings</li>
                    </ul>
                    <p className="mt-2">
                      This action cannot be undone by you.
                    </p>
                  </div>
                }
                confirmLabel="Transfer Ownership"
                onConfirm={handleTransferOwnership}
              />
            </>
          )}
        </CardContent>
      </Card>

      {/* Danger Zone */}
      <Card className="border-destructive">
        <CardHeader>
          <CardTitle className="flex items-center gap-2 text-destructive">
            <Trash2 className="h-5 w-5" />
            Danger Zone
          </CardTitle>
          <CardDescription>
            Permanently delete this organization. This action cannot be undone.
          </CardDescription>
        </CardHeader>
        <CardContent className="space-y-4">
          <Alert variant="destructive">
            <AlertTriangle className="h-4 w-4" />
            <AlertDescription>
              Deleting this organization will:
              <ul className="list-disc ml-6 mt-2">
                <li>Remove all members from the organization</li>
                <li>Cancel all pending invitations</li>
                <li>Delete all data after a 7-day grace period</li>
              </ul>
            </AlertDescription>
          </Alert>

          <Button
            variant="destructive"
            loading={deleting}
            disabled={deleting}
            onClick={() => setDeleteOrgOpen(true)}
          >
            Delete Organization
          </Button>

          <ConfirmDialog
            open={deleteOrgOpen}
            onOpenChange={setDeleteOrgOpen}
            title="Delete Organization"
            description={
              <>
                This will permanently delete{" "}
                <strong>{membership?.displayName}</strong> and all its data.
              </>
            }
            confirmLabel="Delete Organization"
            destructive
            isPending={deleting}
            confirmPhrase={tenantSlug}
            onConfirm={handleDeleteOrganization}
          />
        </CardContent>
      </Card>
    </div>
  );
}
