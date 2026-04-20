import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Separator } from "@/components/ui/separator";
import { formatBuildTime } from "@/lib/utils/formatBuildTime";
import { ArrowLeft, ExternalLink, Mail } from "lucide-react";
import { useNavigate } from "react-router-dom";

// Build-time metadata injected by vite.config.ts
const BUILD_TIME = (typeof __BUILD_TIME__ !== "undefined" ? __BUILD_TIME__ : null) ?? null;

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
              <p className="text-sm text-muted-foreground">Deployed</p>
              <p className="text-sm">
                {BUILD_TIME ? formatBuildTime(BUILD_TIME) : "—"}
              </p>
            </div>

            <Separator />

            <div className="space-y-4">
              <h3 className="font-medium">About</h3>
              <p className="text-sm text-muted-foreground">
                Orkyo is a modern space and resource utilization platform designed to help
                organizations efficiently manage their physical spaces, handle utilization
                requests, and resolve scheduling conflicts.
              </p>
            </div>

            <Separator />

            <div className="space-y-4">
              <h3 className="font-medium">Features</h3>
              <ul className="text-sm text-muted-foreground space-y-2">
                <li>• Visual utilization timeline with drag-and-drop</li>
                <li>• Space management with custom criteria</li>
                <li>• Request workflow with templates</li>
                <li>• Multi-tenant organization support</li>
                <li>• Conflict detection and resolution</li>
                <li>• Import/Export capabilities</li>
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
                Built with React, TypeScript, and Tailwind CSS
              </p>
            </div>
          </CardContent>
        </Card>
      </div>
    </div>
  );
}
