/**
 * Organization Settings Component
 *
 * Allows tenant owners to:
 * - Update organization name
 * - Transfer ownership to another admin
 * - Delete the organization
 */

import { useState, useEffect } from "react";
import { useAuth } from "@/contexts/AuthContext";
import { SettingsPageHeader } from "./SettingsPageHeader";
import { navigateToApex } from "@/lib/utils/tenant-navigation";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
  AlertDialogTrigger,
} from "@/components/ui/alert-dialog";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { Alert, AlertDescription } from "@/components/ui/alert";
import {
  Loader2,
  Building2,
  UserCog,
  Trash2,
  AlertTriangle,
  Save,
  Download,
  Check,
} from "lucide-react";
import { Checkbox } from "@/components/ui/checkbox";
import { updateTenant, transferTenantOwnership } from "@/lib/api/tenant-management-api";
import { deleteTenant } from "@/lib/api/tenant-account-api";
import { getUsers, type UserWithRole } from "@/lib/api/user-api";
import { exportTenantData } from "@/lib/api/export-api";
import { downloadFile } from "@/lib/utils/import-export";
import { logger } from "@/lib/core/logger";

export function OrganizationSettings() {
  const { membership, appUser, clearMembership } = useAuth();

  const [displayName, setDisplayName] = useState("");
  const [originalName, setOriginalName] = useState("");
  const [admins, setAdmins] = useState<UserWithRole[]>([]);
  const [selectedNewOwner, setSelectedNewOwner] = useState("");
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [transferring, setTransferring] = useState(false);
  const [deleting, setDeleting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [success, setSuccess] = useState<string | null>(null);
  const [deleteConfirmText, setDeleteConfirmText] = useState("");
  const [exporting, setExporting] = useState(false);
  const [exportDone, setExportDone] = useState(false);
  const [includePlanningData, setIncludePlanningData] = useState(false);

  const isOwner = membership?.isOwner ?? false;
  const isBreakGlass = membership?.isBreakGlass ?? false;
  const canManageOrg = isOwner || isBreakGlass;
  const tenantId = membership?.tenantId ?? "";
  const tenantSlug = membership?.slug ?? "";
  const currentUserId = appUser?.id ?? "";

  useEffect(() => {
    if (!membership) return;

    setDisplayName(membership.displayName);
    setOriginalName(membership.displayName);

    // Load admin users for ownership transfer
    async function loadAdmins() {
      try {
        const users = await getUsers();
        // Filter to active admins, excluding current user if they're the owner
        const adminUsers = users.filter(
          (u) =>
            u.role === "admin" &&
            u.status === "active" &&
            u.id !== currentUserId,
        );
        setAdmins(adminUsers);
      } catch (err) {
        logger.error("Failed to load admins:", err);
      } finally {
        setLoading(false);
      }
    }

    if (canManageOrg) {
      loadAdmins();
    } else {
      setLoading(false);
    }
  }, [membership, canManageOrg, currentUserId]);

  const handleSaveName = async () => {
    if (!tenantId || displayName === originalName) return;

    setSaving(true);
    setError(null);
    setSuccess(null);

    try {
      await updateTenant(tenantId, { displayName: displayName.trim() });
      setOriginalName(displayName.trim());
      setSuccess("Organization name updated successfully.");
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "Failed to update organization name.",
      );
    } finally {
      setSaving(false);
    }
  };

  const handleTransferOwnership = async () => {
    if (!tenantId || !selectedNewOwner) return;

    setTransferring(true);
    setError(null);

    try {
      await transferTenantOwnership(tenantId, selectedNewOwner);
      setSuccess(
        "Ownership transferred successfully. You are no longer the owner.",
      );
      // Refresh the page to update membership state
      window.location.reload();
    } catch (err) {
      setError(
        err instanceof Error ? err.message : "Failed to transfer ownership.",
      );
    } finally {
      setTransferring(false);
    }
  };

  const handleExport = async () => {
    setExporting(true);
    setError(null);
    setExportDone(false);

    try {
      const payload = await exportTenantData({
        includeMasterData: true,
        includePlanningData,
      });
      const json = JSON.stringify(payload, null, 2);
      const timestamp = new Date().toISOString().split("T")[0];
      downloadFile(json, `${tenantSlug}-export-${timestamp}.json`, "application/json");
      setExportDone(true);
      setTimeout(() => setExportDone(false), 3000);
    } catch (err) {
      setError(
        err instanceof Error ? err.message : "Failed to export data.",
      );
    } finally {
      setExporting(false);
    }
  };

  const handleDeleteOrganization = async () => {
    if (!tenantId) return;

    setDeleting(true);
    setError(null);

    try {
      await deleteTenant(tenantId);
      clearMembership();
      if (!navigateToApex("/")) window.location.href = "/";
    } catch (err) {
      setError(
        err instanceof Error ? err.message : "Failed to delete organization.",
      );
      setDeleting(false);
    }
  };

  if (loading) {
    return (
      <div className="flex items-center justify-center py-12">
        <Loader2 className="h-6 w-6 animate-spin text-muted-foreground" />
      </div>
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

      {error && (
        <Alert variant="destructive">
          <AlertTriangle className="h-4 w-4" />
          <AlertDescription>{error}</AlertDescription>
        </Alert>
      )}

      {success && (
        <Alert>
          <AlertDescription>{success}</AlertDescription>
        </Alert>
      )}

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
                disabled={
                  saving || displayName === originalName || !displayName.trim()
                }
              >
                {saving ? (
                  <Loader2 className="h-4 w-4 animate-spin" />
                ) : (
                  <Save className="h-4 w-4" />
                )}
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

          <Button onClick={handleExport} disabled={exporting}>
            {exporting ? (
              <Loader2 className="h-4 w-4 animate-spin" />
            ) : exportDone ? (
              <Check className="h-4 w-4" />
            ) : (
              <Download className="h-4 w-4" />
            )}
            <span className="ml-2">
              {exporting
                ? "Exporting..."
                : exportDone
                  ? "Downloaded"
                  : "Export JSON"}
            </span>
          </Button>
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

              <AlertDialog>
                <AlertDialogTrigger asChild>
                  <Button
                    variant="outline"
                    disabled={!selectedNewOwner || transferring}
                  >
                    {transferring && (
                      <Loader2 className="h-4 w-4 animate-spin mr-2" />
                    )}
                    Transfer Ownership
                  </Button>
                </AlertDialogTrigger>
                <AlertDialogContent>
                  <AlertDialogHeader>
                    <AlertDialogTitle>
                      Confirm Ownership Transfer
                    </AlertDialogTitle>
                    <AlertDialogDescription asChild>
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
                    </AlertDialogDescription>
                  </AlertDialogHeader>
                  <AlertDialogFooter>
                    <AlertDialogCancel>Cancel</AlertDialogCancel>
                    <AlertDialogAction onClick={handleTransferOwnership}>
                      Transfer Ownership
                    </AlertDialogAction>
                  </AlertDialogFooter>
                </AlertDialogContent>
              </AlertDialog>
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

          <AlertDialog>
            <AlertDialogTrigger asChild>
              <Button variant="destructive" disabled={deleting}>
                {deleting && <Loader2 className="h-4 w-4 animate-spin mr-2" />}
                Delete Organization
              </Button>
            </AlertDialogTrigger>
            <AlertDialogContent>
              <AlertDialogHeader>
                <AlertDialogTitle>Delete Organization</AlertDialogTitle>
                <AlertDialogDescription asChild>
                  <div>
                    This will permanently delete{" "}
                    <strong>{membership?.displayName}</strong> and all its data.
                    <p className="mt-4">
                      Type <strong>{tenantSlug}</strong> to confirm:
                    </p>
                    <Input
                      value={deleteConfirmText}
                      onChange={(e) => setDeleteConfirmText(e.target.value)}
                      placeholder={tenantSlug}
                      className="mt-2"
                    />
                  </div>
                </AlertDialogDescription>
              </AlertDialogHeader>
              <AlertDialogFooter>
                <AlertDialogCancel onClick={() => setDeleteConfirmText("")}>
                  Cancel
                </AlertDialogCancel>
                <AlertDialogAction
                  onClick={handleDeleteOrganization}
                  disabled={deleteConfirmText !== tenantSlug}
                  className="bg-destructive text-destructive-foreground hover:bg-destructive/90"
                >
                  Delete Organization
                </AlertDialogAction>
              </AlertDialogFooter>
            </AlertDialogContent>
          </AlertDialog>
        </CardContent>
      </Card>
    </div>
  );
}
