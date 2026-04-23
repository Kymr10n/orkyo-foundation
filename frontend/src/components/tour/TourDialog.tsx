import { useState } from "react";
import { useNavigate } from "react-router-dom";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@foundation/src/components/ui/dialog";
import { Button } from "@foundation/src/components/ui/button";
import { Badge } from "@foundation/src/components/ui/badge";
import {
  AlertTriangle,
  ArrowLeft,
  ArrowRight,
  Box,
  CheckSquare,
  LayoutDashboard,
  Package,
  Settings,
  Users,
  X,
} from "lucide-react";
import { markTourSeen } from "@foundation/src/lib/api/session-api";

interface TourStep {
  icon: React.ComponentType<{ className?: string }>;
  title: string;
  description: string;
  detail: string;
  action?: { label: string; path: string };
}

const STEPS: TourStep[] = [
  {
    icon: CheckSquare,
    title: "Criteria",
    description: "Define what properties matter for your spaces.",
    detail:
      "Criteria are the attributes you track per space — things like capacity, equipment, or accessibility. Set these up first so you can classify and filter spaces consistently.",
    action: { label: "Go to Criteria", path: "/settings?tab=criteria" },
  },
  {
    icon: Settings,
    title: "Space Templates",
    description: "Standardise space definitions with reusable templates.",
    detail:
      "Templates bundle a set of criteria into a reusable blueprint. When you create spaces they can inherit from a template, saving time and keeping your data consistent.",
    action: { label: "Go to Templates", path: "/settings?tab=templates" },
  },
  {
    icon: Users,
    title: "Groups",
    description: "Organise spaces and users into logical groups.",
    detail:
      "Groups let you cluster spaces by floor, building, or team, and control which users have access to manage them. Set these up before creating spaces so you can assign them right away.",
    action: { label: "Go to Groups", path: "/settings?tab=groups" },
  },
  {
    icon: Box,
    title: "Spaces",
    description: "Create and manage the physical spaces you want to track.",
    detail:
      "Spaces are the core of Orkyo — a desk, meeting room, lab bench, or any area you want to monitor. Attach criteria values, assign groups, and describe capabilities for each space.",
    action: { label: "Go to Spaces", path: "/spaces" },
  },
  {
    icon: AlertTriangle,
    title: "Conflicts",
    description: "Spot and resolve overlapping bookings automatically.",
    detail:
      "The Conflicts view surfaces double-bookings and over-capacity situations in real time. Review and resolve them before they cause disruption.",
    action: { label: "Go to Conflicts", path: "/conflicts" },
  },
  {
    icon: Package,
    title: "Requests",
    description: "Handle space booking requests from your team.",
    detail:
      "When users need a space they submit a request. You can review, approve, or decline requests here, keeping a clear audit trail of who is using what and when.",
    action: { label: "Go to Requests", path: "/requests" },
  },
  {
    icon: LayoutDashboard,
    title: "Utilization",
    description: "See how your spaces are being used over time.",
    detail:
      "The Utilization view gives you a timeline of all spaces. Use the date navigator to move through time and quickly spot under-used or over-booked areas.",
    action: { label: "Go to Utilization", path: "/" },
  },
];

interface TourDialogProps {
  open: boolean;
  onClose: () => void;
}

export function TourDialog({ open, onClose }: TourDialogProps) {
  const [step, setStep] = useState(0);
  const navigate = useNavigate();

  const current = STEPS[step];
  const Icon = current.icon;
  const isLast = step === STEPS.length - 1;

  const handleClose = async () => {
    try {
      await markTourSeen();
    } catch {
      // Non-fatal — the flag will be set on next interaction
    }
    onClose();
  };

  const handleNavigate = (path: string) => {
    handleClose();
    navigate(path);
  };

  return (
    <Dialog open={open} onOpenChange={(isOpen) => { if (!isOpen) handleClose(); }}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <div className="flex items-center justify-between">
            <Badge variant="outline" className="text-xs font-normal">
              {step + 1} / {STEPS.length}
            </Badge>
          </div>
          <div className="flex items-center gap-3 pt-2">
            <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-full bg-primary/10">
              <Icon className="h-5 w-5 text-primary" />
            </div>
            <DialogTitle className="text-lg">{current.title}</DialogTitle>
          </div>
        </DialogHeader>

        <DialogDescription className="sr-only">{current.description}</DialogDescription>

        <div className="space-y-3 py-2">
          <p className="text-sm font-medium text-foreground">{current.description}</p>
          <p className="text-sm text-muted-foreground leading-relaxed">{current.detail}</p>
        </div>

        {/* Step dots */}
        <div className="flex justify-center gap-1.5 py-1">
          {STEPS.map((_, i) => (
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

        <DialogFooter className="flex-col gap-2 sm:flex-row sm:justify-between">
          <div className="flex gap-2">
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

          {current.action && (
            <Button
              variant="outline"
              size="sm"
              onClick={() => handleNavigate(current.action!.path)}
            >
              {current.action.label}
            </Button>
          )}
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
