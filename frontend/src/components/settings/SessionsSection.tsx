import { useState } from "react";
import { Monitor, Smartphone, Tablet, LogOut, Trash2, AlertCircle, Loader2 } from "lucide-react";
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
import { EmptyState } from "@foundation/src/components/ui/EmptyState";
import { ConfirmDialog } from "@foundation/src/components/ui/ConfirmDialog";
import { useQuery, useMutation } from "@tanstack/react-query";
import { getSessions, revokeSession, logoutAllSessions } from "@foundation/src/lib/api/security-api";
import { qk } from "@foundation/src/lib/api/query-keys";
import { formatDistanceToNow } from "date-fns";

interface SessionsSectionProps {
  onLogoutAll: () => void;
}

type SessionItem = Awaited<ReturnType<typeof getSessions>>[number];

function getDeviceIcon(session: SessionItem) {
  if (session.deviceType === "mobile") return <Smartphone className="h-4 w-4" />;
  if (session.deviceType === "tablet") return <Tablet className="h-4 w-4" />;
  if (!session.deviceType) {
    // Fall back to client-string keyword matching for sessions predating capture.
    const clientString = session.clients.join(" ").toLowerCase();
    if (clientString.includes("mobile") || clientString.includes("android") || clientString.includes("ios")) {
      return <Smartphone className="h-4 w-4" />;
    }
  }
  return <Monitor className="h-4 w-4" />;
}

function getDeviceLabel(session: SessionItem): string {
  if (session.deviceLabel) return session.deviceLabel;
  if (session.browser && session.operatingSystem) return `${session.browser} on ${session.operatingSystem}`;
  if (session.browser) return session.browser;
  if (session.operatingSystem) return session.operatingSystem;
  if (session.clients.length > 0) return session.clients.join(", ");
  return "Unknown device";
}

export function SessionsSection({ onLogoutAll }: SessionsSectionProps) {
  const [logoutAllOpen, setLogoutAllOpen] = useState(false);
  const [revokeTarget, setRevokeTarget] = useState<SessionItem | null>(null);

  const {
    data: sessions = [],
    isLoading: sessionsLoading,
    error: sessionsError,
  } = useQuery({
    queryKey: qk.sessions.all(),
    queryFn: getSessions,
  });

  const revokeSessionMutation = useMutation({
    mutationFn: revokeSession,
    meta: {
      successMessage: "Session signed out",
      errorMessage: "Failed to sign out session",
      invalidates: [qk.sessions.all()],
    },
    onSuccess: () => setRevokeTarget(null),
  });

  const logoutAllMutation = useMutation({
    mutationFn: logoutAllSessions,
    meta: {
      successMessage: "Signed out everywhere",
      errorMessage: "Failed to sign out everywhere",
    },
    onSuccess: () => {
      setLogoutAllOpen(false);
      onLogoutAll();
    },
  });

  return (
    <>
      <Card>
        <CardHeader>
          <div className="flex items-center justify-between">
            <div>
              <CardTitle className="flex items-center gap-2">
                <Monitor className="h-5 w-5" />
                Active Sessions
              </CardTitle>
              <CardDescription>
                Manage your logged-in sessions across devices
              </CardDescription>
            </div>
            <Button
              variant="outline"
              size="sm"
              onClick={() => setLogoutAllOpen(true)}
              disabled={sessions.length <= 1}
            >
              <LogOut className="h-4 w-4 mr-2" />
              Sign Out Everywhere
            </Button>
          </div>
        </CardHeader>
        <CardContent>
          {sessionsLoading ? (
            <div className="flex items-center justify-center py-8">
              <Loader2 className="h-6 w-6 animate-spin text-muted-foreground" />
            </div>
          ) : sessionsError ? (
            <Alert variant="destructive">
              <AlertCircle className="h-4 w-4" />
              <AlertDescription>
                Failed to load sessions. Please try again.
              </AlertDescription>
            </Alert>
          ) : sessions.length === 0 ? (
            <EmptyState message="No active sessions found." className="text-sm py-6" />
          ) : (
            <div className="space-y-3">
              {sessions.map((session) => (
                <div
                  key={session.id}
                  className="flex items-center justify-between p-3 rounded-lg border bg-card"
                >
                  <div className="flex items-center gap-3">
                    <div className="p-2 rounded-full bg-muted">
                      {getDeviceIcon(session)}
                    </div>
                    <div>
                      <div className="flex items-center gap-2">
                        <span className="font-medium text-sm">
                          {getDeviceLabel(session)}
                        </span>
                        {session.isCurrent && (
                          <Badge variant="secondary" className="text-xs">
                            Current
                          </Badge>
                        )}
                      </div>
                      <div className="text-xs text-muted-foreground">
                        {session.ipAddress} • Last active{" "}
                        {formatDistanceToNow(new Date(session.lastAccessTime), {
                          addSuffix: true,
                        })}
                      </div>
                    </div>
                  </div>
                  {!session.isCurrent && (
                    <Button
                      variant="ghost"
                      size="sm"
                      onClick={() => setRevokeTarget(session)}
                      className="text-destructive hover:text-destructive"
                      aria-label="Sign out session"
                    >
                      <Trash2 className="h-4 w-4" />
                    </Button>
                  )}
                </div>
              ))}
            </div>
          )}
        </CardContent>
      </Card>

      <ConfirmDialog
        open={revokeTarget !== null}
        onOpenChange={(open) => !open && setRevokeTarget(null)}
        title="Sign out this session?"
        description={
          revokeTarget
            ? `This will sign out the session on ${getDeviceLabel(revokeTarget)} (${revokeTarget.ipAddress}).`
            : ""
        }
        confirmLabel="Sign Out"
        destructive
        isPending={revokeSessionMutation.isPending}
        onConfirm={() => {
          if (revokeTarget) revokeSessionMutation.mutate(revokeTarget.id);
        }}
      />

      <ConfirmDialog
        open={logoutAllOpen}
        onOpenChange={setLogoutAllOpen}
        title="Sign Out Everywhere"
        description="This will terminate all your sessions, including this one. You'll need to log in again."
        confirmLabel="Sign Out Everywhere"
        destructive
        isPending={logoutAllMutation.isPending}
        onConfirm={() => logoutAllMutation.mutate()}
      />
    </>
  );
}
