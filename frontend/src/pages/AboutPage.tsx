import { Button } from "@foundation/src/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@foundation/src/components/ui/card";
import { Separator } from "@foundation/src/components/ui/separator";
import { ArrowLeft, ExternalLink, Mail } from "lucide-react";
import { useNavigate } from "react-router-dom";

// Build-time metadata injected by vite.config.ts
const APP_VERSION = (typeof __APP_VERSION__ !== "undefined" ? __APP_VERSION__ : null) ?? null;

export function AboutPage() {
  const navigate = useNavigate();
  const currentYear = new Date().getFullYear();

  return (
    <div className="min-h-screen bg-background">
      <div className="container max-w-2xl mx-auto py-8 px-4">
        <Button
          variant="ghost"
          className="mb-6"
          onClick={() => navigate(-1)}
        >
          <ArrowLeft className="h-4 w-4 mr-2" />
          Back
        </Button>

        <Card>
          <CardHeader className="text-center pb-2">
            <div className="mx-auto mb-4">
              <img
                src="/orkyo-180.png"
                alt="Orkyo"
                className="h-16 w-16 object-contain dark:hidden"
              />
              <img
                src="/orkyo-dark-180.png"
                alt="Orkyo"
                className="h-16 w-16 object-contain hidden dark:block"
              />
            </div>
            <CardTitle className="text-2xl">Orkyo</CardTitle>
            <CardDescription>
              Space and resource utilization management
            </CardDescription>
          </CardHeader>

          <CardContent className="space-y-6">
            <div className="text-center">
              <p className="text-sm text-muted-foreground">
                {APP_VERSION ? `v${APP_VERSION}` : "—"}
              </p>
            </div>

            <Separator />

            <div className="space-y-4">
              <h3 className="font-medium">About</h3>
              <p className="text-sm text-muted-foreground">
                Orkyo helps organizations manage their physical spaces, people, and scheduling.
                Track space availability, handle booking requests, detect conflicts in real time,
                and analyse utilization — all from one place.
              </p>
            </div>

            <Separator />

            <div className="space-y-4">
              <h3 className="font-medium">Features</h3>
              <ul className="text-sm text-muted-foreground space-y-2">
                <li>• Spaces — manage rooms and areas with custom criteria, templates, and floorplans</li>
                <li>• Utilization — visual drag-and-drop timeline for scheduling across all spaces</li>
                <li>• Requests — structured booking workflows with approval and template support</li>
                <li>• Conflict detection — real-time overlap and constraint validation</li>
                <li>• People — employee directory with teams, departments, and job titles</li>
                <li>• Reporting — usage analytics and capacity-vs-demand insights</li>
              </ul>
            </div>

            <Separator />

            <div className="space-y-4">
              <h3 className="font-medium">Links</h3>
              <div className="flex flex-col gap-2">
                <Button
                  variant="outline"
                  className="justify-start"
                  onClick={() => window.open("mailto:support@orkyo.app", "_blank")}
                >
                  <Mail className="h-4 w-4 mr-2" />
                  Contact Support
                  <ExternalLink className="h-3 w-3 ml-auto" />
                </Button>
              </div>
            </div>

            <Separator />

            <div className="text-center text-sm text-muted-foreground">
              <p>© {currentYear} Orkyo. All rights reserved.</p>
              <p className="mt-1">
                Version and telemetry data are not shared with third parties.
              </p>
            </div>
          </CardContent>
        </Card>
      </div>
    </div>
  );
}
