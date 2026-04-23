/**
 * PlanCards Component
 * 
 * Displays the available service tiers (Free, Professional, Enterprise)
 * with their limits and features. Allows users to register interest
 * in premium tiers that are coming soon.
 *
 * Plan data is generated from requirements/orkyo-plan-matrix.yaml.
 * Regenerate with:  node scripts/generate-plan-data.mjs
 */

import { useState } from "react";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@foundation/src/components/ui/card";
import { Badge } from "@foundation/src/components/ui/badge";
import { Button } from "@foundation/src/components/ui/button";
import { Input } from "@foundation/src/components/ui/input";
import { Alert, AlertDescription } from "@foundation/src/components/ui/alert";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@foundation/src/components/ui/dialog";
import { Check, Sparkles, Building, Loader2, Mail } from "lucide-react";
import { registerInterest } from "@foundation/src/lib/api/interest-api";
import { useAuth } from "@foundation/src/contexts/AuthContext";
import { PLAN_LIMITS, PLAN_FEATURES } from "@foundation/src/lib/generated/plan-data";

interface PlanCardsProps {
  /** User's email, pre-filled in interest form. Falls back to auth context. */
  userEmail?: string;
}

export function PlanCards({ userEmail }: PlanCardsProps) {
  const { membership, appUser } = useAuth();
  const effectiveEmail = userEmail ?? appUser?.email ?? "";
  const organizationId = membership?.tenantId;

  const [interestDialogOpen, setInterestDialogOpen] = useState(false);
  const [selectedTier, setSelectedTier] = useState<"professional" | "enterprise">("professional");
  const [email, setEmail] = useState(effectiveEmail);
  const [submitting, setSubmitting] = useState(false);
  const [submitted, setSubmitted] = useState<Record<string, boolean>>({});
  const [error, setError] = useState<string | null>(null);

  const handleRegisterInterest = (tier: "professional" | "enterprise") => {
    setSelectedTier(tier);
    setEmail(effectiveEmail);
    setError(null);
    setInterestDialogOpen(true);
  };

  const handleSubmitInterest = async () => {
    if (!email.trim()) {
      setError("Please enter your email address.");
      return;
    }

    setSubmitting(true);
    setError(null);

    try {
      await registerInterest({
        email: email.trim(),
        tier: selectedTier === "professional" ? 1 : 2,
        organizationId: organizationId,
        source: "onboarding",
      });
      setSubmitted((prev) => ({ ...prev, [selectedTier]: true }));
      setInterestDialogOpen(false);
    } catch (err) {
      // "already registered" is a success case
      if (err instanceof Error && err.message.toLowerCase().includes("already registered")) {
        setSubmitted((prev) => ({ ...prev, [selectedTier]: true }));
        setInterestDialogOpen(false);
      } else {
        setError(err instanceof Error ? err.message : "Failed to register interest.");
      }
    } finally {
      setSubmitting(false);
    }
  };

  const tierLabel = selectedTier === "professional" ? "Professional" : "Enterprise";

  return (
    <div className="space-y-4">
      <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
        {/* Free Tier */}
        <Card className="border-primary">
          <CardHeader className="pb-3">
            <div className="flex items-center justify-between">
              <CardTitle className="text-lg">Free</CardTitle>
              <Badge>Current</Badge>
            </div>
            <CardDescription>Get started for free</CardDescription>
          </CardHeader>
          <CardContent className="space-y-3">
            <div className="space-y-1.5">
              {PLAN_LIMITS.map((limit) => (
                <div key={limit.label} className="flex justify-between text-sm">
                  <span className="text-muted-foreground">{limit.label}</span>
                  <span className="font-medium">{String(limit.free)}</span>
                </div>
              ))}
            </div>
            <div className="border-t pt-3 space-y-1.5">
              {PLAN_FEATURES.filter((f) => f.free === true).map((feature) => (
                <div key={feature.label} className="flex items-center gap-2 text-sm">
                  <Check className="h-3.5 w-3.5 text-primary shrink-0" />
                  <span>{feature.label}</span>
                </div>
              ))}
            </div>
          </CardContent>
        </Card>

        {/* Professional Tier */}
        <Card className="relative">
          <div className="absolute -top-2.5 left-1/2 -translate-x-1/2">
            <Badge variant="secondary" className="bg-amber-100 text-amber-800 dark:bg-amber-900 dark:text-amber-200">
              Coming Soon
            </Badge>
          </div>
          <CardHeader className="pb-3">
            <div className="flex items-center justify-between">
              <CardTitle className="text-lg flex items-center gap-1.5">
                <Sparkles className="h-4 w-4 text-amber-500" />
                Professional
              </CardTitle>
            </div>
            <CardDescription>For growing teams</CardDescription>
          </CardHeader>
          <CardContent className="space-y-3">
            <div className="space-y-1.5">
              {PLAN_LIMITS.map((limit) => (
                <div key={limit.label} className="flex justify-between text-sm">
                  <span className="text-muted-foreground">{limit.label}</span>
                  <span className="font-medium">{String(limit.professional)}</span>
                </div>
              ))}
            </div>
            <div className="border-t pt-3 space-y-1.5">
              {PLAN_FEATURES.filter((f) => f.professional === true).map((feature) => (
                <div key={feature.label} className="flex items-center gap-2 text-sm">
                  <Check className="h-3.5 w-3.5 text-amber-500 shrink-0" />
                  <span>{feature.label}</span>
                </div>
              ))}
            </div>
            <div className="pt-2">
              {submitted.professional ? (
                <p className="text-sm text-center text-muted-foreground">
                  <Check className="inline h-3.5 w-3.5 mr-1" />
                  Interest registered
                </p>
              ) : (
                <Button
                  variant="outline"
                  size="sm"
                  className="w-full"
                  onClick={() => handleRegisterInterest("professional")}
                >
                  <Mail className="h-3.5 w-3.5 mr-1.5" />
                  Notify me
                </Button>
              )}
            </div>
          </CardContent>
        </Card>

        {/* Enterprise Tier */}
        <Card className="relative">
          <div className="absolute -top-2.5 left-1/2 -translate-x-1/2">
            <Badge variant="secondary" className="bg-purple-100 text-purple-800 dark:bg-purple-900 dark:text-purple-200">
              Coming Soon
            </Badge>
          </div>
          <CardHeader className="pb-3">
            <div className="flex items-center justify-between">
              <CardTitle className="text-lg flex items-center gap-1.5">
                <Building className="h-4 w-4 text-purple-500" />
                Enterprise
              </CardTitle>
            </div>
            <CardDescription>Custom deployment &amp; SLA</CardDescription>
          </CardHeader>
          <CardContent className="space-y-3">
            <div className="space-y-1.5">
              {PLAN_LIMITS.map((limit) => (
                <div key={limit.label} className="flex justify-between text-sm">
                  <span className="text-muted-foreground">{limit.label}</span>
                  <span className="font-medium">{String(limit.enterprise)}</span>
                </div>
              ))}
            </div>
            <div className="border-t pt-3 space-y-1.5">
              {PLAN_FEATURES.filter((f) => f.enterprise === true).map((feature) => (
                <div key={feature.label} className="flex items-center gap-2 text-sm">
                  <Check className="h-3.5 w-3.5 text-purple-500 shrink-0" />
                  <span>{feature.label}</span>
                </div>
              ))}
            </div>
            <div className="pt-2">
              {submitted.enterprise ? (
                <p className="text-sm text-center text-muted-foreground">
                  <Check className="inline h-3.5 w-3.5 mr-1" />
                  Interest registered
                </p>
              ) : (
                <Button
                  variant="outline"
                  size="sm"
                  className="w-full"
                  onClick={() => handleRegisterInterest("enterprise")}
                >
                  <Mail className="h-3.5 w-3.5 mr-1.5" />
                  Notify me
                </Button>
              )}
            </div>
          </CardContent>
        </Card>
      </div>

      {/* Interest Registration Dialog */}
      <Dialog open={interestDialogOpen} onOpenChange={setInterestDialogOpen}>
        <DialogContent className="sm:max-w-md">
          <DialogHeader>
            <DialogTitle>Get notified about {tierLabel}</DialogTitle>
            <DialogDescription>
              We'll let you know when the {tierLabel} plan becomes available.
            </DialogDescription>
          </DialogHeader>
          <div className="space-y-3 py-2">
            {error && (
              <Alert variant="destructive">
                <AlertDescription>{error}</AlertDescription>
              </Alert>
            )}
            <Input
              type="email"
              placeholder="you@example.com"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              disabled={submitting}
              aria-label="Email for interest registration"
            />
          </div>
          <DialogFooter>
            <Button
              variant="outline"
              onClick={() => setInterestDialogOpen(false)}
              disabled={submitting}
            >
              Cancel
            </Button>
            <Button
              onClick={handleSubmitInterest}
              disabled={submitting || !email.trim()}
            >
              {submitting && <Loader2 className="h-4 w-4 animate-spin mr-2" />}
              Register Interest
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  );
}
