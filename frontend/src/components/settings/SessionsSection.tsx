import { useState } from "react";
import { Monitor, Smartphone, LogOut, Trash2, AlertCircle, Loader2 } from "lucide-react";
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
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@foundation/src/components/ui/dialog";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { getSessions, revokeSession, logoutAllSessions } from "@foundation/src/lib/api/security-api";
import { formatDistanceToNow } from "date-fns";

interface SessionsSectionProps {
  onLogoutAll: () => void;
}

function getDeviceIcon(clientInfo: string[]) {
  const clientString = clientInfo.join(" ").toLowerCase();
  if (
    clientString.includes("mobile") ||
    clientString.includes("android") ||
    clientString.includes("ios")
  ) {
    return <Smartphone className="h-4 w-4" />;
  }
  return <Monitor className="h-4 w-4" />;
}

export function SessionsSection({ onLogoutAll }: SessionsSectionProps) {
  const queryClient = useQueryClient();
  const [logoutAllOpen, setLogoutAllOpen] = useState(false);

  const {
    data: sessions = [],
    isLoading: sessionsLoading,
    error: sessionsError,
  } = useQuery({
    queryKey: ["sessions"],
    queryFn: getSessions,
  });

  const revokeSessionMutation = useMutation({
    mutationFn: revokeSession,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["sessions"] });
    },
  });

  const logoutAllMutation = useMutation({
    mutationFn: logoutAllSessions,
    onSuccess: () => {
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
            <p className="text-sm text-muted-foreground">
              No active sessions found.
            </p>
          ) : (
            <div className="space-y-3">
              {sessions.map((session) => (
                <div
                  key={session.id}
                  className="flex items-center justify-between p-3 rounded-lg border bg-card"
                >
                  <div className="flex items-center gap-3">
                    <div className="p-2 rounded-full bg-muted">
                      {getDeviceIcon(session.clients)}
                    </div>
                    <div>
                      <div className="flex items-center gap-2">
                        <span className="font-medium text-sm">
                          {session.clients.length > 0
                            ? session.clients.join(", ")
                            : "Unknown Client"}
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
                      onClick={() => revokeSessionMutation.mutate(session.id)}
                      disabled={revokeSessionMutation.isPending}
                      className="text-destructive hover:text-destructive"
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

      <Dialog open={logoutAllOpen} onOpenChange={setLogoutAllOpen}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Sign Out Everywhere</DialogTitle>
            <DialogDescription>
              This will terminate all your sessions, including this one. You'll
              need to log in again.
            </DialogDescription>
          </DialogHeader>
          <DialogFooter>
            <Button variant="outline" onClick={() => setLogoutAllOpen(false)}>
              Cancel
            </Button>
            <Button
              variant="destructive"
              onClick={() => logoutAllMutation.mutate()}
              disabled={logoutAllMutation.isPending}
            >
              {logoutAllMutation.isPending && (
                <Loader2 className="h-4 w-4 mr-2 animate-spin" />
              )}
              Sign Out Everywhere
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </>
  );
}
