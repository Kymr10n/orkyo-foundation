import { useEffect, useMemo, useRef, useState } from "react";
import { useNavigate } from "react-router";
import { Button } from "@foundation/src/components/ui/button";
import { Badge } from "@foundation/src/components/ui/badge";
import {
  AlertTriangle,
  ArrowLeft,
  ArrowRight,
  BarChart3,
  Box,
  Boxes,
  Building2,
  CheckSquare,
  Compass,
  LayoutDashboard,
  Package,
  Settings,
  Users,
  X,
} from "lucide-react";
import { markTourSeen } from "@foundation/src/lib/api/session-api";
import { ROUTE_CONFIGURATION, ROUTE_SETTINGS } from "@foundation/src/constants/auth";
import { useAuth } from "@foundation/src/contexts/AuthContext";
import { useCanEdit, useIsTenantAdmin } from "@foundation/src/hooks/usePermissions";
import { logger } from "@foundation/src/lib/core/logger";

interface TourStep {
  icon: React.ComponentType<{ className?: string }>;
  title: string;
  description: string;
  detail: string;
  /** Route to navigate to when this step is shown. Omitted = stay on the current page. */
  path?: string;
  /** Editor-only destination (e.g. Settings) — hidden for viewers so the tour never bounces them. */
  requiresEditor?: boolean;
  /** Administrator-only destination (Configuration) — hidden for everyone else, same reason. */
  requiresAdmin?: boolean;
}

const STEPS: TourStep[] = [
  {
    icon: Compass,
    title: "Welcome to Orkyo",
    description: "A quick guided tour of the product.",
    detail:
      "We'll walk through the main areas — each step opens the matching page so you can see it for real. Use Next and Back to move; close any time with Done. Let's go.",
    // No path — opening the tour shouldn't move you until you click Next.
  },
  {
    icon: Boxes,
    title: "Your resource types",
    description: "Decide what this workspace schedules.",
    detail:
      "Nothing is built in. Switch on the kinds you run — mills, benches, people, forklifts — from the catalog, or define one nobody thought of. Each type carries its own fields and the lists it needs, and every step after this one works with whatever you chose here.",
    path: `${ROUTE_CONFIGURATION}/catalog`,
    requiresAdmin: true,
  },
  {
    icon: CheckSquare,
    title: "Criteria",
    description: "Define what properties matter for your resources.",
    detail:
      "Criteria are the attributes you match on — a capability a resource offers, a requirement a job asks for. Set these up so Orkyo can tell which resources can actually do a piece of work.",
    path: `${ROUTE_SETTINGS}/criteria`,
    requiresEditor: true,
  },
  {
    icon: Settings,
    title: "Templates",
    description: "Standardize resource definitions with reusable templates.",
    detail:
      "Templates bundle a set of criteria into a reusable blueprint. New resources can inherit from a template, saving time and keeping your data consistent.",
    path: `${ROUTE_SETTINGS}/templates`,
    requiresEditor: true,
  },
  {
    icon: Box,
    title: "Stations",
    description: "Manage the fixed places you schedule work into.",
    detail:
      "A station is a resource with a fixed location — a mill, a cell, an assembly bay. Pick a type from the selector, then work through its tabs: the list, the Groups that cluster them, and the site Floorplan they stand on.",
    // No type key: the page lands on the first station type this workspace activated, and
    // any key we could name here is one a tenant may not have.
    path: "/stations",
  },
  {
    icon: Users,
    title: "Assets",
    description: "Manage the mobile resources you schedule.",
    detail:
      "An asset moves: a person, a tool, a vehicle. People carry skills, working availability and absences. Groups work the same way here as on stations, clustering by crew or function.",
    path: "/assets",
  },
  {
    icon: Building2,
    title: "Organization",
    description: "The reference data your workspace keeps about itself.",
    detail:
      "Departments, job titles and any other shared list you define. Departments form a real tree — each one points at its parent — and people reference these values rather than repeating them.",
    path: "/organization",
  },
  {
    icon: Package,
    title: "Requests",
    description: "Capture the work that needs scheduling.",
    detail:
      "A request is a piece of work with requirements attached. Orkyo matches it against your resources, so you find out what can satisfy it before you commit to a date.",
    path: "/requests",
  },
  {
    icon: AlertTriangle,
    title: "Conflicts",
    description: "See what does not add up, as it happens.",
    detail:
      "Overbooked resources, missing capabilities, work booked over someone's absence or a site shutdown. Conflicts surface the moment they are created rather than on the morning the job was due to run.",
    path: "/insights/conflicts",
  },
  {
    icon: LayoutDashboard,
    title: "Utilization",
    description: "The board where the plan gets built.",
    detail:
      "One row per resource across time. Drag requests into place, or let auto-scheduling propose placements and tell you what it could not fit and why.",
    path: "/",
  },
  {
    icon: BarChart3,
    title: "Insights",
    description: "How your capacity is actually used.",
    detail:
      "Utilization per resource type, conflict counts and trends over a period you choose. This is where a chronically overbooked machine or a quiet quarter shows up.",
    path: "/insights/overview",
  },
];

interface TourDialogProps {
  open: boolean;
  onClose: () => void;
}

export function TourDialog({ open, onClose }: TourDialogProps) {
  const navigate = useNavigate();
  const { appUser, setAppUser } = useAuth();
  const canEdit = useCanEdit();
  const isAdmin = useIsTenantAdmin();

  // Nobody is walked to a page their role bounces them off: Settings is RequireEditor and
  // Configuration is admin-only, so those steps simply are not part of the tour they get.
  const steps = useMemo(
    () => STEPS.filter((s) => (!s.requiresEditor || canEdit) && (!s.requiresAdmin || isAdmin)),
    [canEdit, isAdmin],
  );

  const [step, setStep] = useState(0);
  const current = steps[Math.min(step, steps.length - 1)];
  const isLast = step >= steps.length - 1;

  const handleClose = async () => {
    // Reflect completion in local auth state so the dismissal survives AppLayout remounts.
    if (appUser && !appUser.hasSeenTour) {
      setAppUser({ ...appUser, hasSeenTour: true });
    }
    try {
      await markTourSeen();
    } catch (err) {
      logger.error("Failed to persist tour-seen", err);
    }
    onClose();
  };

  // Restart at the welcome step each time the tour opens — render-phase, not an effect (see useEntityFormDialog.ts).
  const [syncedOpen, setSyncedOpen] = useState(open);
  if (syncedOpen !== open) {
    setSyncedOpen(open);
    if (open) setStep(0);
  }

  // Each step navigates to its page so the user browses the app behind the panel — once per
  // step, which the ref is what guarantees.
  //
  // Without it the tour fought the router. `navigate` takes a new identity on every pathname
  // change under BrowserRouter, and a step can point at a route that redirects on arrival:
  // /stations sends you to the first station type's list. The redirect changed the pathname,
  // which changed `navigate`, which re-ran this effect, which pushed /stations again — an
  // endless loop at frame speed, showing the blank half of the redirect each time.
  const navigatedForStep = useRef<number | null>(null);
  useEffect(() => {
    if (!open) {
      // Reopening the tour has to navigate again, so forget what this run did.
      navigatedForStep.current = null;
      return;
    }
    if (current?.path && navigatedForStep.current !== step) {
      navigatedForStep.current = step;
      navigate(current.path);
    }
  }, [open, step, current?.path, navigate]);

  // Esc closes (the panel is non-modal, so wire this up ourselves).
  useEffect(() => {
    if (!open) return;
    const onKey = (e: KeyboardEvent) => { if (e.key === "Escape") void handleClose(); };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open]);

  if (!open || !current) return null;
  const Icon = current.icon;

  return (
    <div
      role="dialog"
      aria-label="Product tour"
      className="fixed bottom-4 right-4 z-50 w-[360px] max-w-[calc(100vw-2rem)] rounded-lg border bg-background p-5 shadow-lg"
    >
      <div className="flex items-center justify-between">
        <Badge variant="outline" className="text-xs font-normal">
          {step + 1} / {steps.length}
        </Badge>
        <Button
          variant="ghost"
          size="icon-sm"
          onClick={handleClose}
          aria-label="Close tour"
        >
          <X className="h-4 w-4" />
        </Button>
      </div>

      <div className="flex items-center gap-3 pt-2">
        <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-full bg-primary/10">
          <Icon className="h-5 w-5 text-primary" />
        </div>
        <h2 className="text-lg font-semibold">{current.title}</h2>
      </div>

      <div className="space-y-3 py-3">
        <p className="text-sm font-medium text-foreground">{current.description}</p>
        <p className="text-sm text-muted-foreground leading-relaxed">{current.detail}</p>
      </div>

      {/* Step dots */}
      <div className="flex justify-center gap-1.5 py-1">
        {steps.map((_, i) => (
          <button
            key={i}
            onClick={() => setStep(i)}
            className={`h-1.5 rounded-full transition-all ${
              i === step ? "w-4 bg-primary" : "w-1.5 bg-muted-foreground/30"
            }`}
            aria-label={`Go to step ${i + 1}`}
          />
        ))}
      </div>

      <div className="flex items-center justify-between pt-2">
        <Button
          variant="ghost"
          size="sm"
          onClick={() => setStep((s) => s - 1)}
          disabled={step === 0}
        >
          <ArrowLeft className="h-4 w-4 mr-1" />
          Back
        </Button>
        {isLast ? (
          <Button size="sm" onClick={handleClose}>
            <X className="h-4 w-4 mr-1" />
            Done
          </Button>
        ) : (
          <Button size="sm" onClick={() => setStep((s) => s + 1)}>
            Next
            <ArrowRight className="h-4 w-4 ml-1" />
          </Button>
        )}
      </div>
    </div>
  );
}
