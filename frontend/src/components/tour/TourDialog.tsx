import { useEffect, useMemo, useState } from "react";
import { useNavigate } from "react-router-dom";
import { Button } from "@foundation/src/components/ui/button";
import { Badge } from "@foundation/src/components/ui/badge";
import {
  AlertTriangle,
  ArrowLeft,
  ArrowRight,
  BarChart3,
  Box,
  Boxes,
  CheckSquare,
  Compass,
  LayoutDashboard,
  Package,
  Settings,
  Users,
  X,
} from "lucide-react";
import { markTourSeen } from "@foundation/src/lib/api/session-api";
import { useAuth } from "@foundation/src/contexts/AuthContext";
import { useCanEdit } from "@foundation/src/hooks/usePermissions";
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
    icon: CheckSquare,
    title: "Criteria",
    description: "Define what properties matter for your resources.",
    detail:
      "Criteria are the attributes you track per resource — capacity for a space, skills for a person, specs for a tool. Set these up first so you can match the right resources to each request.",
    path: "/settings/criteria",
    requiresEditor: true,
  },
  {
    icon: Settings,
    title: "Templates",
    description: "Standardise resource definitions with reusable templates.",
    detail:
      "Templates bundle a set of criteria into a reusable blueprint. New resources can inherit from a template, saving time and keeping your data consistent.",
    path: "/settings/templates",
    requiresEditor: true,
  },
  {
    icon: Boxes,
    title: "Groups",
    description: "Organise resources into logical groups.",
    detail:
      "Groups let you cluster resources by site, team, or function. Use them to manage related spaces or people together and keep large catalogues navigable.",
    path: "/spaces/groups",
  },
  {
    icon: Box,
    title: "Spaces",
    description: "Manage the spaces you schedule work into.",
    detail:
      "Spaces are one of Orkyo's resource types — a room, lab bench, or any area you book. People and tools are resources too. Attach criteria, assign groups, and describe each space's capabilities.",
    path: "/spaces/list",
  },
  {
    icon: Users,
    title: "People",
    description: "Manage the people you schedule.",
    detail:
      "People are resources with skills, working availability, and absences. Orkyo matches them to requests and tracks how their time is used — keep their profiles and availability up to date.",
    path: "/people/list",
  },
  {
    icon: Package,
    title: "Requests",
    description: "Capture the work that needs scheduling.",
    detail:
      "A request is a piece of work to place onto your resources, with the criteria it requires. Review and schedule requests here, keeping a clear record of who and what is needed, and when.",
    path: "/requests",
  },
  {
    icon: AlertTriangle,
    title: "Conflicts",
    description: "Spot and resolve scheduling problems automatically.",
    detail:
      "The Conflicts view surfaces overbooking, criteria mismatches, and availability clashes in real time. Review and resolve them before they cause disruption.",
    path: "/insights/conflicts",
  },
  {
    icon: LayoutDashboard,
    title: "Utilization",
    description: "See how spaces and people are used over time.",
    detail:
      "The Utilization view gives you a calendar plus per-space and per-person timelines. Use the date navigator to move through time and quickly spot under-used or over-booked resources.",
    path: "/",
  },
  {
    icon: BarChart3,
    title: "Insights",
    description: "Track utilization, conflicts, and requests at a glance.",
    detail:
      "The Insights dashboard summarises your operation: KPI cards plus utilization, conflict, and request-status trends across a period you choose. A quick health check without leaving the app.",
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

  // Viewers can't reach editor-only destinations (Settings is RequireEditor) — drop those steps so
  // the guided browse never navigates to a page that redirects them away.
  const steps = useMemo(
    () => STEPS.filter((s) => !s.requiresEditor || canEdit),
    [canEdit],
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

  // Restart at the welcome step each time the tour opens.
  useEffect(() => {
    if (open) setStep(0);
  }, [open]);

  // Each step navigates to its page so the user browses the app behind the panel.
  useEffect(() => {
    if (open && current?.path) navigate(current.path);
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
          size="icon"
          className="h-7 w-7"
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
