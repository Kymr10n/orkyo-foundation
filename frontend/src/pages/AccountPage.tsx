/**
 * Account Management Page
 *
 * Shows user's memberships across tenants and allows:
 * - Viewing all memberships
 * - Leaving tenants (with safety checks)
 * - Deleting owned tenants (with confirmation)
 * - Switching between tenants
 * - Managing security settings (password, sessions)
 */

import { useState, useEffect, useCallback } from "react";
import { useNavigate, useSearchParams } from "react-router-dom";
import { Button } from "@foundation/src/components/ui/button";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@foundation/src/components/ui/card";
import { Badge } from "@foundation/src/components/ui/badge";
import { Alert, AlertDescription } from "@foundation/src/components/ui/alert";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@foundation/src/components/ui/tabs";
import { Input } from "@foundation/src/components/ui/input";
import { Label } from "@foundation/src/components/ui/label";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@foundation/src/components/ui/dialog";
import {
  Building2,
  LogOut,
  Trash2,
  ArrowRight,
  Loader2,
  Crown,
  AlertCircle,
  ChevronLeft,
  Shield,
  Sparkles,
  User,
  Pencil,
  Check,
} from "lucide-react";
import { useAuth } from "@foundation/src/contexts/AuthContext";
import { SecuritySettings } from "@foundation/src/components/settings/SecuritySettings";
import { PlanCards } from "@foundation/src/components/plans/PlanCards";
import {
  getTenantMemberships,
  leaveTenant,
  deleteTenant,
  type TenantMembership,
} from "@foundation/src/lib/api/tenant-account-api";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { getUserProfile, updateUserProfile } from "@foundation/src/lib/api/security-api";
import {
  navigateToTenantSubdomain,
  navigateToApex,
} from "@foundation/src/lib/utils/tenant-navigation";
import { logger } from "@foundation/src/lib/core/logger";

type Membership = TenantMembership;

const roleColors: Record<string, string> = {
  admin: "bg-red-100 text-red-800 dark:bg-red-900 dark:text-red-200",
  editor: "bg-blue-100 text-blue-800 dark:bg-blue-900 dark:text-blue-200",
  viewer: "bg-gray-100 text-gray-800 dark:bg-gray-700 dark:text-gray-200",
};

export function AccountPage() {
  const navigate = useNavigate();
  const [searchParams, setSearchParams] = useSearchParams();
  const {
    membership: activeMembership,
    setMembership,
    logout,
    appUser,
    isSiteAdmin,
    setAppUser,
    send,
  } = useAuth();
  const queryClient = useQueryClient();
  const tabParam = searchParams.get("tab");
  const [activeTab, setActiveTab] = useState(tabParam || "profile");
  const [memberships, setMemberships] = useState<Membership[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [actionLoading, setActionLoading] = useState<string | null>(null);
  const [leaveDialogOpen, setLeaveDialogOpen] = useState(false);
  const [deleteDialogOpen, setDeleteDialogOpen] = useState(false);
  const [selectedTenant, setSelectedTenant] = useState<Membership | null>(null);
  const [isEditingName, setIsEditingName] = useState(false);
  const [nameForm, setNameForm] = useState({ firstName: "", lastName: "" });

  // Load user profile from Keycloak
  const { data: profile, isLoading: profileLoading } = useQuery({
    queryKey: ["user-profile"],
    queryFn: getUserProfile,
  });

  // Update profile mutation
  const updateProfileMutation = useMutation({
    mutationFn: updateUserProfile,
    onSuccess: (data) => {
      queryClient.invalidateQueries({ queryKey: ["user-profile"] });
      // Update auth context so TopBar and header reflect the new name immediately
      if (appUser && data.displayName) {
        setAppUser({ ...appUser, displayName: data.displayName });
      }
      setIsEditingName(false);
    },
  });

  const handleStartEditName = () => {
    setNameForm({
      firstName: profile?.firstName ?? "",
      lastName: profile?.lastName ?? "",
    });
    setIsEditingName(true);
  };

  const handleSaveName = () => {
    updateProfileMutation.mutate(nameForm);
  };

  const handleCancelEditName = () => {
    setIsEditingName(false);
    updateProfileMutation.reset();
  };

  // Sync tab from URL param
  useEffect(() => {
    if (tabParam) {
      setActiveTab(tabParam);
    }
  }, [tabParam]);

  const handleTabChange = (tab: string) => {
    setActiveTab(tab);
    const newParams = new URLSearchParams(searchParams);
    newParams.set("tab", tab);
    setSearchParams(newParams, { replace: true });
  };

  const loadMemberships = useCallback(async () => {
    try {
      const data = await getTenantMemberships();
      setMemberships(data);
    } catch (err) {
      logger.error("Failed to load memberships:", err);
      // If unauthorized, signal the machine — it handles the redirect
      if (err instanceof Error && err.message.includes("401")) {
        send({ type: "UNAUTHORIZED" });
        return;
      }
      setError(
        err instanceof Error ? err.message : "Failed to load memberships",
      );
    } finally {
      setLoading(false);
    }
  }, [send]);

  useEffect(() => {
    loadMemberships();
  }, [loadMemberships]);

  const handleSwitchTenant = (membership: Membership) => {
    // In subdomain mode, redirect to /login?auto=1 on the tenant subdomain
    // so OIDC re-auth establishes a session on the new origin automatically.
    if (navigateToTenantSubdomain(membership.tenantSlug, "/login?auto=1"))
      return;

    // Fallback for local dev
    setMembership({
      tenantId: membership.tenantId,
      slug: membership.tenantSlug,
      displayName: membership.tenantDisplayName,
      role: membership.role,
      state: membership.tenantStatus,
      isTenantAdmin: membership.role === "admin",
      isOwner: membership.isOwner,
    });
    navigate("/", { replace: true });
  };

  const handleLeave = async () => {
    if (!selectedTenant) return;

    setActionLoading(selectedTenant.tenantId);
    setError(null);

    try {
      await leaveTenant(selectedTenant.tenantId);

      // If we left the active tenant, send back through the apex pipeline
      if (activeMembership?.tenantId === selectedTenant.tenantId) {
        if (!navigateToApex("/")) window.location.href = "/";
        return;
      }

      // Reload memberships
      await loadMemberships();
    } catch (err) {
      logger.error("Leave tenant error:", err);
      setError(err instanceof Error ? err.message : "Failed to leave tenant");
    } finally {
      setActionLoading(null);
      setLeaveDialogOpen(false);
      setSelectedTenant(null);
    }
  };

  const handleDelete = async () => {
    if (!selectedTenant) return;

    setActionLoading(selectedTenant.tenantId);
    setError(null);

    try {
      await deleteTenant(selectedTenant.tenantId);

      // If we deleted the active tenant, send back through the apex pipeline
      if (activeMembership?.tenantId === selectedTenant.tenantId) {
        if (!navigateToApex("/")) window.location.href = "/";
        return;
      }

      // Reload memberships
      await loadMemberships();
    } catch (err) {
      logger.error("Delete tenant error:", err);
      setError(err instanceof Error ? err.message : "Failed to delete tenant");
    } finally {
      setActionLoading(null);
      setDeleteDialogOpen(false);
      setSelectedTenant(null);
    }
  };

  const handleLogout = () => {
    logout();
  };

  if (loading) {
    return (
      <div className="flex items-center justify-center min-h-[400px]">
        <Loader2 className="h-8 w-8 animate-spin text-primary" />
      </div>
    );
  }

  return (
    <div className="container max-w-3xl py-8 space-y-6">
      <div className="flex items-center gap-4">
        <Button variant="ghost" size="sm" onClick={() => navigate(-1)}>
          <ChevronLeft className="h-4 w-4 mr-1" />
          Back
        </Button>
        <div className="flex items-center gap-4 flex-1">
          <div className="h-12 w-12 rounded-full bg-primary/10 flex items-center justify-center flex-shrink-0">
            <span className="text-lg font-semibold text-primary">
              {appUser?.displayName
                ? appUser.displayName
                    .split(" ")
                    .map((n) => n[0])
                    .slice(0, 2)
                    .join("")
                    .toUpperCase()
                : "?"}
            </span>
          </div>
          <div>
            <h1 className="text-2xl font-bold">
              {appUser?.displayName || "Account"}
            </h1>
            {appUser?.email && (
              <p className="text-muted-foreground">{appUser.email}</p>
            )}
          </div>
        </div>
      </div>

      {error && (
        <Alert variant="destructive">
          <AlertCircle className="h-4 w-4" />
          <AlertDescription>{error}</AlertDescription>
        </Alert>
      )}

      <Tabs value={activeTab} onValueChange={handleTabChange}>
        <TabsList>
          <TabsTrigger value="profile" className="flex items-center gap-2">
            <User className="h-4 w-4" />
            Profile
          </TabsTrigger>
          <TabsTrigger
            value="organizations"
            className="flex items-center gap-2"
          >
            <Building2 className="h-4 w-4" />
            Organizations
          </TabsTrigger>
          <TabsTrigger value="security" className="flex items-center gap-2">
            <Shield className="h-4 w-4" />
            Security
          </TabsTrigger>
          {activeMembership && !isSiteAdmin && (
            <TabsTrigger value="plans" className="flex items-center gap-2">
              <Sparkles className="h-4 w-4" />
              Plans
            </TabsTrigger>
          )}
        </TabsList>

        <TabsContent value="profile" className="mt-6">
          <Card>
            <CardHeader>
              <CardTitle className="flex items-center gap-2">
                <User className="h-5 w-5" />
                Profile Information
              </CardTitle>
              <CardDescription>
                Your personal information from your identity provider
              </CardDescription>
            </CardHeader>
            <CardContent className="space-y-4">
              {profileLoading ? (
                <div className="flex items-center justify-center py-8">
                  <Loader2 className="h-6 w-6 animate-spin text-muted-foreground" />
                </div>
              ) : (
                <>
                  <div className="space-y-4">
                    <div className="space-y-1">
                      <Label className="text-muted-foreground text-xs">
                        Email
                      </Label>
                      <p className="text-sm font-medium">
                        {profile?.email || appUser?.email || "—"}
                      </p>
                    </div>

                    {isEditingName ? (
                      <div className="space-y-3">
                        <div className="grid grid-cols-2 gap-3">
                          <div className="space-y-1">
                            <Label htmlFor="firstName">First Name</Label>
                            <Input
                              id="firstName"
                              value={nameForm.firstName}
                              onChange={(e) =>
                                setNameForm({
                                  ...nameForm,
                                  firstName: e.target.value,
                                })
                              }
                              placeholder="First name"
                            />
                          </div>
                          <div className="space-y-1">
                            <Label htmlFor="lastName">Last Name</Label>
                            <Input
                              id="lastName"
                              value={nameForm.lastName}
                              onChange={(e) =>
                                setNameForm({
                                  ...nameForm,
                                  lastName: e.target.value,
                                })
                              }
                              placeholder="Last name"
                            />
                          </div>
                        </div>
                        {updateProfileMutation.isError && (
                          <Alert variant="destructive">
                            <AlertCircle className="h-4 w-4" />
                            <AlertDescription>
                              {updateProfileMutation.error?.message ||
                                "Failed to update profile"}
                            </AlertDescription>
                          </Alert>
                        )}
                        <div className="flex gap-2">
                          <Button
                            size="sm"
                            onClick={handleSaveName}
                            disabled={updateProfileMutation.isPending}
                          >
                            {updateProfileMutation.isPending ? (
                              <Loader2 className="h-4 w-4 mr-1 animate-spin" />
                            ) : (
                              <Check className="h-4 w-4 mr-1" />
                            )}
                            Save
                          </Button>
                          <Button
                            size="sm"
                            variant="outline"
                            onClick={handleCancelEditName}
                            disabled={updateProfileMutation.isPending}
                          >
                            Cancel
                          </Button>
                        </div>
                      </div>
                    ) : (
                      <div className="flex items-center justify-between">
                        <div className="space-y-1">
                          <Label className="text-muted-foreground text-xs">
                            Name
                          </Label>
                          <p className="text-sm font-medium">
                            {[profile?.firstName, profile?.lastName]
                              .filter(Boolean)
                              .join(" ") || "Not set"}
                          </p>
                        </div>
                        <Button
                          variant="ghost"
                          size="sm"
                          onClick={handleStartEditName}
                        >
                          <Pencil className="h-4 w-4 mr-1" />
                          Edit
                        </Button>
                      </div>
                    )}
                  </div>
                </>
              )}
            </CardContent>
          </Card>

          {/* Sign out button at the bottom of profile tab */}
          <Card className="mt-6">
            <CardContent className="pt-6">
              <Button variant="outline" onClick={handleLogout}>
                <LogOut className="h-4 w-4 mr-2" />
                Sign out
              </Button>
            </CardContent>
          </Card>
        </TabsContent>

        <TabsContent value="organizations" className="mt-6">
          <Card>
            <CardHeader>
              <CardTitle className="flex items-center gap-2">
                <Building2 className="h-5 w-5" />
                Organizations
              </CardTitle>
              <CardDescription>
                You are a member of {memberships.length} organization
                {memberships.length !== 1 ? "s" : ""}
              </CardDescription>
            </CardHeader>
            <CardContent className="space-y-3">
              {memberships.length === 0 ? (
                <div className="text-center py-8 text-muted-foreground">
                  <p>You are not a member of any organizations.</p>
                  {!isSiteAdmin && (
                    <Button
                      className="mt-4"
                      onClick={() => {
                        if (!navigateToApex("/")) window.location.href = "/";
                      }}
                    >
                      Create Organization
                    </Button>
                  )}
                </div>
              ) : (
                memberships.map((membership) => (
                  <div
                    key={membership.tenantId}
                    className={`flex items-center justify-between p-4 rounded-lg border ${
                      membership.tenantStatus === "deleting" ? "opacity-50" : ""
                    } ${
                      activeMembership?.tenantId === membership.tenantId
                        ? "border-primary bg-primary/5"
                        : ""
                    }`}
                  >
                    <div className="flex items-center gap-3">
                      <div className="w-10 h-10 rounded-lg bg-primary/10 flex items-center justify-center">
                        <Building2 className="h-5 w-5 text-primary" />
                      </div>
                      <div>
                        <div className="flex items-center gap-2">
                          <span className="font-medium">
                            {membership.tenantDisplayName}
                          </span>
                          {membership.isOwner && (
                            <span title="Owner">
                              <Crown className="h-4 w-4 text-yellow-500" />
                            </span>
                          )}
                          {activeMembership?.tenantId ===
                            membership.tenantId && (
                            <Badge variant="outline" className="text-xs">
                              Active
                            </Badge>
                          )}
                        </div>
                        <div className="flex items-center gap-2 text-sm text-muted-foreground">
                          <span>{membership.tenantSlug}</span>
                          <Badge
                            variant="secondary"
                            className={`text-xs ${roleColors[membership.role] || roleColors.viewer}`}
                          >
                            {membership.role}
                          </Badge>
                          {membership.tenantStatus === "suspended" && (
                            <Badge variant="destructive" className="text-xs">
                              Suspended
                            </Badge>
                          )}
                          {membership.tenantStatus === "deleting" && (
                            <Badge variant="destructive" className="text-xs">
                              Deleting
                            </Badge>
                          )}
                        </div>
                      </div>
                    </div>

                    <div className="flex items-center gap-2">
                      {membership.tenantStatus !== "deleting" && membership.tenantStatus !== "suspended" && (
                        <>
                          {activeMembership?.tenantId !==
                            membership.tenantId && (
                            <Button
                              variant="outline"
                              size="sm"
                              onClick={() => handleSwitchTenant(membership)}
                              disabled={actionLoading !== null}
                            >
                              <ArrowRight className="h-4 w-4 mr-1" />
                              Switch
                            </Button>
                          )}

                          {membership.isOwner ? (
                            <Button
                              variant="ghost"
                              size="sm"
                              className="text-destructive hover:text-destructive"
                              onClick={() => {
                                setSelectedTenant(membership);
                                setDeleteDialogOpen(true);
                              }}
                              disabled={actionLoading !== null}
                            >
                              <Trash2 className="h-4 w-4" />
                            </Button>
                          ) : (
                            <Button
                              variant="ghost"
                              size="sm"
                              onClick={() => {
                                setSelectedTenant(membership);
                                setLeaveDialogOpen(true);
                              }}
                              disabled={actionLoading !== null}
                            >
                              <LogOut className="h-4 w-4" />
                            </Button>
                          )}
                        </>
                      )}
                    </div>
                  </div>
                ))
              )}
            </CardContent>
          </Card>

          {/* Sign out button at the bottom of organizations tab */}
          <Card className="mt-6">
            <CardContent className="pt-6">
              <Button variant="outline" onClick={handleLogout}>
                <LogOut className="h-4 w-4 mr-2" />
                Sign out
              </Button>
            </CardContent>
          </Card>
        </TabsContent>

        <TabsContent value="security" className="mt-6">
          <SecuritySettings />

          {/* Sign out button at the bottom of security tab */}
          <Card className="mt-6">
            <CardContent className="pt-6">
              <Button variant="outline" onClick={handleLogout}>
                <LogOut className="h-4 w-4 mr-2" />
                Sign out
              </Button>
            </CardContent>
          </Card>
        </TabsContent>

        {activeMembership && !isSiteAdmin && (
          <TabsContent value="plans" className="mt-6">
            <PlanCards />
          </TabsContent>
        )}
      </Tabs>

      {/* Leave Confirmation Dialog */}
      <Dialog open={leaveDialogOpen} onOpenChange={setLeaveDialogOpen}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Leave Organization?</DialogTitle>
            <DialogDescription>
              Are you sure you want to leave{" "}
              <strong>{selectedTenant?.tenantDisplayName}</strong>? You will
              lose access to this organization until you are invited again.
            </DialogDescription>
          </DialogHeader>
          <DialogFooter>
            <Button
              variant="outline"
              onClick={() => setLeaveDialogOpen(false)}
              disabled={actionLoading !== null}
            >
              Cancel
            </Button>
            <Button
              onClick={handleLeave}
              disabled={actionLoading !== null}
              variant="destructive"
            >
              {actionLoading && (
                <Loader2 className="h-4 w-4 mr-2 animate-spin" />
              )}
              Leave
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      {/* Delete Confirmation Dialog */}
      <Dialog open={deleteDialogOpen} onOpenChange={setDeleteDialogOpen}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Delete Organization?</DialogTitle>
            <DialogDescription>
              Are you sure you want to delete{" "}
              <strong>{selectedTenant?.tenantDisplayName}</strong>? This action
              will initiate deletion of all organization data. This cannot be
              undone.
            </DialogDescription>
          </DialogHeader>
          <DialogFooter>
            <Button
              variant="outline"
              onClick={() => setDeleteDialogOpen(false)}
              disabled={actionLoading !== null}
            >
              Cancel
            </Button>
            <Button
              onClick={handleDelete}
              disabled={actionLoading !== null}
              variant="destructive"
            >
              {actionLoading && (
                <Loader2 className="h-4 w-4 mr-2 animate-spin" />
              )}
              Delete Organization
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  );
}
