/**
 * Onboarding Page
 *
 * Pure UI component — flow control is handled by the caller (ApexGateway)
 * via `onComplete` and `onCancel` callbacks.
 *
 * Two-step wizard:
 *   Step 1 – Organisation name + slug
 *   Step 2 – Choose a starter template (empty / demo / presets)
 */

import { useState, useEffect } from "react";
import { Button } from "@foundation/src/components/ui/button";
import { Input } from "@foundation/src/components/ui/input";
import { Label } from "@foundation/src/components/ui/label";
import { Card, CardContent, CardDescription, CardFooter, CardHeader, CardTitle } from "@foundation/src/components/ui/card";
import { Alert, AlertDescription } from "@foundation/src/components/ui/alert";
import { Building2, Plus, Loader2, LogOut, AlertCircle, ArrowLeft, CheckCircle2, RotateCcw } from "lucide-react";
import { canCreateTenant, createTenant, getStarterTemplates, getTenantMemberships, cancelTenantDeletion, type TenantMembership } from "@foundation/src/lib/api/tenant-account-api";
import { StarterTemplatePicker, type StarterTemplate } from "@foundation/src/components/onboarding/StarterTemplatePicker";
import { logger } from "@foundation/src/lib/core/logger";

interface OnboardingPageProps {
  /** Called after successful tenant creation (e.g. refresh the pipeline). */
  onComplete: () => Promise<void> | void;
  /** Called when the user signs out. */
  onCancel: () => void;
  /** Optional plan comparison section rendered below the wizard (SaaS-injected). */
  renderPlanCards?: () => React.ReactNode;
}

type WizardStep = "form" | "template";

const SLUG_MIN = 3;
const SLUG_MAX = 63;
const SLUG_RE = /^[a-z0-9][a-z0-9-]*[a-z0-9]$|^[a-z0-9]$/;

function validateSlug(value: string): string | null {
  if (value.length < SLUG_MIN) return `Must be at least ${SLUG_MIN} characters.`;
  if (value.length > SLUG_MAX) return `Must be ${SLUG_MAX} characters or fewer.`;
  if (!SLUG_RE.test(value)) return "Lowercase letters, numbers, and hyphens only. Cannot start or end with a hyphen.";
  return null;
}

function slugify(text: string): string {
  return text
    .toLowerCase()
    .trim()
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-+|-+$/g, "")
    .substring(0, SLUG_MAX);
}

/** Simple dot-based step indicator */
function StepIndicator({ current, total }: { current: number; total: number }) {
  return (
    <div className="flex items-center justify-center gap-2 mb-2" aria-label={`Step ${current} of ${total}`}>
      {Array.from({ length: total }, (_, i) => (
        <div
          key={i}
          className={`h-2 rounded-full transition-all ${
            i + 1 === current ? "w-6 bg-primary" : "w-2 bg-muted"
          }`}
        />
      ))}
    </div>
  );
}

export function OnboardingPage({ onComplete, onCancel, renderPlanCards }: OnboardingPageProps) {
  const [canCreate, setCanCreate] = useState<boolean | null>(null);
  const [showCreateForm, setShowCreateForm] = useState(false);
  const [slug, setSlug] = useState("");
  const [slugTouched, setSlugTouched] = useState(false);
  const [displayName, setDisplayName] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [templatesError, setTemplatesError] = useState(false);

  // Wizard state
  const [step, setStep] = useState<WizardStep>("form");
  const [selectedTemplate, setSelectedTemplate] = useState("empty");
  const [templates, setTemplates] = useState<StarterTemplate[]>([]);
  const [deletingTenants, setDeletingTenants] = useState<TenantMembership[]>([]);
  const [restoringId, setRestoringId] = useState<string | null>(null);

  useEffect(() => {
    checkCanCreate();
    loadTemplates();
    loadDeletingTenants();
  }, []);

  const checkCanCreate = async () => {
    try {
      const data = await canCreateTenant();
      setCanCreate(data.canCreate);
    } catch (err) {
      logger.error("Failed to check can create:", err);
      // 401 is handled globally by the API error handler (redirects to apex login)
      setCanCreate(false);
    } finally {
      setLoading(false);
    }
  };

  const loadDeletingTenants = async () => {
    try {
      const memberships = await getTenantMemberships();
      setDeletingTenants(memberships.filter((m) => m.tenantStatus === "deleting" && m.isOwner));
    } catch {
      // Non-critical — just don't show the section
    }
  };

  const handleCancelDeletion = async (tenantId: string) => {
    setRestoringId(tenantId);
    try {
      await cancelTenantDeletion(tenantId);
      await onComplete();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to restore organization");
      setRestoringId(null);
    }
  };

  const loadTemplates = async () => {
    try {
      const data = await getStarterTemplates();
      setTemplates(data);
    } catch (err) {
      logger.error("Failed to load starter templates:", err);
      setTemplatesError(true);
    }
  };

  const slugError = slugTouched ? validateSlug(slug) : null;

  const handleNext = () => {
    setSlugTouched(true);
    if (!displayName.trim() || validateSlug(slug)) return;
    setError(null);
    setStep("template");
  };

  const handleBack = () => {
    setStep("form");
    setError(null);
  };

  const handleCreateTenant = async () => {
    setSlugTouched(true);
    if (!slug.trim() || !displayName.trim()) {
      setError("Please fill in all fields");
      return;
    }

    setSubmitting(true);
    setError(null);

    try {
      await createTenant({
        slug: slug.toLowerCase().trim(),
        displayName: displayName.trim(),
        starterTemplate: selectedTemplate,
      });

      await onComplete();
    } catch (err) {
      logger.error("Tenant creation error:", err);
      setError(err instanceof Error ? err.message : "Failed to create organization");
    } finally {
      setSubmitting(false);
    }
  };

  const handleDisplayNameChange = (value: string) => {
    setDisplayName(value);
    // Auto-generate slug only while user hasn't manually diverged it
    if (!slugTouched || slug === slugify(displayName)) {
      setSlug(slugify(value));
    }
  };

  const handleSlugChange = (value: string) => {
    const sanitized = value.toLowerCase().replace(/[^a-z0-9-]/g, "");
    setSlug(sanitized);
    setSlugTouched(true);
  };

  if (loading) {
    return (
      <div className="min-h-screen flex items-center justify-center bg-background">
        <Loader2 className="h-8 w-8 animate-spin text-primary" />
      </div>
    );
  }

  const wizardActive = showCreateForm;

  return (
    <div className="min-h-screen flex flex-col items-center justify-center bg-background p-4 gap-6">
      <Card className="w-full max-w-lg">
        <CardHeader className="text-center">
          <div className="mx-auto w-12 h-12 rounded-full bg-primary/10 flex items-center justify-center mb-2">
            <Building2 className="h-6 w-6 text-primary" />
          </div>
          <CardTitle>Welcome to Orkyo</CardTitle>
          <CardDescription>
            {!canCreate
              ? "You don't have access to any organizations yet."
              : step === "template" && showCreateForm
                ? "Choose how to set up your workspace."
                : "Get started by creating your organization on the Free plan."}
          </CardDescription>

          {/* Step indicator — only shown inside the active wizard */}
          {showCreateForm && (
            <div className="pt-2">
              <StepIndicator current={step === "form" ? 1 : 2} total={2} />
              <p className="text-xs text-muted-foreground mt-1">
                Step {step === "form" ? "1" : "2"} of 2
              </p>
            </div>
          )}
        </CardHeader>

        <CardContent className="space-y-4">
          {/* Pending-deletion tenants that can be restored */}
          {!showCreateForm && deletingTenants.length > 0 && (
            <div className="space-y-3">
              <p className="text-sm text-muted-foreground text-center">
                You have {deletingTenants.length === 1 ? "an organization" : "organizations"} scheduled for deletion:
              </p>
              {deletingTenants.map((t) => (
                <div
                  key={t.tenantId}
                  className="flex items-center justify-between rounded-lg border border-border p-3"
                >
                  <div className="min-w-0">
                    <p className="font-medium truncate">{t.tenantDisplayName}</p>
                    <p className="text-xs text-muted-foreground">{t.tenantSlug}</p>
                  </div>
                  <Button
                    variant="outline"
                    size="sm"
                    onClick={() => handleCancelDeletion(t.tenantId)}
                    disabled={restoringId === t.tenantId}
                  >
                    {restoringId === t.tenantId ? (
                      <Loader2 className="mr-2 h-3 w-3 animate-spin" />
                    ) : (
                      <RotateCcw className="mr-2 h-3 w-3" />
                    )}
                    Restore
                  </Button>
                </div>
              ))}
              {canCreate && (
                <div className="relative py-2">
                  <div className="absolute inset-0 flex items-center">
                    <span className="w-full border-t" />
                  </div>
                  <div className="relative flex justify-center text-xs uppercase">
                    <span className="bg-card px-2 text-muted-foreground">or</span>
                  </div>
                </div>
              )}
            </div>
          )}

          {canCreate && !showCreateForm && (
            <Button onClick={() => setShowCreateForm(true)} className="w-full" size="lg">
              <Plus className="mr-2 h-4 w-4" />
              Create New Organization
            </Button>
          )}

          {/* Step 1: Org name + slug */}
          {showCreateForm && step === "form" && (
            <div className="space-y-4">
              {error && (
                <Alert variant="destructive">
                  <AlertCircle className="h-4 w-4" />
                  <AlertDescription>{error}</AlertDescription>
                </Alert>
              )}

              <div className="space-y-2">
                <Label htmlFor="displayName">Organization Name</Label>
                <Input
                  id="displayName"
                  placeholder="ACME Corporation"
                  value={displayName}
                  onChange={(e) => handleDisplayNameChange(e.target.value)}
                  disabled={submitting}
                />
              </div>

              <div className="space-y-2">
                <Label htmlFor="slug">URL Identifier</Label>
                <div className="flex items-center gap-2">
                  <span className="text-sm text-muted-foreground whitespace-nowrap">orkyo.app/</span>
                  <div className="relative flex-1">
                    <Input
                      id="slug"
                      placeholder="acme"
                      value={slug}
                      onChange={(e) => handleSlugChange(e.target.value)}
                      onBlur={() => setSlugTouched(true)}
                      disabled={submitting}
                      className={slugError ? "border-destructive pr-8" : slug.length >= SLUG_MIN ? "pr-8" : ""}
                      aria-invalid={!!slugError}
                      aria-describedby={slugError ? "slug-error" : "slug-hint"}
                    />
                    {!slugError && slug.length >= SLUG_MIN && (
                      <CheckCircle2 className="absolute right-2 top-1/2 -translate-y-1/2 h-4 w-4 text-green-500 pointer-events-none" />
                    )}
                  </div>
                </div>
                {slugError ? (
                  <p id="slug-error" className="text-xs text-destructive">{slugError}</p>
                ) : (
                  <p id="slug-hint" className="text-xs text-muted-foreground">
                    {SLUG_MIN}–{SLUG_MAX} characters. Lowercase letters, numbers, and hyphens only.
                  </p>
                )}
              </div>

              <div className="flex gap-2">
                <Button
                  variant="outline"
                  onClick={() => { setShowCreateForm(false); setError(null); setStep("form"); setSlugTouched(false); setSelectedTemplate("empty"); }}
                  disabled={submitting}
                  className="flex-1"
                >
                  Cancel
                </Button>
                <Button
                  onClick={handleNext}
                  disabled={!displayName.trim() || !!validateSlug(slug)}
                  className="flex-1"
                >
                  Next
                </Button>
              </div>
            </div>
          )}

          {/* Step 2: Starter-template picker */}
          {showCreateForm && step === "template" && (
            <div className="space-y-4">
              {error && (
                <Alert variant="destructive">
                  <AlertCircle className="h-4 w-4" />
                  <AlertDescription>{error}</AlertDescription>
                </Alert>
              )}

              {templatesError ? (
                <p className="text-sm text-muted-foreground text-center">
                  Could not load templates — you can still continue with an empty workspace.
                </p>
              ) : templates.length > 0 ? (
                <StarterTemplatePicker
                  templates={templates}
                  selected={selectedTemplate}
                  onSelect={setSelectedTemplate}
                  disabled={submitting}
                />
              ) : (
                <div className="flex justify-center py-4">
                  <Loader2 className="h-5 w-5 animate-spin text-muted-foreground" />
                </div>
              )}

              <div className="flex gap-2">
                <Button
                  variant="outline"
                  onClick={handleBack}
                  disabled={submitting}
                  className="flex-1"
                >
                  <ArrowLeft className="mr-2 h-4 w-4" />
                  Back
                </Button>
                <Button onClick={handleCreateTenant} disabled={submitting} className="flex-1">
                  {submitting && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}
                  {submitting ? "Creating workspace…" : "Create"}
                </Button>
              </div>
            </div>
          )}

          {!showCreateForm && (
            <div className="text-center text-sm text-muted-foreground">
              <p>
                {canCreate
                  ? "Or ask an administrator to invite you to an existing organization."
                  : <>
                      Please contact an administrator to request access.{" "}
                      <a href="mailto:support@orkyo.app" className="underline hover:text-foreground transition-colors">
                        Get help
                      </a>
                    </>
                }
              </p>
            </div>
          )}
        </CardContent>

        <CardFooter className="flex justify-center">
          <Button variant="ghost" size="sm" onClick={onCancel}>
            <LogOut className="mr-2 h-4 w-4" />
            Sign out
          </Button>
        </CardFooter>
      </Card>

      {/* Plan comparison — only show before wizard starts to avoid distraction */}
      {!wizardActive && renderPlanCards && (
        <div className="w-full max-w-4xl">
          <h2 className="text-lg font-semibold text-center mb-4">Available Plans</h2>
          {renderPlanCards()}
        </div>
      )}
    </div>
  );
}
